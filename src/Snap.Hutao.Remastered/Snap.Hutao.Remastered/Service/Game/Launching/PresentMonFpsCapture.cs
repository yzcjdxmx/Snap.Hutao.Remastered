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

    public PresentMonFpsSample? ReadSample()
    {
        FpsReading? current = Volatile.Read(ref reading);
        return current is not null && Stopwatch.GetElapsedTime(current.Timestamp).TotalSeconds < 3
            ? current.Sample
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
            try
            {
                capture.BeginErrorReadLine();
                PresentMonFpsSamples samples = new();
                // Cancelling the wait lets us stop the process even when its stdout pipe is idle.
                while (await capture.StandardOutput.ReadLineAsync().WaitAsync(token).ConfigureAwait(false) is { } line)
                {
                    if (samples.Add(line) is { } sample)
                    {
                        Volatile.Write(ref reading, new FpsReading(sample, Stopwatch.GetTimestamp()));
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

    private sealed record FpsReading(PresentMonFpsSample Sample, long Timestamp);
}

internal readonly record struct PresentMonFpsSample(double Fps, double? LowFps);

internal sealed class PresentMonFpsSamples
{
    private readonly Dictionary<ulong, FrameWindow> swapChains = [];
    private readonly Queue<List<double>> recentIntervals = [];
    private double? windowStart;
    private ulong? recentSwapChain;

    public PresentMonFpsSample? Add(string line)
    {
        // PresentMon v2.6.0 v1 CSV: swap chain, relative time, and milliseconds between presents.
        ReadOnlySpan<char> row = line.AsSpan();
        // The first ten columns are always present. Reserve one range for any optional trailing columns.
        Span<Range> columns = stackalloc Range[11];
        if (row.Split(columns, ',') < 10 ||
            !ulong.TryParse(row[columns[2]].TrimStart("0x"), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong swapChain) ||
            !double.TryParse(row[columns[7]], NumberStyles.Float, CultureInfo.InvariantCulture, out double timestamp) ||
            !double.IsFinite(timestamp) ||
            !double.TryParse(row[columns[9]], NumberStyles.Float, CultureInfo.InvariantCulture, out double interval))
        {
            return null;
        }

        windowStart ??= timestamp;
        PresentMonFpsSample? reading = null;
        if (timestamp - windowStart >= 1)
        {
            if (timestamp - windowStart < 2)
            {
                reading = FinishWindow();
            }
            else
            {
                recentIntervals.Clear();
                recentSwapChain = null;
            }

            swapChains.Clear();
            windowStart = timestamp;
        }

        if (!swapChains.TryGetValue(swapChain, out FrameWindow? current))
        {
            current = new(timestamp);
            swapChains.Add(swapChain, current);
        }

        current.Last = timestamp;
        current.Count++;
        if (interval > 0 && double.IsFinite(interval))
        {
            current.Intervals.Add(interval);
        }

        return reading;
    }

    private PresentMonFpsSample? FinishWindow()
    {
        FrameWindow? main = null;
        ulong mainSwapChain = 0;
        foreach ((ulong swapChain, FrameWindow sample) in swapChains)
        {
            if (main is null || sample.Count > main.Count)
            {
                main = sample;
                mainSwapChain = swapChain;
            }
        }

        if (main is null || main.Count < 2 || main.Last <= main.First)
        {
            recentIntervals.Clear();
            recentSwapChain = null;
            return null;
        }

        if (mainSwapChain != recentSwapChain)
        {
            recentIntervals.Clear();
            recentSwapChain = mainSwapChain;
        }

        recentIntervals.Enqueue(main.Intervals);
        if (recentIntervals.Count > 5)
        {
            recentIntervals.Dequeue();
        }

        return new((main.Count - 1) / (main.Last - main.First), ReadOnePercentLow());
    }

    private double? ReadOnePercentLow()
    {
        List<double> sortedIntervals = [];
        foreach (List<double> window in recentIntervals)
        {
            sortedIntervals.AddRange(window);
        }

        if (sortedIntervals.Count < 100)
        {
            return null;
        }

        // 1% low is the reciprocal of the mean of the slowest 1% of frame intervals.
        sortedIntervals.Sort();
        int frameCount = (sortedIntervals.Count + 99) / 100;
        double sum = 0;
        for (int index = sortedIntervals.Count - frameCount; index < sortedIntervals.Count; index++)
        {
            sum += sortedIntervals[index];
        }

        return 1000 * frameCount / sum;
    }

    private sealed class FrameWindow(double first)
    {
        public double First { get; } = first;

        public double Last { get; set; }

        public int Count { get; set; }

        public List<double> Intervals { get; } = [];
    }
}
