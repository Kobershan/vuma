using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Data;

namespace VumaRetail.Desktop.Controls;

/// <summary>
/// Till line list — dense, tabular, virtualised, with the running total pinned
/// and typeset in <c>display</c> type. The single most-looked-at surface in the system.
/// </summary>
public sealed class TillLineList : Control
{
    public static readonly DependencyProperty LinesProperty =
        DependencyProperty.Register(nameof(Lines), typeof(IList<TillLine>), typeof(TillLineList),
            new PropertyMetadata(null));

    public IList<TillLine>? Lines
    {
        get => (IList<TillLine>?)GetValue(LinesProperty);
        set => SetValue(LinesProperty, value);
    }

    public static readonly DependencyProperty RunningTotalProperty =
        DependencyProperty.Register(nameof(RunningTotal), typeof(string), typeof(TillLineList));

    public string RunningTotal
    {
        get => (string)GetValue(RunningTotalProperty);
        set => SetValue(RunningTotalProperty, value);
    }

    public TillLineList()
    {
        Focusable = true;
        AutomationProperties.SetName(this, nameof(TillLineList));
        AutomationProperties.SetHelpText(this, "Till line list with running total");
    }
}

public sealed class TillLine
{
    public string Item { get; set; } = "";
    public string Quantity { get; set; } = "";
    public string Price { get; set; } = "";
    public string Total { get; set; } = "";
}
