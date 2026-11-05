// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using CommunityToolkit.Mvvm.ComponentModel;

namespace Snap.Hutao.Remastered.Core.Property;

public sealed partial class ObservablePropertyReadOnlyWrapper<T> : ObservableObject, IReadOnlyObservableProperty<T>
{
    private readonly IObservableProperty<T> source;

    public ObservablePropertyReadOnlyWrapper(IObservableProperty<T> source)
    {
        this.source = source;
        source.WeakPropertyChanged(this, OnWeakSourceValueChanged);
    }

    public T Value
    {
        get => source.Value;
    }

    private static void OnWeakSourceValueChanged(ObservablePropertyReadOnlyWrapper<T> self, object? sender, PropertyChangedEventArgs e)
    {
        // Compiled x:Bind reads Value from the event sender, which must be the read-only view.
        self.OnPropertyChanged(e);
    }
}

