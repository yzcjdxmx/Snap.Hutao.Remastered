// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Snap.Hutao.Remastered.Core.Setting;
using Snap.Hutao.Remastered.Service.Game.Launching;
using Snap.Hutao.Remastered.UI.Input;
using Snap.Hutao.Remastered.UI.Input.LowLevel;
using Snap.Hutao.Remastered.UI.Windowing;
using Snap.Hutao.Remastered.UI.Windowing.Abstraction;
using Snap.Hutao.Remastered.UI.Xaml.Media.Backdrop;
using Snap.Hutao.Remastered.ViewModel.Overlay;
using Snap.Hutao.Remastered.Win32.UI.Input.KeyboardAndMouse;
using System.Collections.Immutable;
using Windows.Graphics;

namespace Snap.Hutao.Remastered.UI.Xaml.View.Window;

[Service(ServiceLifetime.Transient)]
public sealed partial class LaunchExecutionOverlayWindow : Microsoft.UI.Xaml.Window,
    IXamlWindowExtendContentIntoTitleBar,
    IXamlWindowMouseWheelHandler,
    IXamlWindowClosedHandler,
    IWindowNeedEraseBackground
{
    private readonly LowLevelKeyOptions lowLevelKeyOptions;
    private readonly ITaskContext taskContext;
    private readonly DispatcherQueueTimer fpsTimer;
    private PresentMonFpsCapture? fpsCapture;
    private Task? fpsStopTask;
    private int fpsGameProcessId;

    [WinRT.DynamicWindowsRuntimeCast(typeof(global::Microsoft.UI.Windowing.OverlappedPresenter))]
    public LaunchExecutionOverlayWindow(IServiceProvider serviceProvider)
    {
        InitializeComponent();
        fpsTimer = DispatcherQueue.CreateTimer();
        fpsTimer.Interval = TimeSpan.FromSeconds(1);
        fpsTimer.Tick += OnFpsTimerTick;

        AppWindow.Title = "SnapHutaoLaunchExecutionOverlay";
        AppWindow.SafeIsShowInSwitchers(false);

        SizeInt32 size = ScaledSizeInt32.CreateForWindow(592, 56, this);

        // Thanks to @Scighost for the following code
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
            presenter.SetBorderAndTitleBar(false, false);
            presenter.PreferredMinimumWidth = size.Width;
            presenter.PreferredMinimumHeight = size.Height;
            presenter.PreferredMaximumWidth = size.Width;
            presenter.PreferredMaximumHeight = size.Height;
        }

        this.AddExtendedStyleLayered();
        this.RemoveStyleOverlappedWindow();

        SystemBackdrop = new TransparentBackdrop();

        IServiceScope scope = serviceProvider.CreateScope();
        this.InitializeController(scope.ServiceProvider);
        RootView.InitializeDataContext<OverlayViewModel>(scope.ServiceProvider);
        lowLevelKeyOptions = scope.ServiceProvider.GetRequiredService<LowLevelKeyOptions>();
        taskContext = scope.ServiceProvider.GetRequiredService<ITaskContext>();

        AppWindow.Resize(size);
        if (HideByHotKey)
        {
            AppWindow.Hide();
        }

        InputLowLevelKeyboardSource.Initialize();
        InputLowLevelKeyboardSource.KeyDown += OnLowLevelKeyDown;
    }

    public FrameworkElement TitleBarCaptionAccess { get => RootBorder; }

    public ImmutableArray<FrameworkElement> TitleBarPassthrough { get => [FpsToggle]; }

    public bool PreventClose { get; set; } = true;

    public bool HideByHotKey { get => !LocalSetting.Get(SettingKeys.LaunchOverlayWindowIsVisible, true); }

    public bool HideByEvent { get; set; }

    public void SetFpsTarget(int processId)
    {
        fpsGameProcessId = processId;
        FpsToggle.IsEnabled = true;
    }

    public Task StopFpsCaptureAsync()
    {
        if (fpsCapture is { } capture)
        {
            fpsCapture = null;
            fpsTimer.Stop();
            FpsToggle.IsChecked = false;
            FpsToggle.IsEnabled = false;
            FpsReading.Text = "关闭";
            fpsStopTask = capture.StopAsync();
        }

        return fpsStopTask ?? Task.CompletedTask;
    }

    public void OnMouseWheel(ref readonly PointerPointProperties data)
    {
        RootView.DataContext<OverlayViewModel>()?.HandleMouseWheel(data.Delta / 120);
    }

    public void OnWindowClosing(out bool cancel)
    {
        cancel = PreventClose;
    }

    public void OnWindowClosed()
    {
        fpsTimer.Tick -= OnFpsTimerTick;
        _ = StopFpsCaptureAsync();
        InputLowLevelKeyboardSource.KeyDown -= OnLowLevelKeyDown;
        InputLowLevelKeyboardSource.Uninitialize();
    }

    private async void OnFpsToggleClick(object sender, RoutedEventArgs args)
    {
        if (FpsToggle.IsChecked is true)
        {
            fpsCapture = new(fpsGameProcessId);
            FpsReading.Text = "等待数据";
            fpsTimer.Start();
            return;
        }

        await StopFpsCaptureAsync();
        FpsToggle.IsEnabled = true;
    }

    private void OnFpsTimerTick(DispatcherQueueTimer sender, object args)
    {
        if (fpsCapture is { } capture)
        {
            FpsReading.Text = capture.Failed ? "采集失败" : capture.ReadFps()?.ToString("0") ?? "等待数据";
        }
    }

    private void OnLowLevelKeyDown(LowLevelKeyEventArgs args)
    {
        if (args.Handled)
        {
            return;
        }

        VIRTUAL_KEY key = (VIRTUAL_KEY)args.Data.vkCode;
        if (key is VIRTUAL_KEY.VK__none_)
        {
            // Skipping VK__none_ handling
            return;
        }

        if (key == lowLevelKeyOptions.OverlayHideKey.Value)
        {
            if (HideByEvent)
            {
                return;
            }

            taskContext.InvokeOnMainThread(() =>
            {
                if (AppWindow.IsVisible)
                {
                    AppWindow.Hide();
                    LocalSetting.Set(SettingKeys.LaunchOverlayWindowIsVisible, false);
                }
                else
                {
                    AppWindow.Show(false);
                    AppWindow.MoveInZOrderAtTop();
                    LocalSetting.Set(SettingKeys.LaunchOverlayWindowIsVisible, true);
                }
            });
        }
    }
}
