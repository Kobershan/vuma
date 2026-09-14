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

    /// <summary>Dispatch cannot use stock covered by an active quality hold.</summary>
    public static QualityRuleException DispatchBlocked()
        => new("QUALITY_DISPATCH_BLOCKED", "Dispatch is blocked because the stock is under an active quality hold.");

    /// <summary>Dispatch cannot use tracked stock whose expiry boundary has passed.</summary>
    public static QualityRuleException DispatchBlockedForExpiredStock()
        => new("QUALITY_DISPATCH_EXPIRED_STOCK", "Dispatch is blocked because tracked stock has expired.");
}
