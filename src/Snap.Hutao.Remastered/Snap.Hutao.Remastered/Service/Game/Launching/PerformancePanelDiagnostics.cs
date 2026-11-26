// Copyright (c) Snap Hutao RP. All rights reserved.
// Licensed under the MIT license.

using Snap.Hutao.Remastered.Core;
using System.IO;

namespace Snap.Hutao.Remastered.Service.Game.Launching;

internal static class PerformancePanelDiagnostics
{
    public static void Report(string operation, Exception error)
    {
        try
        {
            string path = HutaoRuntime.GetDataDirectoryFile("PerformancePanel.error.log");
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} [{operation}]{Environment.NewLine}{error}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unavailable diagnostics directory must not interrupt the game launcher.
        }
    }
}
