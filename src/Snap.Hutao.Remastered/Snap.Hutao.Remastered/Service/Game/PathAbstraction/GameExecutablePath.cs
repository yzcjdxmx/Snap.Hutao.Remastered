// Copyright (c) Snap Hutao RP. All rights reserved.
// Licensed under the MIT license.

using System.IO;

namespace Snap.Hutao.Remastered.Service.Game.PathAbstraction;

internal static class GameExecutablePath
{
    internal static string Resolve(string path)
    {
        if (File.Exists(path) || Path.GetDirectoryName(path) is not { Length: > 0 } directory)
        {
            return path;
        }

        (string Name, string DataDirectory) alternate = Path.GetFileName(path).ToUpperInvariant() switch
        {
            GameConstants.YuanShenFileNameUpper => (GameConstants.GenshinImpactFileName, GameConstants.GenshinImpactData),
            GameConstants.GenshinImpactFileNameUpper => (GameConstants.YuanShenFileName, GameConstants.YuanShenData),
            _ => default,
        };
        if (alternate.Name is null || !Directory.Exists(Path.Combine(directory, alternate.DataDirectory)))
        {
            return path;
        }

        string alternatePath = Path.Combine(directory, alternate.Name);
        return File.Exists(alternatePath) ? alternatePath : path;
    }
}
