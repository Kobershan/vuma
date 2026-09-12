using System.Text;
using System.Text.Json.Nodes;

namespace VumaRetail.TokenGenerator;

public static class Program
{
    private const string TokensPath = "design/tokens.json";
    private const string OutputDir = "design/generated";
    private const string DesktopThemesDir = "src/VumaRetail.Desktop/Themes";
    private const string AndroidThemeDir = "android/core-ui/theme";
    private const string Marker = "/* Auto-generated from design/tokens.json — DO NOT EDIT. Run the token generator instead. */";

    public static void Main()
    {
        var json = File.ReadAllText(TokensPath);
        var tokens = JsonNode.Parse(json)!;

        Directory.CreateDirectory(OutputDir);
        Directory.CreateDirectory(DesktopThemesDir);
        Directory.CreateDirectory(AndroidThemeDir);

        GenerateWpfThemes(tokens);
        GenerateAndroidCompose(tokens);
        GenerateCss(tokens);
    }

    private static void GenerateWpfThemes(JsonNode tokens)
    {
        var color = tokens["color"]!;
        var light = color["light"]!;
        var dark = color["dark"]!;

        foreach (var (themeName, themeTokens) in new[] { ("Light", light), ("Dark", dark) })
        {
            var sb = new StringBuilder();
            sb.AppendLine("<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"");
            sb.AppendLine("                xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">");
            sb.AppendLine($"  <!-- {Marker.Trim('/', '*', ' ')} -->");
            WriteWpfColors(sb, themeTokens["surface"]!, "Surface");
            WriteWpfColors(sb, themeTokens["text"]!, "Text");
            WriteWpfSemantic(sb, themeTokens);
            sb.AppendLine("</ResourceDictionary>");

            var content = NormalizeLineEndings(sb.ToString());
            File.WriteAllText(Path.Combine(OutputDir, $"VumaDesktop.Themes.{themeName}.xaml"), content);
            File.WriteAllText(Path.Combine(DesktopThemesDir, $"{themeName}.xaml"), content);
        }
    }

    private static void WriteWpfColors(StringBuilder sb, JsonNode section, string className)
    {
        foreach (var prop in section.AsObject()!)
        {
            var hex = prop.Value!.GetValue<string>()!;
            var name = $"Vuma{className}{ToPascal(prop.Key)}";
            sb.AppendLine($"  <Color x:Key=\"{name}\">{hex}</Color>");
            sb.AppendLine($"  <SolidColorBrush x:Key=\"{name}Brush\" Color=\"{{StaticResource {name}}}\" />");
        }
        sb.AppendLine();
    }

    private static void WriteWpfSemantic(StringBuilder sb, JsonNode color)
    {
        foreach (var key in new[] { "accent", "accentQuiet", "positive", "warning", "critical", "info" })
        {
            var hex = color[key]!.GetValue<string>()!;
            var name = $"Vuma{ToPascal(key)}";
            sb.AppendLine($"  <Color x:Key=\"{name}\">{hex}</Color>");
            sb.AppendLine($"  <SolidColorBrush x:Key=\"{name}Brush\" Color=\"{{StaticResource {name}}}\" />");
        }
        sb.AppendLine();
    }

    private static void GenerateAndroidCompose(JsonNode tokens)
    {
        var color = tokens["color"]!;
        var light = color["light"]!;
        var dark = color["dark"]!;

        foreach (var (themeName, themeTokens) in new[] { ("Light", light), ("Dark", dark) })
        {
            var sb = new StringBuilder();
            sb.AppendLine(Marker);
            sb.AppendLine("package core.ui.theme");
            sb.AppendLine();
            sb.AppendLine("import androidx.compose.ui.graphics.Color");
            sb.AppendLine();
            sb.AppendLine("object VumaTheme {");
            WriteComposeColors(sb, themeTokens);
            sb.AppendLine("}");

            var content = NormalizeLineEndings(sb.ToString());
            File.WriteAllText(Path.Combine(OutputDir, $"VumaTheme.{themeName}.kt"), content);
            File.WriteAllText(Path.Combine(AndroidThemeDir, $"VumaTheme.{themeName}.kt"), content);
        }
    }

    private static void WriteComposeColors(StringBuilder sb, JsonNode theme)
    {
        void C(string name, JsonNode parent, string key)
        {
            var hex = parent[key]!.GetValue<string>()!;
            sb.AppendLine($"    val {name} = Color(0xFF{hex.TrimStart('#')})");
        }
        C("surfaceBase", theme["surface"]!, "base");
        C("surfaceRaised", theme["surface"]!, "raised");
        C("surfaceSunken", theme["surface"]!, "sunken");
        C("separator", theme, "separator");
        C("textPrimary", theme["text"]!, "primary");
        C("textSecondary", theme["text"]!, "secondary");
        C("textTertiary", theme["text"]!, "tertiary");
        C("accent", theme, "accent");
        C("accentQuiet", theme, "accentQuiet");
        C("positive", theme, "positive");
        C("warning", theme, "warning");
        C("critical", theme, "critical");
        C("info", theme, "info");
    }

    private static void GenerateCss(JsonNode tokens)
    {
        var color = tokens["color"]!;
        var light = color["light"]!;
        var dark = color["dark"]!;
        var typography = tokens["typography"]!;
        var spacing = tokens["spacing"]!;
        var radius = tokens["radius"]!;
        var motion = tokens["motion"]!;
        var touchTarget = tokens["touchTarget"]!;

        var sb = new StringBuilder();
        sb.AppendLine(Marker);
        sb.AppendLine(":root, [data-theme=\"light\"] {");
        WriteCssColors(sb, light);
        WriteCssSemantic(sb, light);
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("[data-theme=\"dark\"] {");
        WriteCssColors(sb, dark);
        WriteCssSemantic(sb, dark);
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine(":root {");
        WriteCssTypography(sb, typography);
        WriteCssSpacing(sb, spacing);
        WriteCssRadius(sb, radius);
        WriteCssMotion(sb, motion);
        WriteCssTouchTarget(sb, touchTarget);
        sb.AppendLine("}");

        var content = NormalizeLineEndings(sb.ToString());
        File.WriteAllText("design/tokens.css", content);
    }

    private static void WriteCssColors(StringBuilder sb, JsonNode theme)
    {
        WriteFlat(sb, theme["surface"]!, "surface");
        WriteFlat(sb, theme["text"]!, "text");
    }

    private static void WriteFlat(StringBuilder sb, JsonNode node, string prefix, string[]? excludeKeys = null)
    {
        var exclude = excludeKeys ?? [];
        foreach (var prop in node.AsObject()!)
        {
            if (exclude.Contains(prop.Key))
            {
                sb.AppendLine($"  --{prefix}{prop.Key}: {prop.Value!.GetValue<string>()!};");
            }
            else if (prop.Value is JsonObject)
            {
                WriteFlat(sb, prop.Value!, $"{prefix}{prop.Key}-");
            }
        }
    }

    private static void WriteCssSemantic(StringBuilder sb, JsonNode theme)
    {
        foreach (var key in new[] { "accent", "accentQuiet", "positive", "warning", "critical", "info" })
        {
            sb.AppendLine($"  --{key}: {theme[key]!.GetValue<string>()!};");
        }
    }

    private static void WriteCssTypography(StringBuilder sb, JsonNode typography)
    {
        var scale = typography["scale"]!;
        var fontFamilies = typography["fontFamily"]!;
        foreach (var prop in scale.AsObject()!)
        {
            var t = prop.Value!;
            var fontFamilyKey = t["fontFamily"]!.GetValue<string>()!;
            var fontFamilyName = fontFamilies[fontFamilyKey]!.GetValue<string>()!;
            sb.AppendLine($"  --font-{prop.Key}-size: {t["size"]}px;");
            sb.AppendLine($"  --font-{prop.Key}-line-height: {t["lineHeight"]}px;");
            sb.AppendLine($"  --font-{prop.Key}-weight: {t["weight"]};");
            sb.AppendLine($"  --font-{prop.Key}-family: {fontFamilyName};");
        }
    }

    private static void WriteCssSpacing(StringBuilder sb, JsonNode spacing)
    {
        foreach (var val in spacing["values"]!.AsArray()!)
        {
            sb.AppendLine($"  --spacing-{val}: {val}px;");
        }
    }

    private static void WriteCssRadius(StringBuilder sb, JsonNode radius)
    {
        foreach (var prop in radius.AsObject()!)
        {
            sb.AppendLine($"  --radius-{prop.Key}: {prop.Value}px;");
        }
    }

    private static void WriteCssMotion(StringBuilder sb, JsonNode motion)
    {
        foreach (var prop in motion.AsObject()!)
        {
            var duration = prop.Value!["duration"]!.GetValue<int>();
            sb.AppendLine($"  --motion-{prop.Key}-duration: {duration}ms;");
        }
    }

    private static void WriteCssTouchTarget(StringBuilder sb, JsonNode touchTarget)
    {
        foreach (var prop in touchTarget.AsObject()!)
        {
            sb.AppendLine($"  --touch-{prop.Key}: {prop.Value}px;");
        }
    }

    private static string ToPascal(string snake)
    {
        return string.Concat(snake.Split('_').Select(p => char.ToUpper(p[0]) + p.Substring(1)));
    }

    private static string NormalizeLineEndings(string content)
    {
        return content.Replace("\r\n", "\n").Replace("\r", "\n");
    }
}
