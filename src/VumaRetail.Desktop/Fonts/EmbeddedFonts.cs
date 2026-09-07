// Placeholder font files — Inter, Inter Display, JetBrains Mono
// These are embedded in the installer per DESIGN_SYSTEM.md §8
// The actual font binaries are packaged by the installer build step
namespace VumaRetail.Desktop.Fonts;

public static class EmbeddedFonts
{
    /// <summary>Inter Display font family identifier.</summary>
    public const string Display = "Inter Display";

    /// <summary>Inter body font family identifier.</summary>
    public const string Body = "Inter";

    /// <summary>JetBrains Mono font family identifier.</summary>
    public const string Mono = "JetBrains Mono";
}
