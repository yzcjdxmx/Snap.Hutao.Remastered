// Copyright (c) Snap Hutao RP. All rights reserved.
// Licensed under the MIT license.

using System.IO;
using System.Security.Cryptography;

namespace Snap.Hutao.Remastered.Core.Caching;

/// <summary>A versioned, read-only image snapshot. Cache files remain independent of its version.</summary>
public sealed class ImageResourceBundle
{
    private readonly string directory;
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private readonly HashSet<long> lengths = [];
    private readonly Dictionary<string, List<Entry>> hashes = new(StringComparer.OrdinalIgnoreCase);

    public ImageResourceBundle()
    {
        directory = string.Empty;
    }

    public ImageResourceBundle(string directory)
    {
        this.directory = Path.GetFullPath(directory);
        string manifest = Path.Combine(directory, "manifest.json");
        if (!File.Exists(manifest))
        {
            return;
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(manifest));
        JsonElement root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1)
        {
            throw new InvalidDataException("Unsupported bundled image manifest.");
        }

        Version = root.GetProperty("resourceVersion").GetString() ?? throw new InvalidDataException("Missing image resource version.");
        foreach (JsonElement item in root.GetProperty("files").EnumerateArray())
        {
            string key = item.GetProperty("path").GetString() ?? string.Empty;
            string hash = item.GetProperty("sha256").GetString() ?? string.Empty;
            long length = item.GetProperty("length").GetInt64();
            if (!IsResourcePath(key) || hash.Length != 64 || !hash.All(Uri.IsHexDigit) || length <= 0)
            {
                throw new InvalidDataException($"Invalid bundled image entry: {key}");
            }

            Entry entry = new(key, hash, length);
            entries.Add(key, entry);
            lengths.Add(length);
            if (!hashes.TryGetValue(hash, out List<Entry>? matches))
            {
                hashes.Add(hash, matches = []);
            }

            matches.Add(entry);
        }
    }

    public string? Version { get; }

    public int Count => entries.Count;

    public IEnumerable<string> ResourcePaths => entries.Keys;

    public static bool IsResourcePath(string path)
    {
        string[] parts = path.Split('/');
        return parts.Length == 2 && parts.All(part => part.Length > 0 && part is not ("." or "..") &&
            part.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.'));
    }

    public string? Find(string resourcePath)
    {
        if (!entries.TryGetValue(resourcePath, out Entry? entry))
        {
            return null;
        }

        string path = Path.Combine(directory, entry.Path.Replace('/', Path.DirectorySeparatorChar));
        FileInfo file = new(path);
        return file.Exists && file.Length == entry.Length ? path : null;
    }

    /// <summary>Compare bytes, not names or game versions, before removing any writable file.</summary>
    public async ValueTask<string?> FindIdenticalAsync(string cacheFile, string resourcePath, CancellationToken token = default)
    {
        FileInfo file = new(cacheFile);
        if (!file.Exists || !lengths.Contains(file.Length))
        {
            return null;
        }

        string hash = await HashFileAsync(cacheFile, token).ConfigureAwait(false);
        if (hashes.TryGetValue(hash, out List<Entry>? matches))
        {
            foreach (Entry entry in matches)
            {
                if (!string.Equals(entry.Path, resourcePath, StringComparison.Ordinal))
                {
                    continue;
                }

                string? path = Find(entry.Path);
                if (path is not null && string.Equals(await HashFileAsync(path, token).ConfigureAwait(false), hash, StringComparison.OrdinalIgnoreCase))
                {
                    return path;
                }
            }
        }

        return null;
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken token)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false));
    }

    private sealed record Entry(string Path, string Hash, long Length);
}
