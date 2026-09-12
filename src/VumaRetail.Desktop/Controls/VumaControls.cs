using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace VumaRetail.Desktop.Controls;

/// <summary>Token-styled text input used by the desktop shell.</summary>
public class VumaInput : TextBox
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(string), typeof(VumaInput),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string Placeholder { get; set; } = string.Empty;

    public VumaInput() => AutomationProperties.SetName(this, nameof(VumaInput));
}

/// <summary>Token-styled select control.</summary>
public sealed class VumaSelect : ComboBox
{
    public VumaSelect() => AutomationProperties.SetName(this, nameof(VumaSelect));
}

/// <summary>Token-styled toggle control.</summary>
public sealed class VumaToggle : ToggleButton
{
    public VumaToggle() => AutomationProperties.SetName(this, nameof(VumaToggle));
}

/// <summary>Border-backed card so the XAML token style can set CornerRadius safely.</summary>
public class VumaCard : Border
{
    public VumaCard() => AutomationProperties.SetName(this, nameof(VumaCard));
}

public sealed class VumaMoneyField : VumaInput;
public sealed class VumaQuantityField : VumaInput;
public sealed class VumaSearch : VumaInput;
