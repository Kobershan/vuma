using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace VumaRetail.Desktop.Controls;

/// <summary>
/// Stat tile — one number, one label, one comparison, one sparkline. Never more.
/// Built with the <c>display</c> type for the primary number as required by the design system.
/// </summary>
public sealed class StatTile : Control
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(string), typeof(StatTile));

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(StatTile));

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public static readonly DependencyProperty ComparisonProperty =
        DependencyProperty.Register(nameof(Comparison), typeof(string), typeof(StatTile));

    public string Comparison
    {
        get => (string)GetValue(ComparisonProperty);
        set => SetValue(ComparisonProperty, value);
    }

    public StatTile()
    {
        Focusable = false;
        AutomationProperties.SetName(this, nameof(StatTile));
    }
}
