// Copyright (c) Snap Hutao RP. All rights reserved.
// Licensed under the MIT license.

using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Hutao.Remastered.Service.Game.Launching;

internal sealed class PerformancePanelHardwareMonitor
{
    private const string ReadAsusTemperature = """
        $ErrorActionPreference = 'Stop'
        $sensor = Get-CimInstance -Namespace root/wmi -ClassName AsusAtkWmi_WMNB | Select-Object -First 1
        if (-not $sensor) { exit }
        while ($true) {
            $result = Invoke-CimMethod -InputObject $sensor -MethodName DSTS -Arguments @{ Device_ID = [uint32]0x00120094 }
            $temperature = [long]$result.device_status - 65536
            if ($temperature -le 0 -or $temperature -gt 125) { exit }
            [Console]::WriteLine($temperature)
            Start-Sleep -Milliseconds 1500
        }
        """;

    private readonly CancellationTokenSource cancellation = new();
    private readonly Task gpuTask;
    private readonly Task cpuTemperatureTask;
    private SystemTimes? previousCpuTimes;
    private GpuReading? gpu;
    private TemperatureReading? cpuTemperature;

    public PerformancePanelHardwareMonitor()
    {
        CancellationToken token = cancellation.Token;
        gpuTask = Task.Run(() => WatchGpuAsync(token));
        cpuTemperatureTask = Task.Run(() => WatchCpuTemperatureAsync(token));
    }

    public PanelHardwareValues Read()
    {
        SystemTimes? current = GetSystemTimes(out long idle, out long kernel, out long user) ? new(idle, kernel, user) : null;
        double? cpuUsage = current is { } times && previousCpuTimes is { } previous ? times.UsageSince(previous) : null;
        previousCpuTimes = current;

        TemperatureReading? temperature = Volatile.Read(ref cpuTemperature);
        GpuReading? graphics = Volatile.Read(ref gpu);
        if (graphics is not null && Stopwatch.GetElapsedTime(graphics.Timestamp).TotalSeconds >= 5)
        {
            graphics = null;
        }

        return new(cpuUsage,
            temperature is not null && Stopwatch.GetElapsedTime(temperature.Timestamp).TotalSeconds < 5 ? temperature.Celsius : null,
            graphics?.Usage, graphics?.Celsius);
    }

    public async Task StopAsync()
    {
        using (cancellation)
        {
            cancellation.Cancel();
            await Task.WhenAll(gpuTask, cpuTemperatureTask).ConfigureAwait(false);
        }
    }

    private Task WatchGpuAsync(CancellationToken token)
    {
        string executable = Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe");
        if (!File.Exists(executable))
        {
            executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe");
        }

        if (!File.Exists(executable))
        {
            return Task.CompletedTask;
        }

        return ReadLinesAsync(executable,
            ["--id=0", "--query-gpu=utilization.gpu,temperature.gpu", "--format=csv,noheader,nounits", "--loop=1"],
            AcceptGpuReading, token);
    }

    private void AcceptGpuReading(string line)
    {
        int comma = line.IndexOf(',');
        if (comma < 0)
        {
            return;
        }

        uint? usage = uint.TryParse(line.AsSpan(0, comma), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint percent) && percent <= 100
            ? percent : null;
        uint? temperature = uint.TryParse(line.AsSpan(comma + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint celsius) && celsius is > 0 and <= 125
            ? celsius : null;
        Volatile.Write(ref gpu, new GpuReading(usage, temperature, Stopwatch.GetTimestamp()));
    }

    private Task WatchCpuTemperatureAsync(CancellationToken token)
    {
        try
        {
            object? manufacturer = Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\BIOS", "SystemManufacturer", null);
            if (manufacturer is not string name || !name.Contains("ASUS", StringComparison.OrdinalIgnoreCase))
            {
                return Task.CompletedTask;
            }

            string executable = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            return ReadLinesAsync(executable, ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", ReadAsusTemperature], AcceptCpuTemperature, token);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            return Task.CompletedTask;
        }
    }

    private void AcceptCpuTemperature(string line)
    {
        if (int.TryParse(line, NumberStyles.Integer, CultureInfo.InvariantCulture, out int celsius) && celsius is > 0 and <= 125)
        {
            Volatile.Write(ref cpuTemperature, new TemperatureReading(celsius, Stopwatch.GetTimestamp()));
        }
    }

    private static async Task ReadLinesAsync(string executable, string[] arguments, Action<string> accept, CancellationToken token)
    {
        try
        {
            ProcessStartInfo info = new(executable)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (string argument in arguments)
            {
                info.ArgumentList.Add(argument);
            }

            token.ThrowIfCancellationRequested();
            using Process? process = Process.Start(info);
            if (process is null)
            {
                return;
            }

            try
            {
                process.BeginErrorReadLine();
                while (await process.StandardOutput.ReadLineAsync().WaitAsync(token).ConfigureAwait(false) is { } line)
                {
                    accept(line);
                }
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    await process.WaitForExitAsync().ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or
            OperationCanceledException or UnauthorizedAccessException or SecurityException)
        {
            // Unsupported or inaccessible sensors leave their readings unavailable.
        }
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

    private sealed record GpuReading(uint? Usage, uint? Celsius, long Timestamp);

    private sealed record TemperatureReading(int Celsius, long Timestamp);

    private readonly record struct SystemTimes(long Idle, long Kernel, long User)
    {
        public double? UsageSince(SystemTimes previous)
        {
            long total = Kernel - previous.Kernel + User - previous.User;
            return total > 0 ? Math.Clamp(100D * (total - (Idle - previous.Idle)) / total, 0, 100) : null;
        }
    }
}

internal readonly record struct PanelHardwareValues(double? CpuUsage, int? CpuTemperature, uint? GpuUsage, uint? GpuTemperature);
