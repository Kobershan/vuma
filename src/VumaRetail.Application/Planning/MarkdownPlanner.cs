using VumaRetail.Domain.Planning;

namespace VumaRetail.Application.Planning;

/// <summary>Signals behind a markdown proposal for one SKU.</summary>
/// <param name="Abc">The latest ABC class, or <c>null</c> when unclassified.</param>
/// <param name="Xyz">The latest XYZ class, or <c>null</c> when unclassified.</param>
/// <param name="SellThroughPercent">Sold share of (sold + on-hand) over the review window, 0–100.</param>
/// <param name="DaysOfSupply">On-hand days at current demand. Infinite when demand is zero.</param>
public sealed record MarkdownSignals(
    AbcClass? Abc,
    XyzClass? Xyz,
    decimal SellThroughPercent,
    decimal DaysOfSupply);

/// <summary>What the markdown planner says about one SKU.</summary>
/// <param name="Propose">True when a markdown should be proposed.</param>
/// <param name="DiscountPercent">The proposed percentage off. Zero when not proposed.</param>
public sealed record MarkdownVerdict(bool Propose, decimal DiscountPercent);

/// <summary>Pure markdown evaluator — classifies and phrases inputs, never touches data.</summary>
public interface IMarkdownPlanner
{
    /// <summary>Evaluates one SKU's signals against the thresholds.</summary>
    MarkdownVerdict Evaluate(MarkdownSignals signals);
}

/// <summary>
/// Markdown thresholds (ADR-149): slow (sell-through at or below 20%), deep (at or above 90 days
/// of supply), and unwanted (C or Z class). All three must hold. Boundaries are inclusive — stock
/// sitting exactly on the line is still slow stock.
/// </summary>
public sealed class MarkdownPlanner : IMarkdownPlanner
{
    /// <summary>Sell-through at or below this proposes a markdown.</summary>
    public const decimal SellThroughThresholdPercent = 20m;

    /// <summary>Days of supply at or above this proposes a markdown.</summary>
    public const decimal DaysOfSupplyThreshold = 90m;

    /// <summary>The standard first-markdown percentage.</summary>
    public const decimal StandardDiscountPercent = 25m;

    /// <inheritdoc />
    public MarkdownVerdict Evaluate(MarkdownSignals signals)
    {
        ArgumentNullException.ThrowIfNull(signals);

        bool slow = signals.SellThroughPercent <= SellThroughThresholdPercent;
        bool deep = signals.DaysOfSupply >= DaysOfSupplyThreshold;
        bool unwanted = signals.Abc is AbcClass.C || signals.Xyz is XyzClass.Z;

        return slow && deep && unwanted
            ? new MarkdownVerdict(true, StandardDiscountPercent)
            : new MarkdownVerdict(false, 0m);
    }
}

