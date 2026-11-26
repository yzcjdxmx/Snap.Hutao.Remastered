// Copyright (c) Snap Hutao RP. All rights reserved.
// Licensed under the MIT license.

using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Snap.Hutao.Remastered.Service.Game.Launching;
using Snap.Hutao.Remastered.UI.Windowing;
using Snap.Hutao.Remastered.UI.Windowing.Abstraction;
using Snap.Hutao.Remastered.UI.Xaml.Media.Backdrop;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Graphics;
using WinRT.Interop;

namespace Snap.Hutao.Remastered.UI.Xaml.View.Window;

public sealed partial class LaunchExecutionPerformanceWindow : Microsoft.UI.Xaml.Window,
    IXamlWindowExtendContentIntoTitleBar,
    IWindowNeedEraseBackground
{
    private const int ExtendedStyleIndex = -20;
    private const int ClickThroughStyles = 0x08000020; // WS_EX_NOACTIVATE | WS_EX_TRANSPARENT
    private const uint RefreshFrameWithoutActivating = 0x37; // SWP_NOSIZE | SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED

    private readonly int gameProcessId;
    private readonly nint windowHandle;
    private readonly PresentMonFpsCapture capture;
    private readonly DispatcherQueueTimer timer;
    private nint gameWindowHandle;
    private bool positionLocked;
    private GamePresentation? lastPresentation;
    private int restackAttempts;
    private long lastRefresh;
    private Task? stopTask;

    [WinRT.DynamicWindowsRuntimeCast(typeof(global::Microsoft.UI.Windowing.OverlappedPresenter))]
    public LaunchExecutionPerformanceWindow(IServiceProvider serviceProvider, int gameProcessId)
    {
        InitializeComponent();

        this.gameProcessId = gameProcessId;
        windowHandle = WindowNative.GetWindowHandle(this);
        AppWindow.SafeIsShowInSwitchers(false);

        SizeInt32 size = ScaledSizeInt32.CreateForWindow(230, 58, this);
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = false;
            presenter.IsAlwaysOnTop = true;
            presenter.SetBorderAndTitleBar(false, false);
        }

        this.AddExtendedStyleLayered();
        this.RemoveStyleOverlappedWindow();
        SystemBackdrop = new TransparentBackdrop();
        this.InitializeController(serviceProvider.CreateScope().ServiceProvider);
        AppWindow.Title = "游戏性能";
        AppWindow.Resize(size);
        if (DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary) is { } display)
        {
            RectInt32 area = display.WorkArea;
            AppWindow.Move(new PointInt32 { X = area.X + area.Width - size.Width - 24, Y = area.Y + 24 });
        }

        capture = new(gameProcessId);
        timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(250);
        timer.Tick += OnTick;
        Closed += OnClosed;
        timer.Start();
    }

    public FrameworkElement TitleBarCaptionAccess => RootBorder;

    public ImmutableArray<FrameworkElement> TitleBarPassthrough => [CloseButton];

    public Task CloseAndStopAsync()
    {
        if (stopTask is null)
        {
            Close();
        }

        return stopTask ?? StopAsync();
    }

    private Task StopAsync()
    {
        timer.Stop();
        timer.Tick -= OnTick;
        return stopTask ??= capture.StopAsync();
    }

    private void OnCloseClick(object sender, RoutedEventArgs args)
    {
        Close();
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        Closed -= OnClosed;
        _ = StopAsync();
    }

    private void OnTick(DispatcherQueueTimer sender, object args)
    {
        RestoreOrder();
        if (Stopwatch.GetElapsedTime(lastRefresh).TotalSeconds < 1)
        {
            return;
        }

        lastRefresh = Stopwatch.GetTimestamp();
        PresentMonFpsSample? sample = capture.ReadSample();
        FpsValue.Text = capture.Failed ? "失败" : sample?.Fps.ToString("0") ?? "—";
        LowValue.Text = sample?.LowFps?.ToString("0") ?? "—";
        ToolTipService.SetToolTip(FpsMetric, capture.Failed ? "采集失败，下次启动游戏时会重试" : null);
    }

    private void RestoreOrder()
    {
        nint foreground = GetForegroundWindow();
        GamePresentation? presentation = null;
        bool gameIsForeground = foreground != 0 && GetWindowThreadProcessId(foreground, out uint processId) != 0 &&
            processId == gameProcessId;
        if (gameIsForeground)
        {
            gameWindowHandle = foreground;
        }

        if (gameWindowHandle != 0 && GetWindowRect(gameWindowHandle, out WindowRect bounds))
        {
            SetPositionLocked(CoversMonitor(gameWindowHandle, bounds));
            if (gameIsForeground)
            {
                presentation = new(foreground, bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
            }
        }

        if (presentation != lastPresentation)
        {
            lastPresentation = presentation;
            restackAttempts = 0;
        }

        if (presentation is null || restackAttempts >= 2 || !AppWindow.IsVisible)
        {
            return;
        }

        // The game can jump above other topmost windows while entering full screen.
        // Limit attempts for each game rectangle / foreground interval.
        nint above = GetWindow(windowHandle, 3); // GW_HWNDPREV
        for (int index = 0; index < 512 && above != 0; index++)
        {
            if (above == foreground)
            {
                restackAttempts++;
                AppWindow.MoveInZOrderAtTop();
                break;
            }

            above = GetWindow(above, 3);
        }
    }

    private void SetPositionLocked(bool locked)
    {
        if (locked == positionLocked)
        {
            return;
        }

        int current = GetWindowLongW(windowHandle, ExtendedStyleIndex);
        int desired = locked ? current | ClickThroughStyles : current & ~ClickThroughStyles;
        SetWindowLongW(windowHandle, ExtendedStyleIndex, desired);
        SetWindowPos(windowHandle, 0, 0, 0, 0, 0, RefreshFrameWithoutActivating);
        positionLocked = (GetWindowLongW(windowHandle, ExtendedStyleIndex) & ClickThroughStyles) == ClickThroughStyles;
    }

    private static bool CoversMonitor(nint gameWindow, WindowRect bounds)
    {
        nint monitor = MonitorFromWindow(gameWindow, 0); // MONITOR_DEFAULTTONULL
        MonitorInfo info = new() { Size = Marshal.SizeOf<MonitorInfo>() };
        return monitor != 0 && GetMonitorInfoW(monitor, ref info) &&
            bounds.Left <= info.Monitor.Left && bounds.Top <= info.Monitor.Top &&
            bounds.Right >= info.Monitor.Right && bounds.Bottom >= info.Monitor.Bottom;
    }

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint window, out uint processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint window, out WindowRect rectangle);

    [LibraryImport("user32.dll")]
    private static partial nint GetWindow(nint window, uint command);

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromWindow(nint window, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);

    [LibraryImport("user32.dll")]
    private static partial int GetWindowLongW(nint window, int index);

    [LibraryImport("user32.dll")]
    private static partial int SetWindowLongW(nint window, int index, int value);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);

    private readonly record struct GamePresentation(nint Handle, int Left, int Top, int Right, int Bottom);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public WindowRect Monitor;
        public WindowRect Work;
        public uint Flags;
    }
}
