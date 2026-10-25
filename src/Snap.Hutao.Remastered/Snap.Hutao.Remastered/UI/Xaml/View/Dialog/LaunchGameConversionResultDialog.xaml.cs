// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Snap.Hutao.Remastered.UI.Xaml.View.Dialog;

[DependencyProperty<string>("Message")]
[DependencyProperty<string>("ServerRoute")]
public sealed partial class LaunchGameConversionResultDialog : ContentDialog
{
    public LaunchGameConversionResultDialog(IServiceProvider serviceProvider)
    {
        InitializeComponent();
        DataContext = this;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ConfirmButton.Focus(FocusState.Programmatic);
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        Hide();
    }
}
