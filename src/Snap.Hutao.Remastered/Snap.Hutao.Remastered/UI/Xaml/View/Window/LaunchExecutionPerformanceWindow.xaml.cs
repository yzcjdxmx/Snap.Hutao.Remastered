// Copyright (c) Snap Hutao RP. All rights reserved.
// Licensed under the MIT license.

using Microsoft.UI;
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
    private readonly int gameProcessId;
    private readonly nint windowHandle;
    private readonly PresentMonFpsCapture capture;
    private readonly DispatcherQueueTimer timer;
    private GamePresentation? lastPresentation;
    private int restackAttempts;
    private long lastRefresh;
    private Task? stopTask;

    [WinRT.DynamicWindowsRuntimeCast(typeof(global::Microsoft.UI.Windowing.OverlappedPresenter))]
    public LaunchExecutionPerformanceWindow(IServiceProvider serviceProvider, int gameProcessId, WindowId ownerWindow)
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
        if (DisplayArea.GetFromWindowId(ownerWindow, DisplayAreaFallback.Primary) is { } display)
        {
            RectInt32 area = display.WorkArea;
            AppWindow.Move(new PointInt32 { X = area.X + area.Width - size.Width - 24, Y = area.Y + 24 });
        }

        capture = new(gameProcessId);
        timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(250);
        timer.Tick += OnTick;
        timer.Start();
    }

    public FrameworkElement TitleBarCaptionAccess => RootBorder;

    public ImmutableArray<FrameworkElement> TitleBarPassthrough => [CloseButton];

    public Task StopAsync()
    {
        timer.Stop();
        timer.Tick -= OnTick;
        return stopTask ??= capture.StopAsync();
    }

    private void OnCloseClick(object sender, RoutedEventArgs args)
    {
        Close();
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
        FpsValue.Text = sample?.Fps.ToString("0") ?? "—";
        LowValue.Text = sample?.LowFps?.ToString("0") ?? "—";
        ToolTipService.SetToolTip(FpsMetric, capture.Failed ? "采集失败，可关闭面板后重新开启" : null);
    }

    private void RestoreOrder()
    {
        nint foreground = GetForegroundWindow();
        GamePresentation? presentation = null;
        if (foreground != 0 && GetWindowThreadProcessId(foreground, out uint processId) != 0 &&
            processId == gameProcessId && GetWindowRect(foreground, out WindowRect bounds))
        {
            presentation = new(foreground, bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
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

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint window, out uint processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint window, out WindowRect rectangle);

    [LibraryImport("user32.dll")]
    private static partial nint GetWindow(nint window, uint command);

    private readonly record struct GamePresentation(nint Handle, int Left, int Top, int Right, int Bottom);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
