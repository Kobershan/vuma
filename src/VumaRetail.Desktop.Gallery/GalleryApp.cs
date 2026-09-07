using System.Windows;
using System.Windows.Controls;

namespace VumaRetail.Desktop.Gallery;

/// <summary>
/// Component gallery — shows every component in every state, theme, and density.
/// It is how a developer checks their work and how the system is demoed.
/// </summary>
public partial class GalleryApp : Window
{
    public GalleryApp()
    {
        InitializeComponent();
        Title = "Vuma Retail — Design System Gallery";
        Width = 1200;
        Height = 800;
    }

    /// <summary>
    /// Switch between light and dark themes.
    /// </summary>
    private void OnThemeSwitch(object sender, RoutedEventArgs e)
    {
        var isDark = !ThemeManager.IsDark;
        ThemeManager.SetTheme(isDark);
    }
}
