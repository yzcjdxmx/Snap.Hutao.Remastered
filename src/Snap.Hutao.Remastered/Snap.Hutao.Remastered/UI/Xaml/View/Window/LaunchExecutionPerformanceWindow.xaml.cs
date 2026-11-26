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
using System.Globalization;
using System.Runtime.CompilerServices;
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
    private readonly MetricTile[] metrics;
    private PointInt32 lastPosition;
    private int shownMetrics = -1;
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
        metrics =
        [
            new(FpsTile, FpsLabel, FpsValue, () => options.PerformancePanelShowFps.Value),
            new(LowTile, LowLabel, LowValue, () => options.PerformancePanelShowLow.Value),
            new(CpuUsageTile, CpuUsageLabel, CpuUsageValue, () => options.PerformancePanelShowCpuUsage.Value),
            new(CpuTemperatureTile, CpuTemperatureLabel, CpuTemperatureValue, () => options.PerformancePanelShowCpuTemperature.Value),
            new(GpuUsageTile, GpuUsageLabel, GpuUsageValue, () => options.PerformancePanelShowGpuUsage.Value),
            new(GpuTemperatureTile, GpuTemperatureLabel, GpuTemperatureValue, () => options.PerformancePanelShowGpuTemperature.Value),
            new(TimeTile, TimeLabel, TimeValue, () => options.PerformancePanelShowTime.Value),
        ];
        foreach (MetricTile metric in metrics)
        {
            metric.Label.Foreground = textBrush;
            metric.Value.Foreground = textBrush;
        }

        CloseButton.Foreground = textBrush;
        ApplyAppearance();
        lastPosition = AppWindow.Position;
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

    public async Task CloseAndStopAsync()
    {
        try
        {
            if (stopTask is { } existing)
            {
                await existing;
                return;
            }

            Task stopping = StopAsync();
            Close();
            await stopping;
        }
        catch (Exception ex)
        {
            PerformancePanelDiagnostics.Report("close", ex);
        }
    }

    private Task StopAsync()
    {
        timer.Stop();
        timer.Tick -= OnTick;
        if (stopTask is null)
        {
            stopTask = Task.WhenAll(capture.StopAsync(), hardware.StopAsync());
            try
            {
                if (options.PerformancePanelRememberPosition.Value)
                {
                    if (GetWindowRect(windowHandle, out WindowRect bounds))
                    {
                        lastPosition = new PointInt32 { X = bounds.Left, Y = bounds.Top };
                    }

                    options.PerformancePanelPosition.Value = FormattableString.Invariant($"{lastPosition.X},{lastPosition.Y}");
                }
            }
            catch (Exception ex)
            {
                PerformancePanelDiagnostics.Report("save position", ex);
            }
        }

        return stopTask;
    }

    private void OnCloseClick(object sender, RoutedEventArgs args)
    {
        _ = CloseAndStopAsync();
    }

    private async void OnClosed(object sender, WindowEventArgs args)
    {
        Closed -= OnClosed;
        try
        {
            await StopAsync();
        }
        catch (Exception ex)
        {
            PerformancePanelDiagnostics.Report("stop", ex);
        }
    }

    private void OnTick(DispatcherQueueTimer sender, object args)
    {
        try
        {
            UpdatePanel();
        }
        catch (Exception ex)
        {
            PerformancePanelDiagnostics.Report("update", ex);
            _ = CloseAndStopAsync();
        }
    }

    private void UpdatePanel()
    {
        RestoreOrder();
        if (Stopwatch.GetElapsedTime(lastRefresh).TotalSeconds < 1)
        {
            return;
        }

        lastRefresh = Stopwatch.GetTimestamp();
        lastPosition = AppWindow.Position;
        ApplyAppearance();
        PresentMonFpsSample? sample = capture.ReadSample();
        PanelHardwareValues hardwareValues = hardware.Read();
        metrics[0].Value.Text = capture.Failed ? "失败" : sample?.Fps.ToString("0") ?? "—";
        metrics[1].Value.Text = sample?.LowFps?.ToString("0") ?? "—";
        metrics[2].Value.Text = hardwareValues.CpuUsage is { } cpu ? $"{cpu:0}%" : "—";
        metrics[3].Value.Text = hardwareValues.CpuTemperature is { } cpuCelsius ? $"{cpuCelsius}°C" : "—";
        metrics[4].Value.Text = hardwareValues.GpuUsage is { } gpu ? $"{gpu}%" : "—";
        metrics[5].Value.Text = hardwareValues.GpuTemperature is { } gpuCelsius ? $"{gpuCelsius}°C" : "—";
        metrics[6].Value.Text = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private void ApplyAppearance()
    {
        int selected = 0;
        for (int index = 0; index < metrics.Length; index++)
        {
            if (metrics[index].Selected())
            {
                selected |= 1 << index;
            }
        }

        bool selectionChanged = selected != shownMetrics;
        if (selectionChanged)
        {
            ArrangeMetrics(selected);
            shownMetrics = selected;
        }

        if (selected == 0)
        {
            if (AppWindow.IsVisible)
            {
                AppWindow.Hide();
            }

            showWhenGameReturns = true;
        }

        double configuredScale = options.PerformancePanelScale.Value;
        double configuredOpacity = options.PerformancePanelBackgroundOpacity.Value;
        double scale = double.IsFinite(configuredScale) ? Math.Clamp(configuredScale, 70, 170) / 100 : 1;
        double opacity = double.IsFinite(configuredOpacity) ? Math.Clamp(configuredOpacity, 0, 100) / 100 : 0.89;
        Color background = options.PerformancePanelBackgroundColor;
        var appearance = (Scale: scale, Background: background, Opacity: opacity);
        if (!selectionChanged && lastAppearance is { } previous && previous.Equals(appearance))
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
        PointInt32 current = AppWindow.Position;
        PointInt32 saved = default;
        bool restoreSaved = first && TryReadPosition(out saved);
        DisplayArea? display = restoreSaved
            ? DisplayArea.GetFromPoint(saved, DisplayAreaFallback.Nearest)
            : DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        if (display is not null)
        {
            RectInt32 area = display.WorkArea;
            int maxX = Math.Max(area.X, area.X + area.Width - size.Width);
            int maxY = Math.Max(area.Y, area.Y + area.Height - size.Height);
            PointInt32 position = new()
            {
                X = Math.Clamp(restoreSaved ? saved.X : first ? maxX - 24 : current.X, area.X, maxX),
                Y = Math.Clamp(restoreSaved ? saved.Y : first ? area.Y + 24 : current.Y, area.Y, maxY),
            };
            if (position.X != current.X || position.Y != current.Y)
            {
                AppWindow.Move(position);
            }
        }
    }

    private bool TryReadPosition(out PointInt32 position)
    {
        position = default;
        if (!options.PerformancePanelRememberPosition.Value)
        {
            return false;
        }

        string[] coordinates = options.PerformancePanelPosition.Value.Split(',', 2);
        if (coordinates.Length != 2 ||
            !int.TryParse(coordinates[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x) ||
            !int.TryParse(coordinates[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y))
        {
            return false;
        }

        position = new PointInt32 { X = x, Y = y };
        return true;
    }

    private void ArrangeMetrics(int selection)
    {
        int count = System.Numerics.BitOperations.PopCount((uint)selection);
        int columns = count > 1 ? 2 : 1;
        int slot = 0;
        for (int index = 0; index < metrics.Length; index++)
        {
            StackPanel tile = metrics[index].Panel;
            if ((selection & (1 << index)) == 0)
            {
                tile.Visibility = Visibility.Collapsed;
                continue;
            }

            Grid.SetRow(tile, slot / columns);
            Grid.SetColumn(tile, slot % columns);
            Grid.SetColumnSpan(tile, columns == 1 ? 2 : 1);
            tile.Visibility = Visibility.Visible;
            slot++;
        }

        int rows = Math.Max(1, (count + columns - 1) / columns);
        RootBorder.Width = columns == 1 ? 180 : 286;
        RootBorder.Height = 12 + 40 * rows + 4 * (rows - 1);
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

        if (showWhenGameReturns && shownMetrics > 0 && presentation is not null)
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
        MonitorInfo info = new() { Size = Unsafe.SizeOf<MonitorInfo>() };
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

    private sealed record MetricTile(StackPanel Panel, TextBlock Label, TextBlock Value, Func<bool> Selected);

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
