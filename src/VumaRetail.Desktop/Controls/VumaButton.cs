using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace VumaRetail.Desktop.Controls;

/// <summary>
/// WPF Button component — primary, secondary, quiet, destructive variants.
/// Both themes, both densities, keyboard-operable, screen-reader labelled.
/// </summary>
public sealed class VumaButton : Button
{
    public static readonly DependencyProperty VariantProperty =
        DependencyProperty.Register(nameof(Variant), typeof(ButtonVariant), typeof(VumaButton),
            new PropertyMetadata(ButtonVariant.Primary));

    public ButtonVariant Variant
    {
        get => (ButtonVariant)GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }

    public static readonly DependencyProperty DensityProperty =
        DependencyProperty.Register(nameof(Density), typeof(Density), typeof(VumaButton),
            new PropertyMetadata(Density.Comfortable));

    public Density Density
    {
        get => (Density)GetValue(DensityProperty);
        set => SetValue(DensityProperty, value);
    }

    public VumaButton()
    {
        AutomationProperties.SetRole(this, AutomationRole.Button);
    }
}

public enum ButtonVariant { Primary, Secondary, Quiet, Destructive }
public enum Density { Compact, Comfortable }
