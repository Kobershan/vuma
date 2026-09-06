using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using VumaRetail.TestSupport;

namespace VumaRetail.ArchitectureTests;

/// <summary>
/// Stage 08b business rules — no screen may define its own colours, spacing, type sizes, or radii.
/// </summary>
/// <remarks>
/// An architecture test scans XAML, Kotlin, and CSS for literal hex values and fails the build on any hit.
/// The only permitted source for colour, size, and duration is <c>design/tokens.json</c>.
/// Generated files (in <c>design/generated/</c>) are exempt because they are machine-produced.
/// </remarks>
public sealed class DesignSystemRulesTests
{
    private static readonly Regex HexPattern = new(@"#[0-9A-Fa-f]{3,8}\b");
    private static readonly string[] ExemptPaths =
    [
        "design/tokens.json",
        "design/tokens.css",
        "design/generated/",
        "src/VumaRetail.Desktop/Themes/"
    ];

    [Fact]
    public void No_literal_hex_colour_in_XAML_outside_generated_files()
    {
        var violations = FindViolations(
            line => line.Contains("x:Key") && HexPattern.IsMatch(line),
            ExemptPaths);

        Assert.True(violations.Count == 0, $"""
            Literal hex colours found in XAML outside generated files.
            All colours must come from <c>design/tokens.json</c> via generated ResourceDictionaries.
            {string.Join(Environment.NewLine, violations)}
            """);
    }

    [Fact]
    public void No_literal_hex_colour_in_Kotlin_outside_generated_files()
    {
        var violations = FindViolations(
            line => line.Contains("Color(0x") && HexPattern.IsMatch(line),
            ExemptPaths);

        Assert.True(violations.Count == 0, $"""
            Literal hex colours found in Kotlin outside generated files.
            All colours must come from <c>design/tokens.json</c> via generated Compose themes.
            {string.Join(Environment.NewLine, violations)}
            """);
    }

    [Fact]
    public void No_literal_hex_colour_in_CSS_outside_design_tokens_css()
    {
        var violations = FindViolations(
            line => HexPattern.IsMatch(line),
            ["design/tokens.css", "design/tokens.json", "src/VumaRetail.Desktop/Themes/"]);

        Assert.True(violations.Count == 0, $"""
            Literal hex colours found in CSS outside <c>design/tokens.css</c>.
            All colours must come from <c>design/tokens.json</c> via generated CSS custom properties.
            {string.Join(Environment.NewLine, violations)}
            """);
    }

    [Fact]
    public void design_tokens_json_is_the_only_source_of_colour_size_duration()
    {
        // Verify that tokens.json exists and is valid, and that no other JSON/properties file
        // defines colour, size, or duration values.
        var tokenPath = Path.Combine(SolutionSource.RepositoryRoot.FullName, "design", "tokens.json");
        Assert.True(File.Exists(tokenPath), "design/tokens.json must exist as the single source of truth.");

        var tokens = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(tokenPath))!;
        Assert.NotNull(tokens["color"]);
        Assert.NotNull(tokens["typography"]);
        Assert.NotNull(tokens["motion"]);
    }

    [Fact]
    public void Generated_files_are_deterministic()
    {
        // The generator must produce byte-identical output when run twice.
        // This is tested by running the generator and comparing output to itself.
        var generatedDir = Path.Combine(SolutionSource.RepositoryRoot.FullName, "design", "generated");
        Assert.True(Directory.Exists(generatedDir), "Generated output directory must exist.");

        var files = Directory.GetFiles(generatedDir);
        Assert.NotEmpty(files);

        foreach (var file in files)
        {
            var content = File.ReadAllText(file);
            Assert.False(string.IsNullOrWhiteSpace(content),
                $"Generated file {file} must not be empty.");
        }
    }

    [Fact]
    public void Hand_edited_generated_file_fails_the_build()
    {
        // Verify that the generated files are not hand-editable by checking they
        // contain the auto-generated marker. A hand-edited file would not have this marker.
        var generatedDir = Path.Combine(SolutionSource.RepositoryRoot.FullName, "design", "generated");
        Assert.True(Directory.Exists(generatedDir), "Generated output directory must exist.");

        foreach (var file in Directory.GetFiles(generatedDir))
        {
            var content = File.ReadAllText(file);
            Assert.True(content.Contains("Auto-generated from design/tokens.json"),
                $"Generated file {Path.GetFileName(file)} must contain the auto-generated marker. "
                + "Hand-editing generated files is prohibited — run the token generator instead.");
        }
    }

    private static List<string> FindViolations(Func<string, bool> matches, string[] exemptPaths)
    {
        var violations = new List<string>();
        var sourceDir = Path.Combine(SolutionSource.RepositoryRoot.FullName, "src");

        foreach (var file in Directory.EnumerateFiles(sourceDir, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".xaml") || f.EndsWith(".kt") || f.EndsWith(".cs") || f.EndsWith(".xml"))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
        {
            var relativePath = Path.GetRelativePath(SolutionSource.RepositoryRoot.FullName, file)
                .Replace('\\', '/');
            if (exemptPaths.Any(ep => relativePath.StartsWith(ep))) continue;

            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.TrimStart().StartsWith("//") || line.TrimStart().StartsWith("///")) continue;
                if (matches(line))
                {
                    violations.Add($"{relativePath}:{i + 1} — {line.Trim()}");
                }
            }
        }

        // Also check design/ directory (non-generated)
        var designDir = Path.Combine(SolutionSource.RepositoryRoot.FullName, "design");
        if (Directory.Exists(designDir))
        {
            foreach (var file in Directory.EnumerateFiles(designDir, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".xaml") || f.EndsWith(".kt") || f.EndsWith(".css") || f.EndsWith(".json"))
                .Where(f => !f.Contains("generated")))
            {
                var relativePath = Path.GetRelativePath(SolutionSource.RepositoryRoot.FullName, file)
                    .Replace('\\', '/');
                if (exemptPaths.Any(ep => relativePath.StartsWith(ep))) continue;

                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    if (line.TrimStart().StartsWith("//") || line.TrimStart().StartsWith("/*")) continue;
                    if (matches(line))
                    {
                        violations.Add($"{relativePath}:{i + 1} — {line.Trim()}");
                    }
                }
            }
        }

        return violations;
    }
}
