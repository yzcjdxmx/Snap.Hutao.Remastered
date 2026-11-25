// Copyright (c) Snap Hutao RP. All rights reserved.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Hutao.Remastered.Service.Game.Launching;

internal sealed class PresentMonFpsCapture
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(3);
    private readonly CancellationTokenSource cancellation = new();
    private readonly string sessionName = $"HutaoFpsPreview-{Guid.NewGuid():N}";
    private readonly Task captureTask;
    private FpsReading? reading;

    public PresentMonFpsCapture(int targetProcessId)
    {
        captureTask = Task.Run(() => CaptureAsync(targetProcessId, cancellation.Token));
    }

    public bool Failed => captureTask.IsCompletedSuccessfully && !cancellation.IsCancellationRequested;

    public double? ReadFps()
    {
        FpsReading? current = Volatile.Read(ref reading);
        return current is not null && Stopwatch.GetElapsedTime(current.Timestamp).TotalSeconds < 3
            ? current.Fps
            : null;
    }

    public async Task StopAsync()
    {
        using (cancellation)
        {
            cancellation.Cancel();
            await captureTask.ConfigureAwait(false);
        }
    }

    private async Task CaptureAsync(int targetProcessId, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            using Process capture = Start(
                "--process_id", targetProcessId.ToString(CultureInfo.InvariantCulture),
                "--output_stdout", "--v1_metrics", "--no_console_stats",
                "--no_track_input", "--no_track_gpu", "--no_track_display", "--terminate_on_proc_exit");
            capture.BeginErrorReadLine();

            try
            {
                PresentMonFpsSamples samples = new();
                while (await capture.StandardOutput.ReadLineAsync(token).ConfigureAwait(false) is { } line)
                {
                    if (samples.Add(line) is { } fps)
                    {
                        Volatile.Write(ref reading, new FpsReading(fps, Stopwatch.GetTimestamp()));
                    }
                }
            }
            finally
            {
                await StopProcessAsync(capture).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or OperationCanceledException)
        {
            // The window exposes failures and can retry without interrupting game launch.
        }
    }

    private async Task StopProcessAsync(Process capture)
    {
        if (capture.HasExited)
        {
            return;
        }

        try
        {
            using Process stop = Start("--terminate_existing_session", "--no_console_stats");
            stop.BeginOutputReadLine();
            stop.BeginErrorReadLine();
            using CancellationTokenSource timeout = new(StopTimeout);
            try
            {
                await stop.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                await capture.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            finally
            {
                KillIfRunning(stop);
            }
        }
        finally
        {
            KillIfRunning(capture);
        }
    }

    private Process Start(params string[] arguments)
    {
        ProcessStartInfo info = new(Path.Combine(AppContext.BaseDirectory, "PresentMon", "PresentMon-2.6.0-x64.exe"))
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.ArgumentList.Add("--session_name");
        info.ArgumentList.Add(sessionName);
        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        return Process.Start(info) ?? throw new InvalidOperationException("Could not start PresentMon.");
    }

    private static void KillIfRunning(Process process)
    {
        if (!process.HasExited)
        {
            try
            {
                process.Kill();
            }
            catch (InvalidOperationException)
            {
                // The process exited before the kill request.
            }
        }
    }

    private sealed record FpsReading(double Fps, long Timestamp);
}

internal sealed class PresentMonFpsSamples
{
    private readonly Dictionary<ulong, (int Count, double First, double Last)> swapChains = [];
    private double? windowStart;

    public double? Add(string line)
    {
        // PresentMon v2.6.0 v1 CSV: application, PID, swap chain, ..., time in seconds.
        ReadOnlySpan<char> row = line.AsSpan();
        Span<Range> columns = stackalloc Range[9];
        if (row.Split(columns, ',') < columns.Length ||
            !ulong.TryParse(row[columns[2]].TrimStart("0x"), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong swapChain) ||
            !double.TryParse(row[columns[7]], NumberStyles.Float, CultureInfo.InvariantCulture, out double timestamp) ||
            !double.IsFinite(timestamp))
        {
            return null;
        }

        windowStart ??= timestamp;
        double? fps = null;
        if (timestamp - windowStart >= 1)
        {
            // The game's main swap chain normally accounts for the most presents.
            (int Count, double First, double Last) main = default;
            foreach (var sample in swapChains.Values)
            {
                if (sample.Count > main.Count)
                {
                    main = sample;
                }
            }

            if (timestamp - windowStart < 2 && main.Count >= 2 && main.Last > main.First)
            {
                fps = (main.Count - 1) / (main.Last - main.First);
            }

            swapChains.Clear();
            windowStart = timestamp;
        }

        var current = swapChains.TryGetValue(swapChain, out var sampleForChain)
            ? (sampleForChain.Count + 1, sampleForChain.First, timestamp)
            : (1, timestamp, timestamp);
        swapChains[swapChain] = current;
        return fps;
    }
}
