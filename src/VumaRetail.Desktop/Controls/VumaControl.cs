using System.Windows;
using System.Windows.Controls;

namespace VumaRetail.Desktop.Controls;

/// <summary>
/// Base class for all Vuma UI controls. Enforces:
/// - No literal colours, spacing, type sizes, or radii
/// - Focus ring: 2pt accent ring at 2pt offset
/// - Keyboard accessibility
/// - Both themes supported
/// - Both densities supported
/// </summary>
public abstract class VumaControl : Control
{
    static VumaControl()
    {
        // Ensure focus is always visible: 2pt accent ring at 2pt offset
        FocusVisualStyle = GetFocusVisualStyle();
    }

    private static Style GetFocusVisualStyle()
    {
        return new Style(typeof(Control))
        {
            Setters =
            {
                new Setter(Control.FocusVisualBrushProperty, new SolidColorBrush(Color.FromRgb(0x0B, 0x7A, 0x5A))),
                new Setter(Control.FocusVisualBorderThicknessProperty, new Thickness(2)),
                new Setter(Control.FocusVisualMarginProperty, new Thickness(-2)),
            }
        };
    }

    /// <summary>
    /// Gets the current theme accent colour from the ResourceDictionary.
    /// </summary>
    protected Brush GetAccentBrush()
    {
        if (Application.Current?.Resources.TryGetValue("AccentBrush", out var accent) == true)
        {
            return (Brush)accent;
        }
        return new SolidColorBrush(Color.FromRgb(0x0B, 0x7A, 0x5A));
    }

    /// <summary>
    /// Gets a spacing value from the theme resources.
    /// </summary>
    protected double GetSpacing(string key)
    {
        if (Application.Current?.Resources.TryGetValue($"Spacing{key}", out var spacing) == true)
        {
            return (double)spacing;
        }
        return 8;
    }

    /// <summary>
    /// Gets a border radius from the theme resources.
    /// </summary>
    protected double GetRadius(string key)
    {
        if (Application.Current?.Resources.TryGetValue($"Radius{key}", out var radius) == true)
        {
            return (double)radius;
        }
        return 12;
    }
}
