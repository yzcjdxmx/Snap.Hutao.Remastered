// Copyright (c) Millennium-Science-Technology-R-D-Inst. All rights reserved.
// Licensed under the MIT license.

using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Markup;
using System.Windows.Input;
using WinUI.LiquidGlass;
using XamlControl = Microsoft.UI.Xaml.Controls.Control;

namespace Snap.Hutao.Remastered.UI.Xaml.Control.Theme;

/// <summary>A home card keeps one content tree while its clickable surface changes material.</summary>
[ContentProperty(Name = nameof(Body))]
public class MaterialCardButton : UserControl
{
    private readonly Button nativeButton = new();
    private readonly LiquidGlassButton glassButton = new();
    private readonly SwitchPresenter presenter = new();

    public static readonly DependencyProperty BodyProperty = DependencyProperty.Register(
        nameof(Body), typeof(UIElement), typeof(MaterialCardButton), new PropertyMetadata(null, OnBodyChanged));
    public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(
        nameof(Command), typeof(ICommand), typeof(MaterialCardButton), new PropertyMetadata(null));
    public static readonly DependencyProperty CommandParameterProperty = DependencyProperty.Register(
        nameof(CommandParameter), typeof(object), typeof(MaterialCardButton), new PropertyMetadata(null));

    public MaterialCardButton()
    {
        IsTabStop = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Center;
        presenter.IsTabStop = false;
        presenter.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        presenter.VerticalContentAlignment = VerticalAlignment.Stretch;
        presenter.SwitchCases.Add(new Case { Value = 0, Content = nativeButton });
        presenter.SwitchCases.Add(new Case { Value = 1, Content = glassButton });
        presenter.RegisterPropertyChangedCallback(SwitchPresenter.ValueProperty, (_, _) => MoveBody());
        glassButton.GlassBrush = LiquidGlassPresets.CreateBrush(LiquidGlassPreset.Panel);
        ControlMaterialState.SetIsManaged(glassButton, true);

        foreach (Button button in new Button[] { nativeButton, glassButton })
        {
            Bind(button, Button.CommandProperty, nameof(Command));
            Bind(button, Button.CommandParameterProperty, nameof(CommandParameter));
            Bind(button, XamlControl.PaddingProperty, nameof(Padding));
            Bind(button, XamlControl.ForegroundProperty, nameof(Foreground));
            Bind(button, XamlControl.BorderThicknessProperty, nameof(BorderThickness));
            Bind(button, XamlControl.HorizontalContentAlignmentProperty, nameof(HorizontalContentAlignment));
            Bind(button, XamlControl.VerticalContentAlignmentProperty, nameof(VerticalContentAlignment));
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.VerticalAlignment = VerticalAlignment.Stretch;
        }
        nativeButton.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        Content = presenter;
        Loaded += (_, _) =>
        {
            ControlMaterialState? state = null;
            foreach (ResourceDictionary dictionary in Application.Current.Resources.MergedDictionaries)
            {
                if (dictionary.TryGetValue("ControlMaterialState", out object? value) && value is ControlMaterialState selection)
                {
                    state = selection;
                    break;
                }
            }
            if (state is not null)
            {
                presenter.SetBinding(SwitchPresenter.ValueProperty, new Binding
                {
                    Source = state,
                    Path = new PropertyPath(nameof(ControlMaterialState.CardMaterial)),
                    Mode = BindingMode.OneWay,
                });
            }
            MoveBody();
        };
    }

    public UIElement? Body { get => (UIElement?)GetValue(BodyProperty); set => SetValue(BodyProperty, value); }
    public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }
    public object? CommandParameter { get => GetValue(CommandParameterProperty); set => SetValue(CommandParameterProperty, value); }

    private void Bind(FrameworkElement target, DependencyProperty property, string path) => target.SetBinding(property,
        new Binding { Source = this, Path = new PropertyPath(path), Mode = BindingMode.OneWay });

    private static void OnBodyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((MaterialCardButton)sender).MoveBody();

    private void MoveBody()
    {
        Button active = presenter.Value is 1 ? glassButton : nativeButton;
        if (ReferenceEquals(active.Content, Body)) return;
        nativeButton.Content = null;
        glassButton.Content = null;
        active.Content = Body;
    }
}
