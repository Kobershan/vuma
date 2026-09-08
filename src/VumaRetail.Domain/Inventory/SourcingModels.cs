using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Inventory;

/// <summary>One order line asking for stock, with its captured economics for the split.</summary>
/// <param name="LineId">The source line's id, so the split can be audited back to it.</param>
/// <param name="ItemId">The item, when it has no variants. Exactly one of this and <paramref name="ItemVariantId"/> must be set.</param>
/// <param name="ItemVariantId">The variant.</param>
/// <param name="Demanded">How much the line wants. Must be positive.</param>
/// <param name="UnitPrice">The line's unit price, as captured on the source document. Carried, never priced.</param>
/// <param name="LineNet">The line's net, as captured on the source document.</param>
/// <param name="LineTax">The line's tax, as captured on the source document.</param>
/// <param name="LineGross">The line's gross, as captured on the source document.</param>
/// <param name="PriceListId">The price list the line was resolved against, when one was.</param>
/// <param name="PromotionsSummary">The promotions summary, carried for the same reason Stage 10 keeps one.</param>
/// <remarks>
/// Money here is a snapshot carried for reconciliation, never priced: this stage never prices
/// anything (Stage 10 owns pricing; ADR-075/138 snapshot discipline applies to what is carried).
/// All amounts share one currency — a multi-currency order is refused at commit time, one
/// order one currency being what <c>SalesOrder</c> already requires.
/// </remarks>
public sealed record SourcingDemandLine(
    Guid LineId,
    Guid? ItemId,
    Guid? ItemVariantId,
    Quantity Demanded,
    Money UnitPrice,
    Money LineNet,
    Money LineTax,
    Money LineGross,
    Guid? PriceListId = null,
    string PromotionsSummary = "");

/// <summary>How much of one demand line one company location supplies.</summary>
/// <param name="CompanyId">The supplying company.</param>
/// <param name="LocationId">The supplying location.</param>
/// <param name="Quantity">How much it supplies. Always positive — plan rows with zero are omitted, never emitted.</param>
public sealed record SourcingAllocation(Guid CompanyId, Guid LocationId, Quantity Quantity);

/// <summary>One demand line's sourcing answer: who supplies what, and what remains uncovered.</summary>
/// <param name="LineId">The demand line.</param>
/// <param name="Allocations">One row per supplying location. Never empty when <paramref name="Backorder"/> is zero.</param>
/// <param name="Backorder">What group-wide availability could not cover. Zero means fully sourced.</param>
public sealed record SourcingPlanLine(Guid LineId, IReadOnlyList<SourcingAllocation> Allocations, Quantity Backorder)
{
    /// <summary>How much of the line is covered, across all suppliers.</summary>
    public decimal AllocatedValue => Allocations.Sum(allocation => allocation.Quantity.Value);
}

/// <summary>A sourcing plan: a projection until committed (business rule 4).</summary>
/// <param name="OrderingCompanyId">The company the order was captured against.</param>
/// <param name="Lines">One answer per demand line, in demand order.</param>
public sealed record SourcingPlan(Guid OrderingCompanyId, IReadOnlyList<SourcingPlanLine> Lines)
{
    /// <summary>Every supplying company in the plan, in first-use order.</summary>
    public IReadOnlyList<Guid> SupplyingCompanies => Lines
        .SelectMany(line => line.Allocations)
        .Select(allocation => allocation.CompanyId)
        .Distinct()
        .ToList();

    /// <summary>Whether every line is fully covered with nothing backordered.</summary>
    public bool IsFullyCovered => Lines.All(line => line.Backorder.IsZero);
}

/// <summary>
/// Splits whole-line money across quantity slices so the slices telescope exactly to the source.
/// </summary>
/// <remarks>
/// The same cumulative-rounding shape Stage 10's sales returns use (ADR-075): slice <c>i</c> takes
/// the rounded share of everything up to it less the rounded share of everything before it, so
/// partial slices telescope and their sum is exactly the source line — rounding each slice
/// independently does not have that property, and the failure is money the shop never took.
/// Rounds at scale 4, midpoints away from zero (ADR-033).
/// </remarks>
public static class MoneyTelescoping
{
    /// <summary>Splits a whole-line amount across ordered quantity slices.</summary>
    /// <param name="whole">The source line's amount for its full quantity.</param>
    /// <param name="totalQuantity">The source line's full quantity.</param>
    /// <param name="slices">Each slice's quantity, in a deterministic order the caller owns.</param>
    /// <returns>One amount per slice, summing exactly to <paramref name="whole"/>.</returns>
    public static IReadOnlyList<Money> Split(Money whole, decimal totalQuantity, IReadOnlyList<decimal> slices)
    {
        ArgumentNullException.ThrowIfNull(slices);

        if (totalQuantity <= 0m)
        {
            throw new ArgumentException("A split needs a positive total quantity.", nameof(totalQuantity));
        }

        if (slices.Any(slice => slice < 0m))
        {
            throw new ArgumentException("Slice quantities cannot be negative.", nameof(slices));
        }

        if (Math.Abs(slices.Sum() - totalQuantity) > 0.0000005m)
        {
            throw new ArgumentException("Slices must sum to the total quantity.", nameof(slices));
        }

        List<Money> result = new(slices.Count);
        decimal cumulative = 0m;
        decimal previousShare = 0m;

        foreach (decimal slice in slices)
        {
            cumulative += slice;
            decimal share = Math.Round(
                whole.Amount * cumulative / totalQuantity, 4, MidpointRounding.AwayFromZero);
            result.Add(new Money(share - previousShare, whole.Currency));
            previousShare = share;
        }

        return result;
    }
}
