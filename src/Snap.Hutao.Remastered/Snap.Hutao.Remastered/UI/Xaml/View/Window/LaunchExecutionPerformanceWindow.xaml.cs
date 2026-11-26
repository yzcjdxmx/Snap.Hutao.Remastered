// Copyright (c) Snap Hutao RP. All rights reserved.
// Licensed under the MIT license.

using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Snap.Hutao.Remastered.Service.Game;
using Snap.Hutao.Remastered.Service.Game.Launching;
using Snap.Hutao.Remastered.UI.Windowing;
using Snap.Hutao.Remastered.UI.Windowing.Abstraction;
using Snap.Hutao.Remastered.UI.Xaml.Media.Backdrop;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.UI;
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
    private readonly LaunchOptions options;
    private readonly nint windowHandle;
    private readonly PresentMonFpsCapture capture;
    private readonly PerformancePanelHardwareMonitor hardware;
    private readonly DispatcherQueueTimer timer;
    private readonly SolidColorBrush backgroundBrush = new();
    private readonly SolidColorBrush textBrush = new();
    private nint gameWindowHandle;
    private bool positionLocked;
    private bool showWhenGameReturns = true;
    private (double Scale, Color Background, double Opacity)? lastAppearance;
    private GamePresentation? lastPresentation;
    private int restackAttempts;
    private long lastRefresh;
    private Task? stopTask;

    [WinRT.DynamicWindowsRuntimeCast(typeof(global::Microsoft.UI.Windowing.OverlappedPresenter))]
    public LaunchExecutionPerformanceWindow(IServiceProvider serviceProvider, LaunchOptions options, int gameProcessId)
    {
        InitializeComponent();

        this.options = options;
        this.gameProcessId = gameProcessId;
        windowHandle = WindowNative.GetWindowHandle(this);
        AppWindow.SafeIsShowInSwitchers(false);

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
        RootBorder.Background = backgroundBrush;
        foreach (TextBlock label in new[] { FpsLabel, FpsValue, LowLabel, LowValue, CpuLabel, CpuValue, GpuLabel, GpuValue })
        {
            label.Foreground = textBrush;
        }

        CloseButton.Foreground = textBrush;
        ApplyAppearance();
        AppWindow.Hide();
        capture = new(gameProcessId);
        hardware = new();
        timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(250);
        timer.Tick += OnTick;
        Closed += OnClosed;
        timer.Start();
    }

    public FrameworkElement TitleBarCaptionAccess => DragSurface;

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
        return stopTask ??= Task.WhenAll(capture.StopAsync(), hardware.StopAsync());
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
        ApplyAppearance();
        PresentMonFpsSample? sample = capture.ReadSample();
        PanelHardwareValues hardwareValues = hardware.Read();
        FpsValue.Text = capture.Failed ? "失败" : sample?.Fps.ToString("0") ?? "—";
        LowValue.Text = sample?.LowFps?.ToString("0") ?? "—";
        string cpuUsage = hardwareValues.CpuUsage is { } cpu ? $"{cpu:0}%" : "—";
        string cpuTemperature = hardwareValues.CpuTemperature is { } cpuCelsius ? $"{cpuCelsius}°C" : "—";
        string gpuUsage = hardwareValues.GpuUsage is { } gpu ? $"{gpu}%" : "—";
        string gpuTemperature = hardwareValues.GpuTemperature is { } gpuCelsius ? $"{gpuCelsius}°C" : "—";
        CpuValue.Text = $"{cpuUsage} · {cpuTemperature}";
        GpuValue.Text = $"{gpuUsage} · {gpuTemperature}";
        ToolTipService.SetToolTip(FpsMetric, capture.Failed ? "采集失败，下次启动游戏时会重试" : null);
    }

    private void ApplyAppearance()
    {
        double configuredScale = options.PerformancePanelScale.Value;
        double configuredOpacity = options.PerformancePanelBackgroundOpacity.Value;
        double scale = double.IsFinite(configuredScale) ? Math.Clamp(configuredScale, 70, 170) / 100 : 1;
        double opacity = double.IsFinite(configuredOpacity) ? Math.Clamp(configuredOpacity, 0, 100) / 100 : 0.89;
        Color background = options.PerformancePanelBackgroundColor;
        var appearance = (Scale: scale, Background: background, Opacity: opacity);
        if (lastAppearance is { } previous && previous.Equals(appearance))
        {
            return;
        }

        bool first = lastAppearance is null;
        lastAppearance = appearance;
        backgroundBrush.Color = Color.FromArgb((byte)Math.Round(255 * opacity), background.R, background.G, background.B);
        bool lightBackground = opacity >= 0.5 && (0.2126 * background.R + 0.7152 * background.G + 0.0722 * background.B) > 155;
        textBrush.Color = lightBackground ? Color.FromArgb(255, 24, 29, 37) : Color.FromArgb(255, 235, 241, 247);
        SizeInt32 size = ScaledSizeInt32.CreateForWindow((int)Math.Ceiling(RootBorder.Width * scale), (int)Math.Ceiling(RootBorder.Height * scale), this);
        AppWindow.Resize(size);
        if (DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary) is { } display)
        {
            RectInt32 area = display.WorkArea;
            PointInt32 current = AppWindow.Position;
            int maxX = Math.Max(area.X, area.X + area.Width - size.Width);
            int maxY = Math.Max(area.Y, area.Y + area.Height - size.Height);
            PointInt32 position = new()
            {
                X = Math.Clamp(first ? maxX - 24 : current.X, area.X, maxX),
                Y = Math.Clamp(first ? area.Y + 24 : current.Y, area.Y, maxY),
            };
            if (position.X != current.X || position.Y != current.Y)
            {
                AppWindow.Move(position);
            }
        }
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

        if (gameWindowHandle != 0 && IsIconic(gameWindowHandle))
        {
            if (AppWindow.IsVisible)
            {
                AppWindow.Hide();
            }

            showWhenGameReturns = true;
            lastPresentation = null;
            return;
        }

        if (gameWindowHandle != 0 && GetWindowRect(gameWindowHandle, out WindowRect bounds))
        {
            SetPositionLocked(CoversMonitor(gameWindowHandle, bounds));
            if (gameIsForeground)
            {
                presentation = new(foreground, bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
            }
        }

        if (showWhenGameReturns && presentation is not null)
        {
            AppWindow.Show(false);
            AppWindow.MoveInZOrderAtTop();
            showWhenGameReturns = false;
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
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(nint window);

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
