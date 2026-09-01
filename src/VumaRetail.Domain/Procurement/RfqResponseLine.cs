using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Procurement;

/// <summary>What one supplier charges for one of the RFQ's lines, and how much of it they have.</summary>
/// <remarks>
/// <para>
/// <see cref="QuotedQuantity"/> is the half of a quote buyers forget to record and then argue about.
/// A supplier who quotes the best price on 400 of the 1,000 asked for has not quoted the best price;
/// snapshotting what was asked for alongside what they offered is what makes
/// <see cref="IsFullQuantity"/> answerable later, when the RFQ line's own quantity may have moved on.
/// </para>
/// <para>
/// <see cref="ExtendedCost"/> is computed on the quoted quantity, not on the requested one, for the
/// same reason: comparing a partial quote's price against a full quote's total flatters the partial
/// one.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.Bidirectional, ConflictPolicy.CloudWins)]
public sealed class RfqResponseLine : Entity
{
    private RfqResponseLine(
        Guid tenantId,
        Guid? storeId,
        Guid rfqResponseId,
        Guid rfqLineId,
        Guid? itemId,
        Guid? itemVariantId,
        Quantity requestedQuantity,
        Quantity quotedQuantity,
        Money unitCost,
        Money extendedCost)
        : base(tenantId, storeId)
    {
        RfqResponseId = rfqResponseId;
        RfqLineId = rfqLineId;
        ItemId = itemId;
        ItemVariantId = itemVariantId;
        RequestedQuantity = requestedQuantity;
        QuotedQuantity = quotedQuantity;
        UnitCost = unitCost;
        ExtendedCost = extendedCost;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private RfqResponseLine()
    {
    }

    /// <summary>The response this line belongs to.</summary>
    public Guid RfqResponseId { get; private set; }

    /// <summary>The RFQ line being quoted for.</summary>
    public Guid RfqLineId { get; private set; }

    /// <summary>The item, when it has no variants. Copied off the RFQ line.</summary>
    public Guid? ItemId { get; private set; }

    /// <summary>The variant. Copied off the RFQ line.</summary>
    public Guid? ItemVariantId { get; private set; }

    /// <summary>How much was asked for, snapshotted when the quote was recorded.</summary>
    public Quantity RequestedQuantity { get; private set; }

    /// <summary>How much this supplier can actually supply.</summary>
    public Quantity QuotedQuantity { get; private set; }

    /// <summary>What they charge per unit, in the response's currency.</summary>
    public Money UnitCost { get; private set; }

    /// <summary>The quoted quantity at the quoted cost, rounded once (ADR-033).</summary>
    public Money ExtendedCost { get; private set; }

    /// <summary>True when the supplier can supply everything that was asked for.</summary>
    public bool IsFullQuantity => QuotedQuantity >= RequestedQuantity;

    /// <summary>
    /// Builds a quoted line. Prefer <see cref="RfqResponse.AddLine"/>, which enforces the response's
    /// rules.
    /// </summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The store sourcing.</param>
    /// <param name="rfqResponseId">The response.</param>
    /// <param name="rfqLine">The line being quoted for.</param>
    /// <param name="unitCost">What the supplier charges per unit.</param>
    /// <param name="availableQuantity">
    /// How much they have, or <c>null</c> for everything asked for. A bare <see cref="decimal"/>
    /// rather than a <see cref="Quantity"/>, deliberately: a quote is against the RFQ line, so it is
    /// counted in that line's unit by construction. Letting a caller supply the unit would let a
    /// supplier quote 40 cases against a line asking for 40 kilograms, and the comparison would be
    /// arithmetically valid and completely wrong.
    /// </param>
    /// <exception cref="ProcurementRuleException">The quoted quantity is not positive.</exception>
    public static RfqResponseLine For(
        Guid tenantId,
        Guid? storeId,
        Guid rfqResponseId,
        RfqLine rfqLine,
        Money unitCost,
        decimal? availableQuantity)
    {
        ArgumentNullException.ThrowIfNull(rfqLine);

        Quantity quoted = availableQuantity is { } available
            ? new Quantity(available, rfqLine.Quantity.UnitOfMeasure)
            : rfqLine.Quantity;

        ProcurementLineRules.RequirePositive(quoted);

        // Rounded once, on the extended amount, rather than on the unit cost first. PROGRESS.md §4.14 is
        // the same arithmetic getting this backwards on the sell side: rounding per unit and then
        // multiplying is systematically wrong in one direction, and on a case of 144 it is wrong by
        // enough to notice.
        Money extended = quoted.Extend(unitCost).RoundToCurrencyScale();

        return new RfqResponseLine(
            tenantId, storeId, rfqResponseId, rfqLine.Id, rfqLine.ItemId, rfqLine.ItemVariantId,
            rfqLine.Quantity, quoted, unitCost, extended);
    }
}
