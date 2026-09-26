// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Microsoft.UI.Xaml;
using Snap.Hutao.Remastered.Core.Setting;

namespace Snap.Hutao.Remastered.UI.Xaml.Control.Card;

public sealed class CardReference
{
    public FrameworkElement? Card { get; set; }

    public int Order { get; set; }

    public static CardReference Create(FrameworkElement card, string order)
    {
        return new() { Card = card, Order = LocalSetting.Get(order, 0) };
    }
}
