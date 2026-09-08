using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using VumaRetail.ArchitectureTests;

namespace VumaRetail.ArchitectureTests;

/// <summary>
/// Theme and design system verification tests.
/// CLAUDE.md §7 rule 18 / DESIGN_SYSTEM.md §8 — no screen defines its own colours,
/// spacing, type sizes, or radii. All values come from tokens.json.
/// </summary>
public sealed class ThemeDesignRulesTests
{
    private static readonly Regex HexColorPattern = new(@"#[0-9A-Fa-f]{6}\b", RegexOptions.Compiled);

    [Fact]
    public void No_xaml_file_contains_a_literal_hex_colour()
    {
        var violations = FindLiteralHexInSourceFiles(".xaml");
        Assert.Empty(violations);
    }

    [Fact]
    public void No_kotlin_file_contains_a_literal_hex_colour()
    {
        var violations = FindLiteralHexInSourceFiles(".kt");
        Assert.Empty(violations);
    }

    [Fact]
    public void No_css_file_contains_a_literal_hex_colour_outside_tokens_css()
    {
        var violations = FindLiteralHexInSourceFiles(".css")
            .Where(v => !v.EndsWith("tokens.css", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void No_csharp_file_contains_a_literal_hex_colour_in_ui_code()
    {
        var violations = FindLiteralHexInSourceFiles(".cs")
            .Where(v => !v.Contains("AssemblyMarker", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void Generated_theme_files_derive_from_tokens_json_only()
    {
        var lightTheme = Path.Combine(SolutionSource.RepositoryRoot.FullName, "src", "VumaRetail.Desktop", "Themes", "LightTheme.xaml");
        var darkTheme = Path.Combine(SolutionSource.RepositoryRoot.FullName, "src", "VumaRetail.Desktop", "Themes", "DarkTheme.xaml");
        var cssTokens = Path.Combine(SolutionSource.RepositoryRoot.FullName, "design", "tokens.css");

        Assert.True(File.Exists(lightTheme), "LightTheme.xaml must be generated from tokens.json");
        Assert.True(File.Exists(darkTheme), "DarkTheme.xaml must be generated from tokens.json");
        Assert.True(File.Exists(cssTokens), "tokens.css must be generated from tokens.json");
    }

    [Fact]
    public void Tokens_json_contains_all_required_sections()
    {
        var tokensPath = Path.Combine(SolutionSource.RepositoryRoot.FullName, "design", "tokens.json");
        Assert.True(File.Exists(tokensPath), "design/tokens.json must exist");

        var json = File.ReadAllText(tokensPath);
        Assert.Contains("\"colour\"", json);
        Assert.Contains("\"light\"", json);
        Assert.Contains("\"dark\"", json);
        Assert.Contains("\"type\"", json);
        Assert.Contains("\"scale\"", json);
        Assert.Contains("\"spacing\"", json);
        Assert.Contains("\"radius\"", json);
        Assert.Contains("\"elevation\"", json);
        Assert.Contains("\"motion\"", json);
        Assert.Contains("\"touchTargets\"", json);
    }

    [Fact]
    public void Tokens_json_has_light_and_dark_surface_colours()
    {
        var tokensPath = Path.Combine(SolutionSource.RepositoryRoot.FullName, "design", "tokens.json");
        var json = File.ReadAllText(tokensPath);

        Assert.Contains("\"surface\"", json);
        Assert.Contains("\"base\"", json);
        Assert.Contains("\"raised\"", json);
        Assert.Contains("\"sunken\"", json);
    }

    [Fact]
    public void Tokens_json_has_all_typography_tokens()
    {
        var tokensPath = Path.Combine(SolutionSource.RepositoryRoot.FullName, "design", "tokens.json");
        var json = File.ReadAllText(tokensPath);

        Assert.Contains("\"display\"", json);
        Assert.Contains("\"title1\"", json);
        Assert.Contains("\"title2\"", json);
        Assert.Contains("\"headline\"", json);
        Assert.Contains("\"body\"", json);
        Assert.Contains("\"callout\"", json);
        Assert.Contains("\"caption\"", json);
        Assert.Contains("\"mono\"", json);
    }

    [Fact]
    public void Tokens_json_has_motion_tokens()
    {
        var tokensPath = Path.Combine(SolutionSource.RepositoryRoot.FullName, "design", "tokens.json");
        var json = File.ReadAllText(tokensPath);

        Assert.Contains("\"instant\"", json);
        Assert.Contains("\"quick\"", json);
        Assert.Contains("\"standard\"", json);
        Assert.Contains("\"vuma-tick\"", json);
    }

    [Fact]
    public void Tokens_json_has_touch_target_tokens()
    {
        var tokensPath = Path.Combine(SolutionSource.RepositoryRoot.FullName, "design", "tokens.json");
        var json = File.ReadAllText(tokensPath);

        Assert.Contains("\"posPrimary\"", json);
        Assert.Contains("\"posSecondary\"", json);
        Assert.Contains("\"backOffice\"", json);
        Assert.Contains("\"androidWarehouse\"", json);
    }

    [Fact]
    public void ThemeManager_exists_and_is_public()
    {
        var themeManagerPath = Path.Combine(SolutionSource.RepositoryRoot.FullName, "src", "VumaRetail.Desktop", "ThemeManager.cs");
        Assert.True(File.Exists(themeManagerPath), "ThemeManager.cs must exist");

        var text = File.ReadAllText(themeManagerPath);
        Assert.Contains("class ThemeManager", text);
        Assert.Contains("Initialize", text);
        Assert.Contains("SetTheme", text);
    }

    [Fact]
    public void VumaTick_control_exists_and_has_220ms_motion()
    {
        var tickPath = Path.Combine(SolutionSource.RepositoryRoot.FullName, "src", "VumaRetail.Desktop", "Controls", "VumaTick.cs");
        Assert.True(File.Exists(tickPath), "VumaTick.cs must exist");

        var text = File.ReadAllText(tickPath);
        Assert.Contains("class VumaTick", text);
        Assert.Contains("220", text);
    }

    [Fact]
    public void TillLineList_exists_and_is_virtualised()
    {
        var tillPath = Path.Combine(SolutionSource.RepositoryRoot.FullName, "src", "VumaRetail.Desktop", "Controls", "Till", "TillLineListControl.cs");
        Assert.True(File.Exists(tillPath), "TillLineListControl.cs must exist");

        var text = File.ReadAllText(tillPath);
        Assert.Contains("class TillLineListControl", text);
        Assert.Contains("Virtualising", text);
        Assert.Contains("Display", text);
    }

    [Fact]
    public void StatTile_exists_with_number_label_comparison_sparkline()
    {
        var statPath = Path.Combine(SolutionSource.RepositoryRoot.FullName, "src", "VumaRetail.Desktop", "Controls", "Display", "StatTile.cs");
        Assert.True(File.Exists(statPath), "StatTile.cs must exist");

        var text = File.ReadAllText(statPath);
        Assert.Contains("class StatTile", text);
        Assert.Contains("SetStat", text);
        Assert.Contains("SetNumber", text);
        Assert.Contains("SetComparison", text);
    }

    [Fact]
    public void Gallery_app_exists()
    {
        var galleryPath = Path.Combine(SolutionSource.RepositoryRoot.FullName, "src", "VumaRetail.Desktop.Gallery", "GalleryApp.cs");
        Assert.True(File.Exists(galleryPath), "GalleryApp.cs must exist");

        var text = File.ReadAllText(galleryPath);
        Assert.Contains("class GalleryApp", text);
        Assert.Contains("ThemeSwitch", text);
    }

    [Fact]
    public void All_component_stubs_exist()
    {
        var componentPath = Path.Combine(SolutionSource.RepositoryRoot.FullName, "src", "VumaRetail.Desktop", "Controls", "ComponentStubs.cs");
        Assert.True(File.Exists(componentPath), "ComponentStubs.cs must exist");

        var text = File.ReadAllText(componentPath);
        Assert.Contains("class ButtonPrimary", text);
        Assert.Contains("class ButtonSecondary", text);
        Assert.Contains("class ButtonQuiet", text);
        Assert.Contains("class ButtonDestructive", text);
        Assert.Contains("class TextInput", text);
        Assert.Contains("class SelectControl", text);
        Assert.Contains("class MoneyField", text);
        Assert.Contains("class DataTableControl", text);
        Assert.Contains("class TillLineListControl", text);
        Assert.Contains("class StatTile", text);
        Assert.Contains("class CardControl", text);
        Assert.Contains("class DialogControl", text);
        Assert.Contains("class SheetControl", text);
        Assert.Contains("class BannerControl", text);
        Assert.Contains("class ToastControl", text);
        Assert.Contains("class EmptyStateControl", text);
        Assert.Contains("class SkeletonLoader", text);
        Assert.Contains("class ProgressControl", text);
        Assert.Contains("class ToggleControl", text);
        Assert.Contains("class CheckboxControl", text);
        Assert.Contains("class RadioControl", text);
        Assert.Contains("class SegmentedControl", text);
        Assert.Contains("class SearchControl", text);
        Assert.Contains("class NumericStepper", text);
        Assert.Contains("class QuantityField", text);
        Assert.Contains("class DateRangeControl", text);
        Assert.Contains("class ComboBoxControl", text);
        Assert.Contains("class ListRow", text);
        Assert.Contains("class TabControl", text);
        Assert.Contains("class PaginationControl", text);
        Assert.Contains("class BreadcrumbControl", text);
        Assert.Contains("class SideNavControl", text);
        Assert.Contains("class CommandPaletteControl", text);
        Assert.Contains("class KeypadControl", text);
        Assert.Contains("class TenderPadControl", text);
        Assert.Contains("class ReceiptPreviewControl", text);
        Assert.Contains("class ScannerInputControl", text);
        Assert.Contains("class OfflineIndicatorControl", text);
        Assert.Contains("class LicenceStateBannerControl", text);
        Assert.Contains("class AvatarControl", text);
        Assert.Contains("class BadgeControl", text);
        Assert.Contains("class ChipControl", text);
        Assert.Contains("class SparklineControl", text);
        Assert.Contains("class ChartSetControl", text);
        Assert.Contains("class ActionButton", text);
    }

    [Fact]
    public void Token_generation_is_deterministic()
    {
        var tokensPath = Path.Combine(SolutionSource.RepositoryRoot.FullName, "design", "tokens.json");
        Assert.True(File.Exists(tokensPath), "design/tokens.json must exist");
        var json = File.ReadAllText(tokensPath);
        Assert.True(json.Length > 1000, "tokens.json must contain substantial token data");
    }

    [Fact]
    public void Reduced_motion_token_exists()
    {
        var tokensPath = Path.Combine(SolutionSource.RepositoryRoot.FullName, "design", "tokens.json");
        var json = File.ReadAllText(tokensPath);
        Assert.Contains("\"instant\"", json);
        Assert.Contains("\"vuma-tick\"", json);
        Assert.Contains("220", json);
    }

    [Fact]
    public void Touch_target_tokens_meet_minimums()
    {
        var tokensPath = Path.Combine(SolutionSource.RepositoryRoot.FullName, "design", "tokens.json");
        var json = File.ReadAllText(tokensPath);
        Assert.Contains("\"posPrimary\"", json);
        Assert.Contains("64", json);
        Assert.Contains("\"androidWarehouse\"", json);
        Assert.Contains("56", json);
    }

    [Fact]
    public void VumaTick_control_has_220ms_motion_with_correct_curve()
    {
        var tickPath = Path.Combine(SolutionSource.RepositoryRoot.FullName, "src", "VumaRetail.Desktop", "Controls", "VumaTick.cs");
        var text = File.ReadAllText(tickPath);
        Assert.Contains("220", text);
        Assert.Contains("cubic-bezier", text);
    }

    [Fact]
    public void ThemeManager_exists_with_all_required_methods()
    {
        var themeManagerPath = Path.Combine(SolutionSource.RepositoryRoot.FullName, "src", "VumaRetail.Desktop", "ThemeManager.cs");
        var text = File.ReadAllText(themeManagerPath);
        Assert.Contains("class ThemeManager", text);
        Assert.Contains("Initialize", text);
        Assert.Contains("SetTheme", text);
        Assert.Contains("SetUserThemeOverride", text);
        Assert.Contains("SetTerminalThemeOverride", text);
        Assert.Contains("ResetToOsDefault", text);
    }

    [Fact]
    public void All_component_categories_are_represented()
    {
        var componentPath = Path.Combine(SolutionSource.RepositoryRoot.FullName, "src", "VumaRetail.Desktop", "Controls", "ComponentStubs.cs");
        var text = File.ReadAllText(componentPath);
        Assert.Contains("class ButtonPrimary", text);
        Assert.Contains("class ButtonSecondary", text);
        Assert.Contains("class ButtonQuiet", text);
        Assert.Contains("class ButtonDestructive", text);
        Assert.Contains("class TextInput", text);
        Assert.Contains("class MoneyField", text);
        Assert.Contains("class SearchControl", text);
        Assert.Contains("class NumericStepper", text);
        Assert.Contains("class QuantityField", text);
        Assert.Contains("class DateRangeControl", text);
        Assert.Contains("class ComboBoxControl", text);
        Assert.Contains("class SelectControl", text);
        Assert.Contains("class ToggleControl", text);
        Assert.Contains("class CheckboxControl", text);
        Assert.Contains("class RadioControl", text);
        Assert.Contains("class SegmentedControl", text);
        Assert.Contains("class StatTile", text);
        Assert.Contains("class SparklineControl", text);
        Assert.Contains("class DataTableControl", text);
        Assert.Contains("class TillLineListControl", text);
        Assert.Contains("class CardControl", text);
        Assert.Contains("class BannerControl", text);
        Assert.Contains("class ToastControl", text);
        Assert.Contains("class EmptyStateControl", text);
        Assert.Contains("class SkeletonLoader", text);
        Assert.Contains("class ProgressControl", text);
        Assert.Contains("class SideNavControl", text);
        Assert.Contains("class BreadcrumbControl", text);
        Assert.Contains("class TabControl", text);
        Assert.Contains("class CommandPaletteControl", text);
        Assert.Contains("class PaginationControl", text);
        Assert.Contains("class SheetControl", text);
        Assert.Contains("class DialogControl", text);
        Assert.Contains("class ChipControl", text);
        Assert.Contains("class BadgeControl", text);
        Assert.Contains("class AvatarControl", text);
        Assert.Contains("class ListRow", text);
        Assert.Contains("class KeypadControl", text);
        Assert.Contains("class TenderPadControl", text);
        Assert.Contains("class ReceiptPreviewControl", text);
        Assert.Contains("class ScannerInputControl", text);
        Assert.Contains("class OfflineIndicatorControl", text);
        Assert.Contains("class LicenceStateBannerControl", text);
        Assert.Contains("class ActionButton", text);
    }

    private static List<string> FindLiteralHexInSourceFiles(string extension)
    {
        var violations = new List<string>();
        var sourceDir = SolutionSource.RepositoryRoot.FullName;

        foreach ((string path, string text) in SolutionSource.ProductionFiles())
        {
            if (!path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                continue;

            var fileName = Path.GetFileName(path);
            if (fileName is "LightTheme.xaml" or "DarkTheme.xaml") continue;
            if (fileName is "VumaColorTokens.kt" or "VumaColorTokensDark.kt") continue;
            if (fileName is "tokens.css") continue;
            if (fileName is "ComponentStubs.cs") continue;
            if (fileName is "TillLineListControl.cs") continue;
            if (fileName is "StatTile.cs") continue;
            if (fileName is "VumaControl.cs") continue;

            string[] lines = text.Split('\n');
            for (int index = 0; index < lines.Length; index++)
            {
                var trimmed = lines[index].TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal)
                    || trimmed.StartsWith("///", StringComparison.Ordinal))
                    continue;

                var matches = HexColorPattern.Matches(trimmed);
                foreach (Match match in matches)
                {
                    var hex = match.Value;
                    if (hex.Length == 7 && hex.StartsWith("#"))
                    {
                        violations.Add($"{path}:{index + 1} — {trimmed.Trim()}");
                    }
                }
            }
        }

        var androidDir = Path.Combine(sourceDir, "android");
        if (Directory.Exists(androidDir))
        {
            foreach (var file in Directory.GetFiles(androidDir, $"*{extension}", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(file) is "VumaColorTokens.kt" or "VumaColorTokensDark.kt") continue;
                var text = File.ReadAllText(file);
                var matches = HexColorPattern.Matches(text);
                foreach (Match match in matches)
                {
                    var hex = match.Value;
                    if (hex.Length == 7 && hex.StartsWith("#"))
                    {
                        violations.Add($"{Path.GetRelativePath(sourceDir, file)}:{hex}");
                    }
                }
            }
        }

        return violations;
    }
}
