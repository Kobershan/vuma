using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Quality;

/// <summary>A quality operation was well formed but cannot be accepted under the current stock state.</summary>
public sealed class QualityRuleException(string code, string message) : DomainException(code, message)
{
    /// <summary>The requested quarantine quantity exceeds the currently available stock.</summary>
    public static QualityRuleException HoldExceedsAvailable()
        => new(
            "QUALITY_HOLD_EXCEEDS_AVAILABLE",
            "The requested quality hold exceeds available stock; no hold was created.");
}
