using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Procurement;

/// <summary>One thing the shop says it needs, and how much of it.</summary>
/// <remarks>
/// <para>
/// <see cref="EstimatedUnitCost"/> is optional and is an estimate. It exists so an approver can see
/// roughly what they are agreeing to spend, and it is never carried onto an order as a price — the
/// price on an order is the supplier's, quoted or negotiated. Copying an estimate onto a commitment is
/// how a shop ends up ordering at a number nobody agreed to.
/// </para>
/// <para>
/// <see cref="SourcedToDocumentId"/> is what lets a requisition answer "did this ever get bought?".
/// It points at an RFQ or a purchase order, which is why it is a bare <see cref="Guid"/> with no typed
/// reference: which of the two it is depends on how the buyer chose to source it.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.Bidirectional, ConflictPolicy.CloudWins)]
public sealed class PurchaseRequisitionLine : Entity
{
    private PurchaseRequisitionLine(
        Guid tenantId,
        Guid? storeId,
        Guid purchaseRequisitionId,
        Guid? itemId,
        Guid? itemVariantId,
        string description,
        Quantity quantity,
        Money? estimatedUnitCost)
        : base(tenantId, storeId)
    {
        PurchaseRequisitionId = purchaseRequisitionId;
        ItemId = itemId;
        ItemVariantId = itemVariantId;
        Description = description;
        Quantity = quantity;
        EstimatedUnitCostAmount = estimatedUnitCost?.Amount;
        EstimatedUnitCostCurrency = estimatedUnitCost?.Currency;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private PurchaseRequisitionLine()
    {
    }

    /// <summary>The requisition this line belongs to.</summary>
    public Guid PurchaseRequisitionId { get; private set; }

    /// <summary>The item, when it has no variants.</summary>
    public Guid? ItemId { get; private set; }

    /// <summary>The variant.</summary>
    public Guid? ItemVariantId { get; private set; }

    /// <summary>What it is, in the requester's words.</summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>How much is needed. Always positive.</summary>
    public Quantity Quantity { get; private set; }

    /// <summary>
    /// The estimate's amount, or <c>null</c>. Mapped as two plain columns behind
    /// <see cref="EstimatedUnitCost"/> — EF Core 9 cannot map an optional complex property, the same
    /// constraint <c>Promotion.RewardAmount</c> and <c>JournalLine</c> work around (ADR-067).
    /// </summary>
    public decimal? EstimatedUnitCostAmount { get; private set; }

    /// <summary>The estimate's currency, or <c>null</c>.</summary>
    public string? EstimatedUnitCostCurrency { get; private set; }

    /// <summary>What the requester thinks one unit costs, or <c>null</c>. An estimate, never a commitment.</summary>
    public Money? EstimatedUnitCost
        => EstimatedUnitCostAmount is { } amount && EstimatedUnitCostCurrency is { } currency
            ? new Money(amount, currency)
            : null;

    /// <summary>The RFQ or purchase order this line was sourced onto, or <c>null</c> if it never was.</summary>
    public Guid? SourcedToDocumentId { get; private set; }

    /// <summary>When it was sourced, or <c>null</c>.</summary>
    public DateTimeOffset? SourcedAt { get; private set; }

    /// <summary>True once this line has been carried onto a sourcing document.</summary>
    public bool IsSourced => SourcedToDocumentId is not null;

    /// <summary>
    /// Builds a requisition line. Prefer <see cref="PurchaseRequisition.AddLine"/>, which enforces the
    /// document's rules.
    /// </summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The store that needs the goods.</param>
    /// <param name="purchaseRequisitionId">The requisition.</param>
    /// <param name="itemId">The item, when it has no variants.</param>
    /// <param name="itemVariantId">The variant.</param>
    /// <param name="description">What it is.</param>
    /// <param name="quantity">How much. Positive.</param>
    /// <param name="estimatedUnitCost">The estimate, or <c>null</c>.</param>
    /// <exception cref="ProcurementRuleException">
    /// Neither or both of item and variant were named, the quantity is not positive, or the estimate is
    /// negative.
    /// </exception>
    public static PurchaseRequisitionLine For(
        Guid tenantId,
        Guid? storeId,
        Guid purchaseRequisitionId,
        Guid? itemId,
        Guid? itemVariantId,
        string description,
        Quantity quantity,
        Money? estimatedUnitCost)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        ProcurementLineRules.RequireExactlyOneStockKeepingUnit(itemId, itemVariantId);
        ProcurementLineRules.RequirePositive(quantity);

        if (estimatedUnitCost is { IsNegative: true })
        {
            throw ProcurementRuleException.CostMustNotBeNegative();
        }

        return new PurchaseRequisitionLine(
            tenantId, storeId, purchaseRequisitionId, itemId, itemVariantId, description.Trim(),
            quantity, estimatedUnitCost);
    }

    /// <summary>Records that this line was carried onto an RFQ or a purchase order.</summary>
    /// <param name="documentId">The document that now carries it.</param>
    /// <param name="sourcedAt">When, UTC.</param>
    internal void RecordSourced(Guid documentId, DateTimeOffset sourcedAt)
    {
        SourcedToDocumentId = documentId;
        SourcedAt = sourcedAt;
    }
}

/// <summary>
/// The two checks every procurement line makes, in one place rather than six.
/// </summary>
/// <remarks>
/// <c>CONVENTIONS.md</c> §4's rule about extracting shared logic, applied at its smallest scale. Written
/// per line type these would be right the first four times and subtly different the fifth — and "exactly
/// one of item or variant" written slightly differently in two places is the kind of divergence that
/// only shows up as a row the database check constraint rejects.
/// </remarks>
internal static class ProcurementLineRules
{
    /// <summary>Refuses a line that names neither an item nor a variant, or both.</summary>
    /// <param name="itemId">The item.</param>
    /// <param name="itemVariantId">The variant.</param>
    internal static void RequireExactlyOneStockKeepingUnit(Guid? itemId, Guid? itemVariantId)
    {
        if ((itemId is null) == (itemVariantId is null))
        {
            throw ProcurementRuleException.ExactlyOneItemOrVariantRequired();
        }
    }

    /// <summary>Refuses a quantity that is zero or negative.</summary>
    /// <param name="quantity">The quantity.</param>
    internal static void RequirePositive(Quantity quantity)
    {
        if (quantity.Value <= 0m)
        {
            throw ProcurementRuleException.QuantityMustBePositive();
        }
    }

    /// <summary>Refuses a negative cost.</summary>
    /// <param name="cost">The cost.</param>
    internal static void RequireNonNegative(Money cost)
    {
        if (cost.IsNegative)
        {
            throw ProcurementRuleException.CostMustNotBeNegative();
        }
    }

    /// <summary>Refuses money denominated in something other than the document's currency (business rule 4).</summary>
    /// <param name="documentCurrency">The document's currency.</param>
    /// <param name="amount">The amount being put onto it.</param>
    internal static void RequireSameCurrency(string documentCurrency, Money amount)
    {
        if (!string.Equals(documentCurrency, amount.Currency, StringComparison.Ordinal))
        {
            throw ProcurementRuleException.CurrencyMismatch(documentCurrency, amount.Currency);
        }
    }
}
