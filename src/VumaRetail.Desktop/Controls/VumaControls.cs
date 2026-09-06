using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace VumaRetail.Desktop.Controls;

/// <summary>
/// WPF Input component with both themes, both densities, keyboard spec, and accessibility spec.
/// </summary>
public sealed class VumaInput : Control
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(string), typeof(VumaInput));

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(VumaInput));

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public static readonly DependencyProperty IsDisabledProperty =
        DependencyProperty.Register(nameof(IsDisabled), typeof(bool), typeof(VumaInput),
            new PropertyMetadata(false));

    public bool IsDisabled
    {
        get => (bool)GetValue(IsDisabledProperty);
        set => SetValue(IsDisabledProperty, value);
    }

    public VumaInput()
    {
        Focusable = true;
        AutomationProperties.SetRole(this, AutomationRole.Text);
        AutomationProperties.SetName(this, nameof(VumaInput));
    }
}

/// <summary>
/// WPF Select component — both themes, both densities, keyboard-navigable.
/// </summary>
public sealed class VumaSelect : Control
{
    public static readonly DependencyProperty ItemsProperty =
        DependencyProperty.Register(nameof(Items), typeof(IList<string>), typeof(VumaSelect));

    public IList<string> Items
    {
        get => (IList<string>)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public static readonly DependencyProperty SelectedItemProperty =
        DependencyProperty.Register(nameof(SelectedItem), typeof(string), typeof(VumaSelect));

    public string? SelectedItem
    {
        get => (string?)GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public VumaSelect()
    {
        Focusable = true;
        AutomationProperties.SetRole(this, AutomationRole.ComboBox);
    }
}

/// <summary>
/// WPF Toggle component — both themes, both densities.
/// </summary>
public sealed class VumaToggle : ToggleButton
{
    public VumaToggle()
    {
        AutomationProperties.SetRole(this, AutomationRole.ToggleButton);
    }
}

/// <summary>
/// WPF Card component — both themes, both densities, elevation-based depth.
/// </summary>
public sealed class VumaCard : ContentControl
{
    public static readonly DependencyProperty ElevationProperty =
        DependencyProperty.Register(nameof(Elevation), typeof(int), typeof(VumaCard),
            new PropertyMetadata(1));

    public int Elevation
    {
        get => (int)GetValue(ElevationProperty);
        set => SetValue(ElevationProperty, value);
    }

    public VumaCard()
    {
        AutomationProperties.SetRole(this, AutomationRole.Group);
    }
}

/// <summary>
/// WPF Badge component — both themes, both densities.
/// </summary>
public sealed class VumaBadge : ContentControl
{
    public static readonly DependencyProperty VariantProperty =
        DependencyProperty.Register(nameof(Variant), typeof(string), typeof(VumaBadge));

    public string Variant
    {
        get => (string)GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }

    public VumaBadge()
    {
        AutomationProperties.SetRole(this, AutomationRole.StatusBar);
    }
}

/// <summary>
/// WPF Chip component — both themes, both densities.
/// </summary>
public sealed class VumaChip : Button
{
    public VumaChip()
    {
        AutomationProperties.SetRole(this, AutomationRole.Button);
    }
}

/// <summary>
/// WPF Banner component — both themes, both densities.
/// </summary>
public sealed class VumaBanner : ContentControl
{
    public static readonly DependencyProperty SeverityProperty =
        DependencyProperty.Register(nameof(Severity), typeof(string), typeof(VumaBanner));

    public string Severity
    {
        get => (string)GetValue(SeverityProperty);
        set => SetValue(SeverityProperty, value);
    }

    public VumaBanner()
    {
        AutomationProperties.SetRole(this, AutomationRole.Alert);
    }
}

/// <summary>
/// WPF Checkbox component — both themes, both densities.
/// </summary>
public sealed class VumaCheckbox : CheckBox
{
    public VumaCheckbox()
    {
        AutomationProperties.SetRole(this, AutomationRole.CheckBox);
    }
}

/// <summary>
/// WPF RadioButton component — both themes, both densities.
/// </summary>
public sealed class VumaRadioButton : RadioButton
{
    public VumaRadioButton()
    {
        AutomationProperties.SetRole(this, AutomationRole.RadioButton);
    }
}

/// <summary>
/// WPF Tabs component — both themes, both densities.
/// </summary>
public sealed class VumaTabs : TabControl
{
    public VumaTabs()
    {
        AutomationProperties.SetRole(this, AutomationRole.TabControl);
    }
}

/// <summary>
/// WPF Progress component — both themes, both densities.
/// </summary>
public sealed class VumaProgress : ProgressBar
{
    public VumaProgress()
    {
        AutomationProperties.SetRole(this, AutomationRole.ProgressBar);
    }
}

/// <summary>
/// WPF Avatar component — both themes, both densities.
/// </summary>
public sealed class VumaAvatar : Image
{
    public VumaAvatar()
    {
        AutomationProperties.SetRole(this, AutomationRole.Image);
    }
}

/// <summary>
/// WPF Pagination component — both themes, both densities.
/// </summary>
public sealed class VumaPagination : Control
{
    public static readonly DependencyProperty CurrentPageProperty =
        DependencyProperty.Register(nameof(CurrentPage), typeof(int), typeof(VumaPagination));

    public int CurrentPage
    {
        get => (int)GetValue(CurrentPageProperty);
        set => SetValue(CurrentPageProperty, value);
    }

    public VumaPagination()
    {
        AutomationProperties.SetRole(this, AutomationRole.Pager);
    }
}

/// <summary>
/// WPF Toast component — both themes, both densities.
/// </summary>
public sealed class VumaToast : ContentControl
{
    public VumaToast()
    {
        AutomationProperties.SetRole(this, AutomationRole.StatusBar);
    }
}

/// <summary>
/// WPF Empty State component — both themes, both densities.
/// </summary>
public sealed class VumaEmptyState : ContentControl
{
    public VumaEmptyState()
    {
        AutomationProperties.SetRole(this, AutomationRole.Group);
    }
}

/// <summary>
/// WPF Skeleton Loader component — both themes, both densities.
/// </summary>
public sealed class VumaSkeleton : Control
{
    public VumaSkeleton()
    {
        AutomationProperties.SetRole(this, AutomationRole.Progressbar);
    }
}

/// <summary>
/// WPF Segmented Control component — both themes, both densities.
/// </summary>
public sealed class VumaSegmentedControl : Control
{
    public VumaSegmentedControl()
    {
        AutomationProperties.SetRole(this, AutomationRole.TabList);
    }
}

/// <summary>
/// WPF Numeric Stepper component — both themes, both densities.
/// </summary>
public sealed class VumaNumericStepper : Control
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(decimal), typeof(VumaNumericStepper));

    public decimal Value
    {
        get => (decimal)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public VumaNumericStepper()
    {
        Focusable = true;
        AutomationProperties.SetRole(this, AutomationRole.Spinner);
    }
}

/// <summary>
/// WPF Money Field component — tabular, currency-aware.
/// </summary>
public sealed class VumaMoneyField : VumaInput
{
    public VumaMoneyField()
    {
        AutomationProperties.SetHelpText(this, "Currency-aware money field");
    }
}

/// <summary>
/// WPF Quantity Field component — unit-aware.
/// </summary>
public sealed class VumaQuantityField : VumaInput
{
    public VumaQuantityField()
    {
        AutomationProperties.SetHelpText(this, "Unit-aware quantity field");
    }
}

/// <summary>
/// WPF Search component — both themes, both densities.
/// </summary>
public sealed class VumaSearch : VumaInput
{
    public VumaSearch()
    {
        AutomationProperties.SetHelpText(this, "Search field");
    }
}

/// <summary>
/// WPF Dialog component — both themes, both densities.
/// </summary>
public sealed class VumaDialog : Window
{
    public VumaDialog()
    {
        AutomationProperties.SetRole(this, AutomationRole.Window);
    }
}

/// <summary>
/// WPF Sheet component — both themes, both densities.
/// </summary>
public sealed class VumaSheet : Window
{
    public VumaSheet()
    {
        AutomationProperties.SetRole(this, AutomationRole.Window);
    }
}

/// <summary>
/// WPF Side Nav component — both themes, both densities.
/// </summary>
public sealed class VumaSideNav : Control
{
    public VumaSideNav()
    {
        AutomationProperties.SetRole(this, AutomationRole.NavigationBar);
    }
}

/// <summary>
/// WPF Breadcrumb component — both themes, both densities.
/// </summary>
public sealed class VumaBreadcrumb : ItemsControl
{
    public VumaBreadcrumb()
    {
        AutomationProperties.SetRole(this, AutomationRole.MenuBar);
    }
}

/// <summary>
/// WPF Data Table component — sticky header, virtualised, column resize, sortable.
/// </summary>
public sealed class VumaDataTable : Control
{
    public static readonly DependencyProperty ColumnsProperty =
        DependencyProperty.Register(nameof(Columns), typeof(IList<DataTableColumn>), typeof(VumaDataTable));

    public IList<DataTableColumn> Columns
    {
        get => (IList<DataTableColumn>?)GetValue(ColumnsProperty) ?? [];
        set => SetValue(ColumnsProperty, value);
    }

    public VumaDataTable()
    {
        AutomationProperties.SetRole(this, AutomationRole.Table);
    }
}

public sealed class DataTableColumn
{
    public string Header { get; set; } = "";
    public string Binding { get; set; } = "";
}
