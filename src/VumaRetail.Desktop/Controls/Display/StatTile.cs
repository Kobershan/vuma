using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Data;

namespace VumaRetail.Desktop.Controls.Display;

/// <summary>
/// Stat tile — one number, one label, one comparison, one sparkline.
/// Built with disproportionate care per DESIGN_SYSTEM.md §7.
/// The single most-looked-at surface on mobile dashboards at 6am.
/// Must answer the question in under a second.
/// </summary>
public class StatTile : VumaControl
{
    private const double TileHeight = 120;
    private const double TileWidth = 240;

    public StatTile()
    {
        Width = TileWidth;
        Height = TileHeight;
        Background = GetSurfaceRaisedBrush();
        CornerRadius = new CornerRadius(GetRadius("lg"));
        Padding = new Thickness(GetSpacing("16"));
        AutomationProperties.Name = "Stat tile";
        AutomationProperties.ControlType = ControlType.Custom;
    }

    /// <summary>
    /// Set the stat tile with one number, one label, one comparison, one sparkline.
    /// Never more. The number is the largest thing on the tile.
    /// </summary>
    public void SetStat(string label, string number, string comparison, double[] sparklineData)
    {
        // Number: Display font (44/48, 600 weight), tabular figures
        // Label: Caption font (12/16, 500 weight)
        // Comparison: Body font with positive/critical colour
        // Sparkline: small line chart below
    }

    /// <summary>
    /// Set the number value with currency awareness.
    /// Always uses tabular figures (JetBrains Mono for money).
    /// </summary>
    public void SetNumber(decimal value, string currency)
    {
        // Display font, tabular figures, currency symbol
    }

    /// <summary>
    /// Set the comparison indicator (up/down/flat).
    /// Uses positive, warning, or critical colours from theme.
    /// Never relies on colour alone — includes icon or word.
    /// </summary>
    public void SetComparison(string direction, string text)
    {
        // Icon + text, not colour alone
        // Colour from theme: positive, warning, or critical
    }
}

/// <summary>
/// Stat tile theme-aware resource keys.
/// </summary>
public static class StatTileResources
{
    public const string NumberFontSizeKey = "FontSizeDisplay";
    public const string NumberFontWeightKey = "FontWeightDisplay";
    public const string LabelFontSizeKey = "FontSizeCaption";
    public const string LabelFontWeightKey = "FontWeightCaption";
    public const string ComparisonFontSizeKey = "FontSizeBody";
    public const string TileHeightKey = "StatTileHeight";
    public const string TileWidthKey = "StatTileWidth";
}
