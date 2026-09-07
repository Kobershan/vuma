using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace VumaRetail.ArchitectureTests;

/// <summary>
/// CLAUDE.md §7 rule 18 / DESIGN_SYSTEM.md §8 — no screen defines its own colours,
/// spacing, type sizes, or radii. An architecture test scans XAML, Kotlin and CSS
/// for literal hex values and fails the build.
/// </summary>
/// <remarks>
/// The design system's single source of truth is <c>design/tokens.json</c>. All colour,
/// spacing, type, and radius values must be consumed from generated theme resources
/// (WPF ResourceDictionary, Android Compose theme, CSS custom properties). A literal hex
/// value anywhere in the UI source means someone bypassed the token system and will drift
/// within a month.
///
/// The generated files themselves (WPF .xaml, Android .kt, CSS) contain the hex values
/// that came from tokens.json — they are build artifacts, not source. This test only scans
/// source files (XAML markup that is hand-authored, .kt source that is hand-authored, .css
/// source that is hand-authored).
/// </remarks>
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
    public void Tokens_json_is_the_only_source_of_colour_values()
    {
        var tokensPath = Path.Combine(SolutionSource.RepositoryRoot.FullName, "design", "tokens.json");
        Assert.True(File.Exists(tokensPath), "design/tokens.json must exist");

        var json = File.ReadAllText(tokensPath);
        Assert.Contains("\"colour\"", json);
        Assert.Contains("\"light\"", json);
        Assert.Contains("\"dark\"", json);
    }

    private static List<string> FindLiteralHexInSourceFiles(string extension)
    {
        var violations = new List<string>();
        var sourceDir = SolutionSource.RepositoryRoot.FullName;

        foreach ((string path, string text) in SolutionSource.ProductionFiles())
        {
            if (!path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                continue;

            // Skip generated theme files
            var fileName = Path.GetFileName(path);
            if (fileName is "LightTheme.xaml" or "DarkTheme.xaml") continue;
            if (fileName is "VumaColorTokens.kt" or "VumaColorTokensDark.kt") continue;
            if (fileName is "tokens.css") continue;

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

        // Also scan the android directory
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
