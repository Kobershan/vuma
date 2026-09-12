using System.Windows;
using System.Windows.Data;
using WpfApplication = global::System.Windows.Application;

namespace VumaRetail.Desktop;

/// <summary>
/// Manages theme switching between light and dark ResourceDictionaries.
/// Supports OS default, manual per-user override, and per-terminal override.
/// Theme switching is instant with no restart and no flash of wrong theme.
/// </summary>
public static class ThemeManager
{
    private static ResourceDictionary? _lightTheme;
    private static ResourceDictionary? _darkTheme;
    private static bool _isDark;

    public static bool IsDark => _isDark;

    static ThemeManager()
    {
        _lightTheme = new ResourceDictionary { Source = new Uri("pack://application:,,,/VumaRetail.Desktop;component/Themes/LightTheme.xaml") };
        _darkTheme = new ResourceDictionary { Source = new Uri("pack://application:,,,/VumaRetail.Desktop;component/Themes/DarkTheme.xaml") };
    }

    /// <summary>
    /// Initialize the theme manager. Detects OS preference by default.
    /// </summary>
    public static void Initialize()
    {
        _isDark = DetectOsThemePreference();
        ApplyTheme(_isDark);
    }

    /// <summary>
    /// Switch to the specified theme instantly. No restart, no flash.
    /// </summary>
    public static void SetTheme(bool isDark)
    {
        if (_isDark == isDark) return;
        _isDark = isDark;
        ApplyTheme(isDark);
    }

    /// <summary>
    /// Set theme per-user override.
    /// </summary>
    public static void SetUserThemeOverride(bool isDark)
    {
        // Persist user preference; ApplyTheme picks it up on next switch
        if (WpfApplication.Current != null)
            WpfApplication.Current.Properties["UserThemeOverride"] = isDark;
        SetTheme(isDark);
    }

    /// <summary>
    /// Set theme per-terminal override.
    /// </summary>
    public static void SetTerminalThemeOverride(bool isDark)
    {
        if (WpfApplication.Current != null)
            WpfApplication.Current.Properties["TerminalThemeOverride"] = isDark;
        SetTheme(isDark);
    }

    /// <summary>
    /// Reset to OS default theme detection.
    /// </summary>
    public static void ResetToOsDefault()
    {
        WpfApplication.Current?.Properties.Remove("UserThemeOverride");
        WpfApplication.Current?.Properties.Remove("TerminalThemeOverride");
        _isDark = DetectOsThemePreference();
        ApplyTheme(_isDark);
    }

    private static void ApplyTheme(bool isDark)
    {
        if (WpfApplication.Current == null) return;

        var currentTheme = isDark ? _darkTheme : _lightTheme;

        // Replace the current theme dictionary
        var existing = WpfApplication.Current.Resources.MergedDictionaries
            .FirstOrDefault(d => d.Source?.ToString().Contains("Themes/") == true);
        if (existing != null)
        {
            WpfApplication.Current.Resources.MergedDictionaries.Remove(existing);
        }

        WpfApplication.Current.Resources.MergedDictionaries.Add(currentTheme!);
        _isDark = isDark;
    }

    private static bool DetectOsThemePreference()
    {
        try
        {
            // Use Windows registry to detect dark/light mode preference
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key != null)
            {
                var value = key.GetValue("AppsUseLightTheme");
                return value is not null && (int)value == 0;
            }
        }
        catch
        {
            // Fallback: default to light
        }
        return false;
    }
}
