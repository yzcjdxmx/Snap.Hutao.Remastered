// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Snap.Hutao.Remastered.Core;
using Snap.Hutao.Remastered.Service.Game.Launching.Context;
using Snap.Hutao.Remastered.UI.Xaml.View.Window;

namespace Snap.Hutao.Remastered.Service.Game.Launching.Handler;

public sealed class LaunchExecutionOverlayHandler : AbstractLaunchExecutionHandler
{
    private LaunchExecutionOverlayWindow? window;

    public override async ValueTask ExecuteAsync(LaunchExecutionContext context)
    {
        if (!HutaoRuntime.IsProcessElevated || !context.LaunchOptions.UsingOverlay.Value)
        {
            return;
        }

        await context.TaskContext.SwitchToMainThreadAsync();
        window = context.ServiceProvider.GetRequiredService<LaunchExecutionOverlayWindow>();
        window.SetFpsTarget(context.Process.Id);
    }

    public override async ValueTask AfterAsync(AfterLaunchExecutionContext context)
    {
        if (window is null)
        {
            return;
        }

        await context.TaskContext.SwitchToMainThreadAsync();
        await window.StopFpsCaptureAsync();
        window.PreventClose = false;
        window.Close();
    }
}
