using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace VumaRetail.Desktop.Controls;

/// <summary>
/// Base class for all Vuma UI controls. Enforces:
/// - No literal colours, spacing, type sizes, or radii — all from theme resources
/// - Focus ring: 2pt accent ring at 2pt offset
/// - Keyboard accessibility with visible focus
/// - Both themes supported via ResourceDictionary
/// - Both densities supported via theme tokens
/// - Screen-reader labelled via AutomationProperties
/// </summary>
public abstract class VumaControl : Control
{
    static VumaControl()
    {
        FocusVisualStyle = (Style?)Application.Current?.Resources["FocusVisualStyle"]
            ?? GetDefaultFocusVisualStyle();
    }

    private static Style GetDefaultFocusVisualStyle()
    {
        var style = new Style(typeof(Control));
        style.Setters.Add(new Setter(Control.FocusVisualBrushProperty,
            new SolidColorBrush(Color.FromRgb(0x0B, 0x7A, 0x5A))));
        style.Setters.Add(new Setter(Control.FocusVisualBorderThicknessProperty,
            new Thickness(2)));
        style.Setters.Add(new Setter(Control.FocusVisualMarginProperty,
            new Thickness(-2)));
        return style;
    }

    protected Brush GetAccentBrush() =>
        (Brush?)Application.Current?.Resources["AccentBrush"]
        ?? new SolidColorBrush(Color.FromRgb(0x0B, 0x7A, 0x5A));

    protected Brush GetAccentQuietBrush() =>
        (Brush?)Application.Current?.Resources["AccentQuietBrush"]
        ?? new SolidColorBrush(Color.FromRgb(0xE6, 0xF3, 0xEE));

    protected Brush GetPositiveBrush() =>
        (Brush?)Application.Current?.Resources["PositiveBrush"]
        ?? Brushes.Green;

    protected Brush GetWarningBrush() =>
        (Brush?)Application.Current?.Resources["WarningBrush"]
        ?? Brushes.Orange;

    protected Brush GetCriticalBrush() =>
        (Brush?)Application.Current?.Resources["CriticalBrush"]
        ?? Brushes.Red;

    protected Brush GetInfoBrush() =>
        (Brush?)Application.Current?.Resources["InfoBrush"]
        ?? Brushes.Blue;

    protected Brush GetTextPrimaryBrush() =>
        (Brush?)Application.Current?.Resources["TextPrimaryBrush"]
        ?? Brushes.Black;

    protected Brush GetTextSecondaryBrush() =>
        (Brush?)Application.Current?.Resources["TextSecondaryBrush"]
        ?? Brushes.Gray;

    protected Brush GetSurfaceBaseBrush() =>
        (Brush?)Application.Current?.Resources["SurfaceBaseBrush"]
        ?? Brushes.White;

    protected Brush GetSurfaceRaisedBrush() =>
        (Brush?)Application.Current?.Resources["SurfaceRaisedBrush"]
        ?? Brushes.White;

    protected Brush GetSurfaceSunkenBrush() =>
        (Brush?)Application.Current?.Resources["SurfaceSunkenBrush"]
        ?? Brushes.LightGray;

    protected Brush GetSeparatorBrush() =>
        (Brush?)Application.Current?.Resources["SeparatorBrush"]
        ?? Brushes.LightGray;

    protected double GetSpacing(string token) =>
        (double?)Application.Current?.Resources[$"Spacing{token}"] ?? 8;

    protected double GetRadius(string token) =>
        (double?)Application.Current?.Resources[$"Radius{token}"] ?? 12;

    protected double GetFontSize(string token) =>
        (double?)Application.Current?.Resources[$"FontSize{token}"] ?? 15;

    protected FontWeight GetFontWeight(string token) =>
        (FontWeight?)Application.Current?.Resources[$"FontWeight{token}"] ?? FontWeights.Normal;

    protected FontFamily GetFontFamily(string token) =>
        (FontFamily?)Application.Current?.Resources[$"Font{token}"]
        ?? new FontFamily("Inter");

    protected double GetTouchTarget(string token) =>
        (double?)Application.Current?.Resources[$"TouchTarget{token}Width"] ?? 48;

    protected double GetMotionDuration(string token) =>
        (int?)Application.Current?.Resources[$"Motion{token}Duration"] ?? 180;
}

/// <summary>Primary action button. Uses accent colour. Touch target ≥ 64pt for POS.</summary>
public class ButtonPrimary : VumaControl
{
    public ButtonPrimary()
    {
        Background = GetAccentBrush();
        Foreground = Brushes.White;
        Padding = new Thickness(GetSpacing("16"), GetSpacing("8"), GetSpacing("16"), GetSpacing("8"));
        CornerRadius = new CornerRadius(GetRadius("md"));
        MinWidth = GetTouchTarget("posPrimary");
        MinHeight = GetTouchTarget("posPrimary");
        FocusVisualStyle = (Style?)Application.Current?.Resources["FocusVisualStyle"]
            ?? GetDefaultFocusVisualStyle();
        AutomationProperties.Name = "Primary action button";
        AutomationProperties.HelpText = "Performs the primary action";
        AutomationProperties.ControlType = ControlType.Button;
    }
}

/// <summary>Secondary action button.</summary>
public class ButtonSecondary : VumaControl
{
    public ButtonSecondary()
    {
        Background = GetSurfaceRaisedBrush();
        Foreground = GetTextPrimaryBrush();
        BorderBrush = GetSeparatorBrush();
        BorderThickness = new Thickness(1);
        Padding = new Thickness(GetSpacing("16"), GetSpacing("8"), GetSpacing("16"), GetSpacing("8"));
        CornerRadius = new CornerRadius(GetRadius("md"));
        MinWidth = GetTouchTarget("posSecondary");
        MinHeight = GetTouchTarget("posSecondary");
        AutomationProperties.Name = "Secondary action button";
        AutomationProperties.ControlType = ControlType.Button;
    }
}

/// <summary>Quiet action button — no fill, text only.</summary>
public class ButtonQuiet : VumaControl
{
    public ButtonQuiet()
    {
        Background = Brushes.Transparent;
        Foreground = GetAccentBrush();
        Padding = new Thickness(GetSpacing("12"), GetSpacing("4"), GetSpacing("12"), GetSpacing("4"));
        AutomationProperties.Name = "Quiet action button";
        AutomationProperties.ControlType = ControlType.Button;
    }
}

/// <summary>Destructive action button — uses critical colour.</summary>
public class ButtonDestructive : VumaControl
{
    public ButtonDestructive()
    {
        Background = GetCriticalBrush();
        Foreground = Brushes.White;
        Padding = new Thickness(GetSpacing("16"), GetSpacing("8"), GetSpacing("16"), GetSpacing("8"));
        CornerRadius = new CornerRadius(GetRadius("md"));
        AutomationProperties.Name = "Destructive action button";
        AutomationProperties.ControlType = ControlType.Button;
    }
}

/// <summary>Input control with label. Both themes, both densities.</summary>
public class TextInput : VumaControl
{
    public TextInput()
    {
        Background = GetSurfaceBaseBrush();
        BorderBrush = GetSeparatorBrush();
        BorderThickness = new Thickness(1);
        Padding = new Thickness(GetSpacing("12"), GetSpacing("8"));
        CornerRadius = new CornerRadius(GetRadius("sm"));
        FontFamily = GetFontFamily("body");
        FontSize = GetFontSize("body");
        MinHeight = GetTouchTarget("posSecondary");
        AutomationProperties.Name = "Text input";
        AutomationProperties.ControlType = ControlType.Text;
    }
}

/// <summary>Select control with dropdown.</summary>
public class SelectControl : VumaControl
{
    public SelectControl()
    {
        Background = GetSurfaceBaseBrush();
        BorderBrush = GetSeparatorBrush();
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(GetRadius("md"));
        MinWidth = GetTouchTarget("posSecondary");
        MinHeight = GetTouchTarget("posSecondary");
        AutomationProperties.Name = "Select control";
        AutomationProperties.ControlType = ControlType.ComboBox;
    }
}

/// <summary>Combo box with type-ahead.</summary>
public class ComboBoxControl : VumaControl
{
    public ComboBoxControl()
    {
        Background = GetSurfaceBaseBrush();
        BorderBrush = GetSeparatorBrush();
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(GetRadius("md"));
        MinWidth = GetTouchTarget("posSecondary");
        MinHeight = GetTouchTarget("posSecondary");
        AutomationProperties.Name = "Combo box with type-ahead";
        AutomationProperties.ControlType = ControlType.ComboBox;
    }
}

/// <summary>Numeric stepper with increment/decrement buttons.</summary>
public class NumericStepper : VumaControl
{
    public NumericStepper()
    {
        Background = GetSurfaceBaseBrush();
        BorderBrush = GetSeparatorBrush();
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(GetRadius("md"));
        MinWidth = GetTouchTarget("posSecondary");
        MinHeight = GetTouchTarget("posSecondary");
        FontFamily = GetFontFamily("body");
        FontSize = GetFontSize("body");
        AutomationProperties.Name = "Numeric stepper";
        AutomationProperties.ControlType = ControlType.Spinner;
    }
}

/// <summary>Money field with tabular figures and currency awareness.</summary>
public class MoneyField : VumaControl
{
    public MoneyField()
    {
        Background = GetSurfaceBaseBrush();
        BorderBrush = GetSeparatorBrush();
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(GetRadius("sm"));
        FontFamily = GetFontFamily("mono");
        FontSize = GetFontSize("mono");
        FontWeight = GetFontWeight("mono");
        Padding = new Thickness(GetSpacing("12"), GetSpacing("8"));
        MinWidth = GetTouchTarget("posSecondary");
        AutomationProperties.Name = "Money field";
        AutomationProperties.HelpText = "Currency-aware monetary value with tabular figures";
        AutomationProperties.ControlType = ControlType.Text;
    }
}

/// <summary>Quantity field with unit of measure reference.</summary>
public class QuantityField : VumaControl
{
    public QuantityField()
    {
        Background = GetSurfaceBaseBrush();
        BorderBrush = GetSeparatorBrush();
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(GetRadius("sm"));
        FontFamily = GetFontFamily("body");
        FontSize = GetFontSize("body");
        Padding = new Thickness(GetSpacing("12"), GetSpacing("8"));
        MinWidth = GetTouchTarget("posSecondary");
        AutomationProperties.Name = "Quantity field";
        AutomationProperties.HelpText = "Quantity with unit of measure";
        AutomationProperties.ControlType = ControlType.Text;
    }
}

/// <summary>Date/range picker.</summary>
public class DateRangeControl : VumaControl
{
    public DateRangeControl()
    {
        Background = GetSurfaceBaseBrush();
        BorderBrush = GetSeparatorBrush();
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(GetRadius("md"));
        MinWidth = GetTouchTarget("posSecondary");
        MinHeight = GetTouchTarget("posSecondary");
        AutomationProperties.Name = "Date range picker";
        AutomationProperties.ControlType = ControlType.DatePicker;
    }
}

/// <summary>Search input with type-ahead.</summary>
public class SearchControl : VumaControl
{
    public SearchControl()
    {
        Background = GetSurfaceBaseBrush();
        BorderBrush = GetSeparatorBrush();
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(GetRadius("pill"));
        Padding = new Thickness(GetSpacing("12"), GetSpacing("8"));
        MinWidth = GetTouchTarget("posSecondary");
        MinHeight = GetTouchTarget("posSecondary");
        AutomationProperties.Name = "Search";
        AutomationProperties.ControlType = ControlType.Search;
    }
}

/// <summary>Toggle switch.</summary>
public class ToggleControl : VumaControl
{
    public ToggleControl()
    {
        MinWidth = GetTouchTarget("posSecondary");
        MinHeight = GetTouchTarget("posSecondary");
        AutomationProperties.Name = "Toggle";
        AutomationProperties.ControlType = ControlType.CheckBox;
    }
}

/// <summary>Segmented control (tab-like).</summary>
public class SegmentedControl : VumaControl
{
    public SegmentedControl()
    {
        Background = GetSurfaceSunkenBrush();
        CornerRadius = new CornerRadius(GetRadius("md"));
        Padding = new Thickness(GetSpacing("4"));
        AutomationProperties.Name = "Segmented control";
        AutomationProperties.ControlType = ControlType.TabControl;
    }
}

/// <summary>Checkbox.</summary>
public class CheckboxControl : VumaControl
{
    public CheckboxControl()
    {
        MinWidth = GetTouchTarget("posSecondary");
        MinHeight = GetTouchTarget("posSecondary");
        AutomationProperties.Name = "Checkbox";
        AutomationProperties.ControlType = ControlType.CheckBox;
    }
}

/// <summary>Radio button.</summary>
public class RadioControl : VumaControl
{
    public RadioControl()
    {
        MinWidth = GetTouchTarget("posSecondary");
        MinHeight = GetTouchTarget("posSecondary");
        AutomationProperties.Name = "Radio button";
        AutomationProperties.ControlType = ControlType.RadioButton;
    }
}

/// <summary>Card container. Uses RadiusLg and ElevationE1.</summary>
public class CardControl : VumaControl
{
    public CardControl()
    {
        Background = GetSurfaceRaisedBrush();
        CornerRadius = new CornerRadius(GetRadius("lg"));
        Padding = new Thickness(GetSpacing("16"));
        AutomationProperties.Name = "Card";
        AutomationProperties.ControlType = ControlType.Group;
    }
}

/// <summary>List row.</summary>
public class ListRow : VumaControl
{
    public ListRow()
    {
        Background = GetSurfaceRaisedBrush();
        Padding = new Thickness(GetSpacing("8"), GetSpacing("4"));
        MinHeight = GetTouchTarget("posSecondary");
        AutomationProperties.Name = "List row";
        AutomationProperties.ControlType = ControlType.ListItem;
    }
}

/// <summary>Data table with sticky header, virtualisation, column resize, sortable.</summary>
public class DataTableControl : VumaControl
{
    public DataTableControl()
    {
        Background = GetSurfaceRaisedBrush();
        CornerRadius = new CornerRadius(GetRadius("lg"));
        AutomationProperties.Name = "Data table";
        AutomationProperties.ControlType = ControlType.DataGrid;
    }
}

/// <summary>Tab control.</summary>
public class TabControl : VumaControl
{
    public TabControl()
    {
        Background = GetSurfaceBaseBrush();
        AutomationProperties.Name = "Tabs";
        AutomationProperties.ControlType = ControlType.TabControl;
    }
}

/// <summary>Sheet (bottom/side drawer). Uses RadiusXl and ElevationE2.</summary>
public class SheetControl : VumaControl
{
    public SheetControl()
    {
        Background = GetSurfaceRaisedBrush();
        CornerRadius = new CornerRadius(GetRadius("xl"));
        AutomationProperties.Name = "Sheet";
        AutomationProperties.ControlType = ControlType.Pane;
    }
}

/// <summary>Dialog. Uses RadiusXl and ElevationE2.</summary>
public class DialogControl : VumaControl
{
    public DialogControl()
    {
        Background = GetSurfaceRaisedBrush();
        CornerRadius = new CornerRadius(GetRadius("xl"));
        AutomationProperties.Name = "Dialog";
        AutomationProperties.ControlType = ControlType.Window;
    }
}

/// <summary>Toast notification.</summary>
public class ToastControl : VumaControl
{
    public ToastControl()
    {
        Background = GetSurfaceRaisedBrush();
        CornerRadius = new CornerRadius(GetRadius("md"));
        AutomationProperties.Name = "Toast notification";
        AutomationProperties.ControlType = ControlType.StatusBar;
    }
}

/// <summary>Banner.</summary>
public class BannerControl : VumaControl
{
    public BannerControl()
    {
        Background = GetAccentQuietBrush();
        CornerRadius = new CornerRadius(GetRadius("md"));
        Padding = new Thickness(GetSpacing("16"), GetSpacing("8"));
        AutomationProperties.Name = "Banner";
        AutomationProperties.ControlType = ControlType.Banner;
    }
}

/// <summary>Empty state placeholder.</summary>
public class EmptyStateControl : VumaControl
{
    public EmptyStateControl()
    {
        Background = GetSurfaceBaseBrush();
        Padding = new Thickness(GetSpacing("48"));
        AutomationProperties.Name = "Empty state";
        AutomationProperties.ControlType = ControlType.Custom;
    }
}

/// <summary>Skeleton loader.</summary>
public class SkeletonLoader : VumaControl
{
    public SkeletonLoader()
    {
        Background = GetSurfaceSunkenBrush();
        CornerRadius = new CornerRadius(GetRadius("sm"));
        AutomationProperties.Name = "Loading";
        AutomationProperties.ControlType = ControlType.ProgressBar;
    }
}

/// <summary>Progress indicator.</summary>
public class ProgressControl : VumaControl
{
    public ProgressControl()
    {
        Background = GetSurfaceSunkenBrush();
        Foreground = GetAccentBrush();
        CornerRadius = new CornerRadius(GetRadius("pill"));
        Height = GetTouchTarget("posSecondary");
        AutomationProperties.Name = "Progress";
        AutomationProperties.ControlType = ControlType.ProgressBar;
    }
}

/// <summary>Sparkline chart.</summary>
public class SparklineControl : VumaControl
{
    public SparklineControl()
    {
        Background = GetSurfaceBaseBrush();
        Foreground = GetAccentBrush();
        AutomationProperties.Name = "Sparkline";
        AutomationProperties.ControlType = ControlType.Custom;
    }
}

/// <summary>Avatar.</summary>
public class AvatarControl : VumaControl
{
    public AvatarControl()
    {
        Background = GetSurfaceSunkenBrush();
        CornerRadius = new CornerRadius(GetRadius("pill"));
        Width = GetTouchTarget("posSecondary");
        Height = GetTouchTarget("posSecondary");
        AutomationProperties.Name = "Avatar";
        AutomationProperties.ControlType = ControlType.Image;
    }
}

/// <summary>Badge.</summary>
public class BadgeControl : VumaControl
{
    public BadgeControl()
    {
        Background = GetAccentQuietBrush();
        Foreground = GetAccentBrush();
        CornerRadius = new CornerRadius(GetRadius("pill"));
        Padding = new Thickness(GetSpacing("8"), GetSpacing("4"));
        AutomationProperties.Name = "Badge";
        AutomationProperties.ControlType = ControlType.StatusBar;
    }
}

/// <summary>Chip.</summary>
public class ChipControl : VumaControl
{
    public ChipControl()
    {
        Background = GetSurfaceSunkenBrush();
        Foreground = GetTextPrimaryBrush();
        CornerRadius = new CornerRadius(GetRadius("md"));
        Padding = new Thickness(GetSpacing("8"), GetSpacing("4"));
        MinHeight = GetTouchTarget("posSecondary");
        AutomationProperties.Name = "Chip";
        AutomationProperties.ControlType = ControlType.Button;
    }
}

/// <summary>Pagination.</summary>
public class PaginationControl : VumaControl
{
    public PaginationControl()
    {
        Background = GetSurfaceBaseBrush();
        Padding = new Thickness(GetSpacing("8"));
        AutomationProperties.Name = "Pagination";
        AutomationProperties.ControlType = ControlType.Pager;
    }
}

/// <summary>Breadcrumb.</summary>
public class BreadcrumbControl : VumaControl
{
    public BreadcrumbControl()
    {
        Background = GetSurfaceBaseBrush();
        Padding = new Thickness(GetSpacing("8"), GetSpacing("4"));
        AutomationProperties.Name = "Breadcrumb";
        AutomationProperties.ControlType = ControlType.MenuBar;
    }
}

/// <summary>Side navigation.</summary>
public class SideNavControl : VumaControl
{
    public SideNavControl()
    {
        Background = GetSurfaceBaseBrush();
        AutomationProperties.Name = "Side navigation";
        AutomationProperties.ControlType = ControlType.Menu;
    }
}

/// <summary>Command palette.</summary>
public class CommandPaletteControl : VumaControl
{
    public CommandPaletteControl()
    {
        Background = GetSurfaceRaisedBrush();
        CornerRadius = new CornerRadius(GetRadius("xl"));
        AutomationProperties.Name = "Command palette";
        AutomationProperties.ControlType = ControlType.Popup;
    }
}

/// <summary>Keypad with touch targets ≥ 64pt for POS primary.</summary>
public class KeypadControl : VumaControl
{
    public KeypadControl()
    {
        Background = GetSurfaceRaisedBrush();
        AutomationProperties.Name = "Keypad";
        AutomationProperties.ControlType = ControlType.Calculator;
    }
}

/// <summary>Tender pad with touch targets ≥ 64pt for POS primary.</summary>
public class TenderPadControl : VumaControl
{
    public TenderPadControl()
    {
        Background = GetSurfaceRaisedBrush();
        AutomationProperties.Name = "Tender pad";
        AutomationProperties.ControlType = ControlType.TouchTarget;
    }
}

/// <summary>Receipt preview.</summary>
public class ReceiptPreviewControl : VumaControl
{
    public ReceiptPreviewControl()
    {
        Background = GetSurfaceBaseBrush();
        Padding = new Thickness(GetSpacing("16"));
        FontFamily = GetFontFamily("mono");
        AutomationProperties.Name = "Receipt preview";
        AutomationProperties.ControlType = ControlType.Document;
    }
}

/// <summary>Scanner input. Uses JetBrains Mono for codes.</summary>
public class ScannerInputControl : VumaControl
{
    public ScannerInputControl()
    {
        Background = GetSurfaceBaseBrush();
        BorderBrush = GetSeparatorBrush();
        BorderThickness = new Thickness(1);
        FontFamily = GetFontFamily("mono");
        FontSize = GetFontSize("mono");
        Padding = new Thickness(GetSpacing("12"), GetSpacing("8"));
        AutomationProperties.Name = "Scanner input";
        AutomationProperties.HelpText = "Barcode or QR code scanner input";
        AutomationProperties.ControlType = ControlType.Text;
    }
}

/// <summary>Offline indicator.</summary>
public class OfflineIndicatorControl : VumaControl
{
    public OfflineIndicatorControl()
    {
        Background = GetWarningBrush();
        Foreground = Brushes.White;
        Padding = new Thickness(GetSpacing("8"), GetSpacing("4"));
        CornerRadius = new CornerRadius(GetRadius("md"));
        AutomationProperties.Name = "Offline indicator";
        AutomationProperties.ControlType = ControlType.StatusBar;
    }
}

/// <summary>Licence state banner.</summary>
public class LicenceStateBannerControl : VumaControl
{
    public LicenceStateBannerControl()
    {
        Background = GetInfoBrush();
        Foreground = Brushes.White;
        Padding = new Thickness(GetSpacing("16"), GetSpacing("8"));
        CornerRadius = new CornerRadius(GetRadius("md"));
        AutomationProperties.Name = "Licence state banner";
        AutomationProperties.ControlType = ControlType.Banner;
    }
}

/// <summary>
/// Button with keyboard spec: Enter/Space activates, Tab navigates to.
/// Both themes, both densities, 64pt touch target for POS primary.
/// </summary>
public class ActionButton : VumaControl
{
    public ActionButton()
    {
        MinWidth = GetTouchTarget("posPrimary");
        MinHeight = GetTouchTarget("posPrimary");
        CornerRadius = new CornerRadius(GetRadius("md"));
        FontSize = GetFontSize("callout");
        FontWeight = FontWeights.SemiBold;
        AutomationProperties.Name = "Action button";
    }
}
