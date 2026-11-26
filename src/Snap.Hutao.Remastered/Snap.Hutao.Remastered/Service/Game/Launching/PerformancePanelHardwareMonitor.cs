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
    private readonly Task hardwareTask;
    private readonly Task cpuTemperatureTask;
    private HardwareReading? hardware;
    private TemperatureReading? cpuTemperature;

    public PerformancePanelHardwareMonitor()
    {
        CancellationToken token = cancellation.Token;
        hardwareTask = Task.Run(() => UpdateHardwareAsync(token));
        cpuTemperatureTask = Task.Run(() => UpdateCpuTemperatureAsync(token));
    }

    public PanelHardwareValues Read()
    {
        HardwareReading? current = Volatile.Read(ref hardware);
        TemperatureReading? temperature = Volatile.Read(ref cpuTemperature);
        return new(current?.CpuUsage,
            temperature is not null && Stopwatch.GetElapsedTime(temperature.Timestamp).TotalSeconds < 5 ? temperature.Celsius : null,
            current?.GpuUsage, current?.GpuTemperature);
    }

    public async Task StopAsync()
    {
        using (cancellation)
        {
            cancellation.Cancel();
            await Task.WhenAll(hardwareTask, cpuTemperatureTask).ConfigureAwait(false);
        }
    }

    private async Task UpdateHardwareAsync(CancellationToken token)
    {
        using NvidiaSensors? nvidia = NvidiaSensors.TryOpen();
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(1));
        SystemTimes? previous = null;
        try
        {
            do
            {
                SystemTimes? current = GetSystemTimes(out long idle, out long kernel, out long user)
                    ? new(idle, kernel, user) : null;
                double? cpuUsage = current is { } times && previous is { } older ? times.UsageSince(older) : null;
                previous = current;
                Volatile.Write(ref hardware, new HardwareReading(cpuUsage, nvidia?.ReadUsage(), nvidia?.ReadTemperature()));
            } while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    private async Task UpdateCpuTemperatureAsync(CancellationToken token)
    {
        try
        {
            object? manufacturer = Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\BIOS", "SystemManufacturer", null);
            if (manufacturer is not string name || !name.Contains("ASUS", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            ProcessStartInfo info = new(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"))
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            info.ArgumentList.Add("-NoLogo");
            info.ArgumentList.Add("-NoProfile");
            info.ArgumentList.Add("-NonInteractive");
            info.ArgumentList.Add("-Command");
            info.ArgumentList.Add(ReadAsusTemperature);
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
                    if (int.TryParse(line, NumberStyles.Integer, CultureInfo.InvariantCulture, out int celsius) &&
                        celsius is > 0 and <= 125)
                    {
                        Volatile.Write(ref cpuTemperature, new TemperatureReading(celsius, Stopwatch.GetTimestamp()));
                    }
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
            // Unsupported or inaccessible sensors leave the temperature unavailable.
        }
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

    private sealed record HardwareReading(double? CpuUsage, uint? GpuUsage, uint? GpuTemperature);

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

internal sealed unsafe class NvidiaSensors : IDisposable
{
    private readonly nint library;
    private readonly nint device;
    private readonly delegate* unmanaged[Cdecl]<int> shutdown;
    private readonly delegate* unmanaged[Cdecl]<nint, uint, uint*, int> temperature;
    private readonly delegate* unmanaged[Cdecl]<nint, GpuUtilization*, int> utilization;

    private NvidiaSensors(nint library, nint device, nint shutdown, nint temperature, nint utilization)
    {
        this.library = library;
        this.device = device;
        this.shutdown = (delegate* unmanaged[Cdecl]<int>)shutdown;
        this.temperature = (delegate* unmanaged[Cdecl]<nint, uint, uint*, int>)temperature;
        this.utilization = (delegate* unmanaged[Cdecl]<nint, GpuUtilization*, int>)utilization;
    }

    public static NvidiaSensors? TryOpen()
    {
        string[] paths =
        [
            Path.Combine(Environment.SystemDirectory, "nvml.dll"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvml.dll"),
        ];
        foreach (string path in paths)
        {
            if (!NativeLibrary.TryLoad(path, out nint library))
            {
                continue;
            }

            if (NativeLibrary.TryGetExport(library, "nvmlInit_v2", out nint init) &&
                NativeLibrary.TryGetExport(library, "nvmlShutdown", out nint shutdown) &&
                NativeLibrary.TryGetExport(library, "nvmlDeviceGetHandleByIndex_v2", out nint getDevice) &&
                ((delegate* unmanaged[Cdecl]<int>)init)() == 0)
            {
                nint device;
                if (((delegate* unmanaged[Cdecl]<uint, nint*, int>)getDevice)(0, &device) == 0)
                {
                    NativeLibrary.TryGetExport(library, "nvmlDeviceGetTemperature", out nint temperature);
                    NativeLibrary.TryGetExport(library, "nvmlDeviceGetUtilizationRates", out nint utilization);
                    return new(library, device, shutdown, temperature, utilization);
                }

                _ = ((delegate* unmanaged[Cdecl]<int>)shutdown)();
            }

            NativeLibrary.Free(library);
        }

        return null;
    }

    public uint? ReadTemperature()
    {
        uint value;
        return temperature != null && temperature(device, 0, &value) == 0 ? value : null; // NVML_TEMPERATURE_GPU
    }

    public uint? ReadUsage()
    {
        GpuUtilization value;
        return utilization != null && utilization(device, &value) == 0 ? value.Gpu : null;
    }

    public void Dispose()
    {
        _ = shutdown();
        NativeLibrary.Free(library);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GpuUtilization
    {
        public uint Gpu;
        public uint Memory;
    }
}
