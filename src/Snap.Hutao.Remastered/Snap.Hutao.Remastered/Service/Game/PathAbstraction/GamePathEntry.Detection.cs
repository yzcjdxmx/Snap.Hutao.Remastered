// Copyright (c) Snap Hutao RP. All rights reserved.
// Licensed under the MIT license.

using Snap.Hutao.Remastered.Service.Game.Configuration;
using Snap.Hutao.Remastered.Service.Game.Scheme;
using System.IO;

namespace Snap.Hutao.Remastered.Service.Game.PathAbstraction;

public sealed partial class GamePathEntry
{
    [JsonIgnore]
    public string DetectedServerLabel
    {
        get
        {
            string executable = EffectivePath;
            if (!File.Exists(executable))
            {
                return SH.ViewPageLaunchGamePathExecutableMissing;
            }

            string? directory = System.IO.Path.GetDirectoryName(executable);
            if (directory is null)
            {
                return SH.ViewPageLaunchGamePathUnknownServer;
            }

            bool isOversea = string.Equals(System.IO.Path.GetFileName(executable), GameConstants.GenshinImpactFileName, StringComparison.OrdinalIgnoreCase);
            string label = isOversea ? SH.ModelBindingLaunchGameLaunchSchemeOversea : SH.ViewPageLaunchGamePathUnknownServer;
            try
            {
                string config = System.IO.Path.Combine(directory, GameConstants.ConfigFileName);
                ChannelOptions options = GameConfiguration.Read(config, directory, isOversea, requireChannel: true);
                if (options.ErrorKind is ChannelOptionsErrorKind.None &&
                    KnownLaunchSchemes.Values.FirstOrDefault(scheme => scheme.Equals(options)) is { } scheme)
                {
                    label = scheme.DisplayName;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentOutOfRangeException)
            {
                // The configuration may be unavailable or incomplete while a converter is writing it.
            }

            return string.Equals(executable, Path, StringComparison.OrdinalIgnoreCase)
                ? label
                : SH.FormatViewPageLaunchGamePathConvertedServer(label);
        }
    }

    [JsonIgnore]
    internal string EffectivePath => GameExecutablePath.Resolve(Path);

    internal void RefreshDetection()
    {
        OnPropertyChanged(nameof(DetectedServerLabel));
    }
}
