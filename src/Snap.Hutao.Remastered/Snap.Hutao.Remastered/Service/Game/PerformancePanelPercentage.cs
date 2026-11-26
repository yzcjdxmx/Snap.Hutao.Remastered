// Copyright (c) Snap Hutao RP. All rights reserved.
// Licensed under the MIT license.

using System;
using System.Globalization;

namespace Snap.Hutao.Remastered.Service.Game;

internal static class PerformancePanelPercentage
{
    public static float Parse(string value, float fallback, float minimum, float maximum)
    {
        return float.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out float number)
            ? Normalize(number, fallback, minimum, maximum)
            : fallback;
    }

    public static float Normalize(float value, float fallback, float minimum, float maximum)
    {
        return float.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;
    }
}
