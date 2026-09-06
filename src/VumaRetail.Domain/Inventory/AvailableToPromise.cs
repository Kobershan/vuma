using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Inventory;

/// <summary>
/// How much of one stock-keeping unit at one location can actually be sold right now — the
/// answer to the first question Stage 08c exists to answer.
/// </summary>
/// <remarks>
/// <para>
/// ADR-103: <c>Available = OnHand − Reserved − InStaging</c>, computed per company, always.
/// <c>OnHand</c> is what the ledger says is physically present; <c>Reserved</c> is what live
/// holds speak for; <c>InStaging</c> is what sits in a staging bin (ADR-114) — on hand but not
/// promised to anyone yet. <c>Incoming</c> (open purchase supply) is carried as information and
/// deliberately NOT added: only stock that is here, unreserved and unstaged answers "can I sell
/// this", and showing on-hand where a user reads availability is a reportable defect (ADR-103).
/// </para>
/// <para>
/// <c>Available</c> is computed, never stored — there is no column anywhere that can disagree
/// with the three figures it derives from. The constructor refuses a combination that would
/// make it negative, so a negative available cannot be constructed, only refused at the
/// transaction that would have caused it.
/// </para>
/// </remarks>
public sealed record AvailableToPromise
{
    /// <summary>Builds the promise for one stock-keeping unit at one location.</summary>
    /// <param name="onHand">What the ledger says is physically present.</param>
    /// <param name="reserved">What live holds speak for.</param>
    /// <param name="inStaging">What sits in staging bins — on hand, not available.</param>
    /// <param name="incoming">Open inbound supply, informational only.</param>
    /// <param name="asAt">When the underlying figures were read. Every consumer must display it.</param>
    /// <exception cref="InventoryRuleException">The figures disagree on unit of measure, or available would go negative.</exception>
    public AvailableToPromise(Quantity onHand, Quantity reserved, Quantity inStaging, Quantity incoming, DateTimeOffset asAt)
    {
        EnsureSameUnit(onHand, reserved, nameof(reserved));
        EnsureSameUnit(onHand, inStaging, nameof(inStaging));

        if (reserved.Value < 0m || inStaging.Value < 0m)
        {
            throw InventoryRuleException.QuantityMustBePositive();
        }

        if (reserved + inStaging > onHand)
        {
            throw InventoryRuleException.AvailableWouldGoNegative(onHand, reserved, inStaging);
        }

        OnHand = onHand;
        Reserved = reserved;
        InStaging = inStaging;
        Incoming = incoming;
        AsAt = asAt;
    }

    /// <summary>What the ledger says is physically present.</summary>
    public Quantity OnHand { get; }

    /// <summary>What live holds speak for.</summary>
    public Quantity Reserved { get; }

    /// <summary>What sits in staging bins — on hand, not available.</summary>
    public Quantity InStaging { get; }

    /// <summary>Open inbound supply. Informational: never part of <see cref="Available"/>.</summary>
    public Quantity Incoming { get; }

    /// <summary>What can actually be sold: <c>OnHand − Reserved − InStaging</c>. Never negative by construction.</summary>
    public Quantity Available => OnHand - Reserved - InStaging;

    /// <summary>When the underlying figures were read. Every consumer must display it.</summary>
    public DateTimeOffset AsAt { get; }

    private static void EnsureSameUnit(Quantity anchor, Quantity candidate, string parameterName)
    {
        if (!string.Equals(anchor.UnitOfMeasure, candidate.UnitOfMeasure, StringComparison.Ordinal))
        {
            throw InventoryRuleException.UnitOfMeasureMismatch(anchor.UnitOfMeasure, candidate.UnitOfMeasure);
        }

        _ = parameterName;
    }
}
