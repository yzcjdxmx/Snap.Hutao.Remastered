// Copyright (c) Snap Hutao RP. All rights reserved.
// Licensed under the MIT license.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Snap.Hutao.Remastered.Service.Game;

namespace Snap.Hutao.Remastered.UI.Xaml.View.Page;

public sealed partial class LaunchGamePage
{
    private void OnPerformanceMetricColorsOpening(object sender, object args)
    {
        if (PerformanceMetricSelector.SelectedIndex < 0)
        {
            PerformanceMetricSelector.SelectedIndex = 0;
        }

        LoadPerformanceMetricColor();
    }

    private void OnPerformanceMetricSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        LoadPerformanceMetricColor();
    }

    private void OnPerformanceMetricColorApply(object sender, RoutedEventArgs args)
    {
        if (SelectedPerformanceMetric() is { } metric)
        {
            DataContext.LaunchOptions.SetPerformancePanelMetricColor(metric, PerformanceMetricColorPicker.Color);
        }
    }

    private void OnPerformanceMetricColorReset(object sender, RoutedEventArgs args)
    {
        if (SelectedPerformanceMetric() is { } metric)
        {
            DataContext.LaunchOptions.SetPerformancePanelMetricColor(metric, null);
            LoadPerformanceMetricColor();
        }
    }

    private void LoadPerformanceMetricColor()
    {
        if (PerformanceMetricColorPicker is not null && SelectedPerformanceMetric() is { } metric)
        {
            LaunchOptions options = DataContext.LaunchOptions;
            PerformanceMetricColorPicker.Color = options.GetPerformancePanelMetricColor(metric) ?? options.GetPerformancePanelDefaultTextColor();
        }
    }

    private PerformancePanelMetric? SelectedPerformanceMetric()
    {
        return PerformanceMetricSelector?.SelectedItem is ComboBoxItem { Tag: string name } &&
            Enum.TryParse(name, out PerformancePanelMetric metric) ? metric : null;
    }
}
