// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Snap.Hutao.Remastered.Core.Diagnostics;
using Snap.Hutao.Remastered.Core.Property;
using Snap.Hutao.Remastered.Factory.Process;
using Snap.Hutao.Remastered.Win32;
using Snap.Hutao.Remastered.Win32.Foundation;

namespace Snap.Hutao.Remastered.Service.Game;

public static class GameLifeCycle
{
    public static IObservableProperty<bool> IsGameRunningProperty { get; } = Property.CreateObservable(PrivateIsGameRunning());

    public static IObservableProperty<bool> IsIslandConnected { get; } = Property.CreateObservable(false);

    public static async ValueTask SpinWaitGameExitAsync(ITaskContext taskContext)
    {
        while (true)
        {
            await taskContext.SwitchToBackgroundAsync();
            List<DiagnosticsProcess> processes = GetGameProcesses();
            if (processes.Count == 0)
            {
                break;
            }

            try
            {
                await WaitForGameProcessesAsync(processes, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                DisposeProcesses(processes);
            }
        }

        await taskContext.SwitchToMainThreadAsync();
        UpdateGameRunningState(false);
    }

    public static async Task ObserveGameRunningAsync(ITaskContext taskContext, CancellationToken token)
    {
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                await taskContext.SwitchToBackgroundAsync();
                List<DiagnosticsProcess> processes = GetGameProcesses();
                try
                {
                    await taskContext.SwitchToMainThreadAsync();
                    token.ThrowIfCancellationRequested();
                    UpdateGameRunningState(processes.Count > 0);
                    await taskContext.SwitchToBackgroundAsync();
                    if (processes.Count == 0)
                    {
                        // Discovery is needed only while idle. A running process has an OS exit notification.
                        await Task.Delay(TimeSpan.FromSeconds(2), token).ConfigureAwait(false);
                    }
                    else
                    {
                        await WaitForGameProcessesAsync(processes, token).ConfigureAwait(false);
                    }
                }
                finally
                {
                    DisposeProcesses(processes);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // The launch page was unloaded. Its observer must stop without changing shared game state.
        }
    }

    public static bool IsGameRunningRequiresMainThread()
    {
        bool result = PrivateIsGameRunning();
        UpdateGameRunningState(result);
        return result;
    }

    public static async ValueTask<bool> IsGameRunningAsync(ITaskContext taskContext)
    {
        await taskContext.SwitchToBackgroundAsync();
        bool result = PrivateIsGameRunning();

        await taskContext.SwitchToMainThreadAsync();
        UpdateGameRunningState(result);
        return result;
    }

    public static async ValueTask<ValueResult<bool, IProcess?>> TryGetRunningGameProcessAsync(ITaskContext taskContext)
    {
        await taskContext.SwitchToBackgroundAsync();
        bool result = PrivateIsGameRunning(out IProcess? runningProcess);

        await taskContext.SwitchToMainThreadAsync();
        UpdateGameRunningState(result);
        return new(result, runningProcess);
    }

    public static async ValueTask<bool> TryKillGameProcessAsync(ITaskContext taskContext)
    {
        await taskContext.SwitchToBackgroundAsync();
        List<DiagnosticsProcess> processes = GetGameProcesses();
        Exception? failure = null;
        try
        {
            foreach (DiagnosticsProcess process in processes)
            {
                try
                {
                    process.Kill();
                    using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
                    await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (InvalidOperationException) when (process.HasExited)
                {
                    // It already exited after the fresh discovery snapshot.
                }
                catch (Exception ex)
                {
                    SentrySdk.CaptureException(ex);
                    failure ??= ex;
                }
            }
        }
        finally
        {
            DisposeProcesses(processes);
        }

        bool isRunning = PrivateIsGameRunning();
        await taskContext.SwitchToMainThreadAsync();
        UpdateGameRunningState(isRunning);
        if (failure is not null)
        {
            string message = failure is Win32Exception { NativeErrorCode: (int)WIN32_ERROR.ERROR_ACCESS_DENIED }
                ? SH.ServiceGameStopProcessAccessDenied
                : failure.Message;
            HutaoNative.Instance.ShowErrorMessage(SH.ServiceGameLaunchExecutionGameRunningKillFailed, message);
        }

        return processes.Count > 0 && failure is null && !isRunning;
    }

    private static List<DiagnosticsProcess> GetGameProcesses()
    {
        return ProcessFactory.GetRunningProcesses([GameConstants.YuanShenProcessName, GameConstants.GenshinImpactProcessName]);
    }

    private static void DisposeProcesses(List<DiagnosticsProcess> processes)
    {
        foreach (DiagnosticsProcess process in processes)
        {
            process.Dispose();
        }
    }

    private static async Task WaitForGameProcessesAsync(List<DiagnosticsProcess> processes, CancellationToken token)
    {
        try
        {
            await Task.WhenAll(processes.Select(process => process.WaitForExitAsync(token))).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // A process exited before the wait was registered. Refresh the snapshot.
        }
        catch (Win32Exception ex)
        {
            // Some elevated processes cannot be watched from a normal-integrity process.
            SentrySdk.CaptureException(ex);
            await Task.Delay(TimeSpan.FromSeconds(2), token).ConfigureAwait(false);
        }
    }

    private static void UpdateGameRunningState(bool isRunning)
    {
        if (!isRunning)
        {
            // The Island monitor can still be awaiting a network response after the process exits.
            IsIslandConnected.Value = false;
        }

        IsGameRunningProperty.Value = isRunning;
    }

    private static bool PrivateIsGameRunning()
    {
        bool running = PrivateIsGameRunning(out IProcess? process);
        process?.Dispose();
        return running;
    }

    private static bool PrivateIsGameRunning([NotNullWhen(true)] out IProcess? runningProcess)
    {
        return ProcessFactory.IsRunning([GameConstants.YuanShenProcessName, GameConstants.GenshinImpactProcessName], out runningProcess);
    }
}

