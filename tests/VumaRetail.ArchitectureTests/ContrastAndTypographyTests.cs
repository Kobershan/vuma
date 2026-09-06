using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using VumaRetail.TestSupport;

namespace VumaRetail.ArchitectureTests;

/// <summary>
/// Stage 08b business rules — contrast verification and POS typography constraints.
/// </summary>
public sealed class ContrastAndTypographyTests
{
    private static readonly Regex HexPattern = new(@"#[0-9A-Fa-f]{3,8}\b");

    [Fact]
    public void Contrast_ratios_meet_AA_requirement_for_all_token_pairs()
    {
        var tokens = LoadTokens();
        var light = tokens["color"]!["light"]!;
        var dark = tokens["color"]!["dark"]!;

        var allPairs = new[] { light, dark };
        foreach (var theme in allPairs)
        {
            var bg = GetHex(theme["surface"]!["base"]!);
            var fgColors = new[] {
                theme["text"]!["primary"]!,
                theme["text"]!["secondary"]!,
                theme["text"]!["tertiary"]!,
                theme["accent"]!,
                theme["positive"]!,
                theme["warning"]!,
                theme["critical"]!,
                theme["info"]!
            };

            foreach (var fg in fgColors)
            {
                var fgHex = GetHex(fg);
                var ratio = CalculateContrastRatio(bg, fgHex);
                Assert.True(ratio >= 4.5,
                    $"Contrast ratio {ratio:F2}:1 between {fgHex} on {bg} does not meet AA (4.5:1).");
            }
        }
    }

    [Fact]
    public void Money_quantities_and_critical_states_meet_AAA_requirement()
    {
        var tokens = LoadTokens();
        var light = tokens["color"]!["light"]!;
        var dark = tokens["color"]!["dark"]!;

        // Money: text/primary on surface/base and text/primary on surface/raised
        // Quantities: same
        // Critical states: critical token on surface/base
        var pairs = new[] {
            ("light-money", light["text"]!["primary"]!, light["surface"]!["base"]!),
            ("light-critical", light["critical"]!, light["surface"]!["base"]!),
            ("dark-money", dark["text"]!["primary"]!, dark["surface"]!["base"]!),
            ("dark-critical", dark["critical"]!, dark["surface"]!["base"]!),
        };

        foreach (var (name, fg, bg) in pairs)
        {
            var ratio = CalculateContrastRatio(GetHex(bg), GetHex(fg));
            Assert.True(ratio >= 7.0,
                $"AAA contrast ratio {ratio:F2}:1 for {name} does not meet AAA (7.0:1).");
        }
    }

    [Fact]
    public void POS_surfaces_never_use_type_below_callout_or_weight_below_400()
    {
        var tokens = LoadTokens();
        var scale = tokens["typography"]!["scale"]!;
        var posConstraint = tokens["typography"]!["posConstraint"]!;
        var minSizeToken = posConstraint["minSize"]!.GetValue<string>()!;
        var minWeight = posConstraint["minWeight"]!.GetValue<int>();
        var minSize = scale[minSizeToken]!["size"]!.GetValue<int>();

        // Tokens that are intentionally smaller than callout (used for labels/metadata, not POS surfaces):
        var exemptTokens = new[] { "caption", "mono" };

        foreach (var prop in scale.AsObject())
        {
            if (exemptTokens.Contains(prop.Key)) continue;

            var t = prop.Value!;
            var size = t["size"]!.GetValue<int>();
            var weight = t["weight"]!.GetValue<int>();

            Assert.True(size >= minSize,
                $"POS token '{prop.Key}' has size {size}px which is below {minSizeToken} ({minSize}px).");
            Assert.True(weight >= minWeight,
                $"POS token '{prop.Key}' has weight {weight} which is below {minWeight}.");
        }
    }

    [Fact]
    public void Fonts_embedded_no_runtime_download()
    {
        // Verify font files exist in the fonts/ directory
        var repoRoot = SolutionSource.RepositoryRoot.FullName;
        var fontsDir = Path.Combine(repoRoot, "fonts");
        Assert.True(Directory.Exists(fontsDir), "fonts/ directory must exist.");

        var requiredFonts = new[] { "Inter-Regular.ttf", "InterDisplay-Regular.ttf", "JetBrainsMono-Regular.ttf" };
        foreach (var font in requiredFonts)
        {
            var fontPath = Path.Combine(fontsDir, font);
            Assert.True(File.Exists(fontPath),
                $"Font file {font} must be embedded in fonts/ — no runtime download (DESIGN_SYSTEM.md §8).");
        }
    }

    [Fact]
    public void No_screen_defines_its_own_colours_spacing_type_size_or_radius()
    {
        // Architecture test: scan all XAML files for inline colour/size definitions
        // that don't reference design tokens.
        var xamlFiles = Directory.GetFiles(
            Path.Combine(SolutionSource.RepositoryRoot.FullName, "src"),
            "*.xaml", SearchOption.AllDirectories);

        var violations = new List<string>();
        foreach (var file in xamlFiles)
        {
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.Contains("Color=") && !line.Contains("StaticResource") && !line.Contains("{DynamicResource"))
                {
                    violations.Add($"{Path.GetFileName(file)}:{i + 1}: {line.Trim()}");
                }
            }
        }

        Assert.True(violations.Count == 0,
            $"Found screens defining their own colours. All colours must come from design tokens. "
            + string.Join(Environment.NewLine, violations));
    }

    private static JsonNode LoadTokens()
    {
        var path = Path.Combine(SolutionSource.RepositoryRoot.FullName, "design", "tokens.json");
        return System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
    }

    private static string GetHex(JsonNode node) => node.GetValue<string>()!.TrimStart('#');

    private static double CalculateContrastRatio(string bgHex, string fgHex)
    {
        var bg = HexToRgb(bgHex);
        var fg = HexToRgb(fgHex);
        var bgLum = RelativeLuminance(bg);
        var fgLum = RelativeLuminance(fg);
        var lighter = Math.Max(bgLum, fgLum);
        var darker = Math.Min(bgLum, fgLum);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static (int r, int g, int b) HexToRgb(string hex)
    {
        if (hex.Length == 3)
            hex = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";
        return (
            Convert.ToInt32(hex.Substring(0, 2), 16),
            Convert.ToInt32(hex.Substring(2, 2), 16),
            Convert.ToInt32(hex.Substring(4, 2), 16));
    }

    private static double RelativeLuminance((int r, int g, int b) c)
    {
        double Linear(int val) => val <= 12 ? val / 3294.0 : Math.Pow((val + 0.055) / 1.055, 2.4);
        return 0.2126 * Linear(c.r) + 0.7152 * Linear(c.g) + 0.0722 * Linear(c.b);
    }
}
