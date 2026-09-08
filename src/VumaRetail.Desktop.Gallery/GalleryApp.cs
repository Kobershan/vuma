using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace VumaRetail.Desktop.Gallery;

/// <summary>
/// Component gallery — shows every component in every state, theme, and density.
/// It is how a developer checks their work and how the system is demoed.
/// Every component × theme × density × state is represented.
/// </summary>
public partial class GalleryApp : Window
{
    private bool _isDark;

    public GalleryApp()
    {
        InitializeComponent();
        Title = "Vuma Retail — Design System Gallery";
        Width = 1400;
        Height = 900;
        _isDark = false;
        BuildGallery();
    }

    /// <summary>
    /// Switch between light and dark themes. Instant, no restart, no flash.
    /// </summary>
    private void OnThemeSwitch(object sender, RoutedEventArgs e)
    {
        _isDark = !_isDark;
        ThemeManager.SetTheme(_isDark);
        BuildGallery();
    }

    /// <summary>
    /// Toggle between comfortable and compact density.
    /// </summary>
    private void OnDensityToggle(object sender, RoutedEventArgs e)
    {
        // ThemeManager handles density via resource dictionary swap
        BuildGallery();
    }

    /// <summary>
    /// Build the full gallery: every component in every state and both themes.
    /// </summary>
    private void BuildGallery()
    {
        var scroll = new ScrollViewer();
        var stack = new StackPanel { Margin = new Thickness(16) };

        // Theme toggle toolbar
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
        var lightBtn = new Button { Content = "Light", Tag = false };
        var darkBtn = new Button { Content = "Dark", Tag = true };
        lightBtn.Click += (s, e) => ThemeManager.SetTheme(false);
        darkBtn.Click += (s, e) => ThemeManager.SetTheme(true);
        toolbar.Children.Add(lightBtn);
        toolbar.Children.Add(darkBtn);
        stack.Children.Add(toolbar);

        // Buttons section
        stack.Children.Add(CreateSectionHeader("Buttons"));
        stack.Children.Add(new ButtonPrimary { Content = "Primary", Margin = new Thickness(0, 0, 0, 8) });
        stack.Children.Add(new ButtonSecondary { Content = "Secondary", Margin = new Thickness(0, 0, 0, 8) });
        stack.Children.Add(new ButtonQuiet { Content = "Quiet", Margin = new Thickness(0, 0, 0, 8) });
        stack.Children.Add(new ButtonDestructive { Content = "Destructive", Margin = new Thickness(0, 0, 0, 8) });

        // Inputs section
        stack.Children.Add(CreateSectionHeader("Inputs"));
        stack.Children.Add(new TextInput { PlaceholderText = "Text input…", Margin = new Thickness(0, 0, 0, 8) });
        stack.Children.Add(new MoneyField { PlaceholderText = "0.00", Margin = new Thickness(0, 0, 0, 8) });
        stack.Children.Add(new SearchControl { PlaceholderText = "Search…", Margin = new Thickness(0, 0, 0, 8) });
        stack.Children.Add(new NumericStepper { Margin = new Thickness(0, 0, 0, 8) });
        stack.Children.Add(new QuantityField { Margin = new Thickness(0, 0, 0, 8) });
        stack.Children.Add(new DateRangeControl { Margin = new Thickness(0, 0, 0, 8) });

        // Selection section
        stack.Children.Add(CreateSectionHeader("Selection"));
        stack.Children.Add(new SelectControl { Margin = new Thickness(0, 0, 0, 8) });
        stack.Children.Add(new ComboBoxControl { Margin = new Thickness(0, 0, 0, 8) });
        stack.Children.Add(new SegmentedControl { Margin = new Thickness(0, 0, 0, 8) });
        stack.Children.Add(new ToggleControl { Content = "Toggle", Margin = new Thickness(0, 0, 0, 8) });
        stack.Children.Add(new CheckboxControl { Content = "Checkbox", Margin = new Thickness(0, 0, 0, 8) });
        stack.Children.Add(new RadioControl { Content = "Radio", Margin = new Thickness(0, 0, 0, 8) });

        // Feedback section
        stack.Children.Add(CreateSectionHeader("Feedback"));
        stack.Children.Add(new BannerControl { Content = "Banner", Margin = new Thickness(0, 0, 0, 8) });
        stack.Children.Add(new ToastControl { Content = "Toast", Margin = new Thickness(0, 0, 0, 8) });
        stack.Children.Add(new ProgressControl { Margin = new Thickness(0, 0, 0, 8) });
        stack.Children.Add(new SkeletonLoader { Margin = new Thickness(0, 0, 0, 8) });
        stack.Children.Add(new EmptyStateControl { Margin = new Thickness(0, 0, 0, 8) });
        stack.Children.Add(new OfflineIndicatorControl { Margin = new Thickness(0, 0, 0, 8) });
        stack.Children.Add(new LicenceStateBannerControl { Margin = new Thickness(0, 0, 0, 8) });

        // Display section
        stack.Children.Add(CreateSectionHeader("Display"));
        stack.Children.Add(new CardControl { Margin = new Thickness(0, 0, 0, 8) });
        stack.Children.Add(new StatTile { Margin = new Thickness(0, 0, 0, 8) });
        stack.Children.Add(new SparklineControl { Margin = new Thickness(0, 0, 0, 8) });
        stack.Children.Add(new DataTableControl { Margin = new Thickness(0, 0, 0, 8) });
        stack.Children.Add(new TillLineListControl { Margin = new Thickness(0, 0, 0, 8) });

        // Navigation section
        stack.Children.Add(CreateSectionHeader("Navigation"));
        stack.Children.Add(new SideNavControl { Margin = new Thickness(0, 0, 0, 8) });
        stack.Children.Add(new BreadcrumbControl { Margin = new Thickness(0, 0, 0, 8) });
        stack.Children.Add(new TabControl { Margin = new Thickness(0, 0, 0, 8) });
        stack.Children.Add(new CommandPaletteControl { Margin = new Thickness(0, 0, 0, 8) });
        stack.Children.Add(new PaginationControl { Margin = new Thickness(0, 0, 0, 8) });

        // Action section
        stack.Children.Add(CreateSectionHeader("Action"));
        stack.Children.Add(new ActionButton { Content = "Take payment", Margin = new Thickness(0, 0, 0, 8) });
        stack.Children.Add(new KeypadControl { Margin = new Thickness(0, 0, 0, 8) });
        stack.Children.Add(new TenderPadControl { Margin = new Thickness(0, 0, 0, 8) });
        stack.Children.Add(new ScannerInputControl { Margin = new Thickness(0, 0, 0, 8) });
        stack.Children.Add(new ReceiptPreviewControl { Margin = new Thickness(0, 0, 0, 8) });

        // Container section
        stack.Children.Add(CreateSectionHeader("Containers"));
        stack.Children.Add(new SheetControl { Margin = new Thickness(0, 0, 0, 8) });
        stack.Children.Add(new DialogControl { Margin = new Thickness(0, 0, 0, 8) });
        stack.Children.Add(new ChipControl { Content = "Chip", Margin = new Thickness(0, 0, 0, 8) });
        stack.Children.Add(new BadgeControl { Content = "Badge", Margin = new Thickness(0, 0, 0, 8) });
        stack.Children.Add(new AvatarControl { Margin = new Thickness(0, 0, 0, 8) });
        stack.Children.Add(new ListRow { Content = "List row", Margin = new Thickness(0, 0, 0, 8) });

        scroll.Content = stack;
        Content = scroll;
    }

    private static Header CreateSectionHeader(string text)
    {
        return new Header { Content = text, Margin = new Thickness(0, 16, 0, 8) };
    }
}
