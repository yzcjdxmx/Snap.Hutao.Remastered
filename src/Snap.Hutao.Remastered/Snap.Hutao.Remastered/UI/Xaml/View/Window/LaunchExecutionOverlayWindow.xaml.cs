// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Snap.Hutao.Remastered.Core.Setting;
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
    private readonly IServiceProvider serviceProvider;
    private LaunchExecutionPerformanceWindow? performancePanel;
    private Task? fpsStopTask;
    private int fpsGameProcessId;
    private bool isClosed;

    [WinRT.DynamicWindowsRuntimeCast(typeof(global::Microsoft.UI.Windowing.OverlappedPresenter))]
    public LaunchExecutionOverlayWindow(IServiceProvider serviceProvider)
    {
        InitializeComponent();
        this.serviceProvider = serviceProvider;

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
        FpsToggle.IsEnabled = processId > 0;
    }

    public Task StopFpsCaptureAsync()
    {
        if (performancePanel is { } panel)
        {
            fpsStopTask = panel.StopAsync();
            panel.Close();
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
        isClosed = true;
        _ = StopFpsCaptureAsync();
        InputLowLevelKeyboardSource.KeyDown -= OnLowLevelKeyDown;
        InputLowLevelKeyboardSource.Uninitialize();
    }

    private void OnFpsToggleClick(object sender, RoutedEventArgs args)
    {
        if (FpsToggle.IsChecked is true)
        {
            performancePanel = new(serviceProvider, fpsGameProcessId, AppWindow.Id);
            performancePanel.Closed += OnPerformancePanelClosed;
            performancePanel.AppWindow.Show(false);
            return;
        }

        _ = StopFpsCaptureAsync();
    }

    private async void OnPerformancePanelClosed(object sender, WindowEventArgs args)
    {
        if (performancePanel is { } panel && sender == panel)
        {
            performancePanel = null;
            panel.Closed -= OnPerformancePanelClosed;
            FpsToggle.IsChecked = false;
            FpsToggle.IsEnabled = false;
            fpsStopTask = panel.StopAsync();
            await fpsStopTask;
            if (!isClosed)
            {
                FpsToggle.IsEnabled = true;
            }
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
