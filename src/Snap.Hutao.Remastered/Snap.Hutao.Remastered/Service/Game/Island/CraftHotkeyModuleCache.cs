// Licensed under the MIT license.
using System;
using System.IO;
using System.Security.Cryptography;

namespace Snap.Hutao.Remastered.Service.Game.Island;

internal static class CraftHotkeyModuleCache
{
    public static string Prepare(string sourcePath, string cacheDirectory)
    {
        using FileStream source = File.OpenRead(sourcePath);
        string hash = Convert.ToHexString(SHA256.HashData(source));
        Directory.CreateDirectory(cacheDirectory);
        // A content-specific path avoids overwriting a DLL still loaded by a game.
        string targetPath = Path.Combine(cacheDirectory, $"CraftModifiersIsland_{hash}.dll");
        if (!File.Exists(targetPath))
        {
            File.Copy(sourcePath, targetPath);
        }
        return targetPath;
    }
}
