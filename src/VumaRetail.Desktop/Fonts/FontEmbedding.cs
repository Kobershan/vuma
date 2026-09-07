using System.IO.Compression;

namespace VumaRetail.Desktop.Fonts;

/// <summary>
/// Font embedding helpers. Inter, Inter Display, and JetBrains Mono are embedded
/// in the installer per DESIGN_SYSTEM.md §8 — no runtime download.
/// </summary>
public static class FontEmbedding
{
    /// <summary>Register the font families with the WPF application.</summary>
    public static void RegisterFonts()
    {
        // Fonts are packaged as resources in the installer.
        // At runtime they are loaded from the application's resource dictionary.
        // The font files are placed in the Fonts/ directory and referenced
        // via pack URIs in the theme ResourceDictionaries.
    }

    /// <summary>Get the pack URI for the Inter Display font.</summary>
    public const string InterDisplay = "fonts/InterDisplay.ttf#Inter Display";

    /// <summary>Get the pack URI for the Inter font.</summary>
    public const string Inter = "fonts/Inter.ttf#Inter";

    /// <summary>Get the pack URI for the JetBrains Mono font.</summary>
    public const string JetBrainsMono = "fonts/JetBrainsMono.ttf#JetBrains Mono";
}
