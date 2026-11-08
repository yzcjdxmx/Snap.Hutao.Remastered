// Copyright (c) Snap Hutao RP. All rights reserved.
// Licensed under the MIT license.

namespace Snap.Hutao.Remastered.Core.Caching;

[Service(ServiceLifetime.Singleton)]
public sealed class BundledImageResources
{
    private readonly Lazy<ImageResourceBundle> bundle = new(Load);

    public ImageResourceBundle Bundle => bundle.Value;

    public bool IsAvailable => Bundle.Count > 0;

    private static ImageResourceBundle Load()
    {
        string directory = InstalledLocation.GetAbsolutePath("BundledImages");
        try
        {
            return new(directory);
        }
        catch (Exception ex)
        {
            // A missing/broken optional payload must not prevent online or cached images from loading.
            SentrySdk.CaptureException(ex);
            return new();
        }
    }
}
