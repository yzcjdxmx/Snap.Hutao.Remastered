// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Snap.Hutao.Remastered.Core;
using Snap.Hutao.Remastered.Service.Game.Launching.Context;
using Snap.Hutao.Remastered.UI.Xaml.View.Window;

namespace Snap.Hutao.Remastered.Service.Game.Launching.Handler;

public sealed class LaunchExecutionOverlayHandler : AbstractLaunchExecutionHandler
{
    private LaunchExecutionOverlayWindow? window;
    private LaunchExecutionPerformanceWindow? performanceWindow;

    public override async ValueTask ExecuteAsync(LaunchExecutionContext context)
    {
        if (!HutaoRuntime.IsProcessElevated ||
            (!context.LaunchOptions.UsingOverlay.Value && !context.LaunchOptions.UsingPerformancePanel.Value))
        {
            return;
        }

        await context.TaskContext.SwitchToMainThreadAsync();
        if (context.LaunchOptions.UsingOverlay.Value)
        {
            window = context.ServiceProvider.GetRequiredService<LaunchExecutionOverlayWindow>();
        }

        if (context.LaunchOptions.UsingPerformancePanel.Value)
        {
            performanceWindow = new(context.ServiceProvider, context.LaunchOptions, context.Process.Id);
        }
    }

    public override async ValueTask AfterAsync(AfterLaunchExecutionContext context)
    {
        if (window is null && performanceWindow is null)
        {
            return;
        }

        await context.TaskContext.SwitchToMainThreadAsync();
        if (window is { } overlay)
        {
            overlay.PreventClose = false;
            overlay.Close();
        }

        if (performanceWindow is { } panel)
        {
            await panel.CloseAndStopAsync();
        }
    }
}
