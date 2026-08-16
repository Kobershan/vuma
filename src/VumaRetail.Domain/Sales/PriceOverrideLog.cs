using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Sales;

/// <summary>
/// A record that somebody sold something at a price the resolver did not give them.
/// </summary>
/// <remarks>
/// <para>
/// <b>An override is a row, not a warning</b> (business rule 9). Selling off-price is legitimate and
/// routine — a damaged tin, a price-match, a manager's goodwill — and it is also the oldest shrinkage
/// pattern in retail. Both facts are handled by the same thing: the till does not refuse the override,
/// it records it with the operator, the two prices and the reason, so a variance that is one cashier
/// and one item every Friday is visible as a pattern rather than invisible as a series of individually
/// reasonable decisions.
/// </para>
/// <para>
/// <see cref="IImmutableRecord"/> and <see cref="ConflictPolicy.AppendOnly"/>: a log a later write can
/// overwrite is not a log. Same call <c>ReceiptPrint</c> made in Stage 09, for the same reason.
/// </para>
/// <para>
/// The row is written at the point of override, which is before the sale line exists and possibly
/// before the sale is completed at all — so <see cref="SaleId"/> and <see cref="SaleLineId"/> are
/// optional. An override on a sale later voided is still an override that happened, and deleting the
/// row to tidy that up would remove exactly the evidence the log exists for.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.AppendOnly)]
public sealed class PriceOverrideLog : Entity, IImmutableRecord
{
    private PriceOverrideLog(
        Guid tenantId,
        Guid? storeId,
        Guid? saleId,
        Guid? saleLineId,
        Guid? itemId,
        Guid? itemVariantId,
        Guid operatorUserId,
        Quantity quantity,
        Money resolvedUnitPrice,
        Money actualUnitPrice,
        string reason,
        DateTimeOffset occurredAt)
        : base(tenantId, storeId)
    {
        SaleId = saleId;
        SaleLineId = saleLineId;
        ItemId = itemId;
        ItemVariantId = itemVariantId;
        OperatorUserId = operatorUserId;
        Quantity = quantity;
        ResolvedUnitPrice = resolvedUnitPrice;
        ActualUnitPrice = actualUnitPrice;
        Reason = reason;
        OccurredAt = occurredAt;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private PriceOverrideLog()
    {
    }

    /// <summary>The sale it happened on, when there was one by then.</summary>
    public Guid? SaleId { get; private set; }

    /// <summary>The line it happened on, when the line already existed.</summary>
    public Guid? SaleLineId { get; private set; }

    /// <summary>The item sold off-price, when it has no variants.</summary>
    public Guid? ItemId { get; private set; }

    /// <summary>The variant sold off-price.</summary>
    public Guid? ItemVariantId { get; private set; }

    /// <summary>Who did it. The whole point of the row.</summary>
    public Guid OperatorUserId { get; private set; }

    /// <summary>How much was sold at the overridden price.</summary>
    public Quantity Quantity { get; private set; }

    /// <summary>What the resolver said one unit should cost.</summary>
    public Money ResolvedUnitPrice { get; private set; }

    /// <summary>What one unit was actually sold for.</summary>
    public Money ActualUnitPrice { get; private set; }

    /// <summary>Why, in the operator's words.</summary>
    public string Reason { get; private set; } = string.Empty;

    /// <summary>When, UTC.</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>
    /// What the override cost the shop across the whole quantity — negative when sold below the
    /// resolved price, which is the normal direction.
    /// </summary>
    /// <remarks>
    /// Computed rather than stored: it is a difference between two columns that are already here, and a
    /// stored copy is one more thing that can disagree with them. Reports that need to sum it group on
    /// the two price columns instead.
    /// </remarks>
    public Money Variance => (ActualUnitPrice - ResolvedUnitPrice) * Quantity.Value;

    /// <summary>Records an override.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The store it happened at.</param>
    /// <param name="saleId">The sale, when there was one.</param>
    /// <param name="saleLineId">The line, when there was one.</param>
    /// <param name="itemId">The item, when it has no variants.</param>
    /// <param name="itemVariantId">The variant.</param>
    /// <param name="operatorUserId">Who did it.</param>
    /// <param name="quantity">How much was sold at the overridden price.</param>
    /// <param name="resolvedUnitPrice">What the resolver said.</param>
    /// <param name="actualUnitPrice">What was charged.</param>
    /// <param name="reason">Why.</param>
    /// <param name="occurredAt">When, UTC.</param>
    /// <exception cref="SalesRuleException">
    /// A price is negative, the two prices are in different currencies, or the quantity is not positive.
    /// </exception>
    public static PriceOverrideLog Record(
        Guid tenantId,
        Guid? storeId,
        Guid? saleId,
        Guid? saleLineId,
        Guid? itemId,
        Guid? itemVariantId,
        Guid operatorUserId,
        Quantity quantity,
        Money resolvedUnitPrice,
        Money actualUnitPrice,
        string reason,
        DateTimeOffset occurredAt)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A price override must belong to a tenant.", nameof(tenantId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (itemId.HasValue == itemVariantId.HasValue)
        {
            throw SalesRuleException.ExactlyOneItemOrVariantRequired();
        }

        if (quantity.Value <= 0m)
        {
            throw SalesRuleException.QuantityMustBePositive();
        }

        if (resolvedUnitPrice.IsNegative || actualUnitPrice.IsNegative)
        {
            throw SalesRuleException.PriceMustNotBeNegative();
        }

        if (!string.Equals(resolvedUnitPrice.Currency, actualUnitPrice.Currency, StringComparison.Ordinal))
        {
            throw SalesRuleException.CurrencyMismatch(resolvedUnitPrice.Currency, actualUnitPrice.Currency);
        }

        return new PriceOverrideLog(
            tenantId,
            storeId,
            saleId,
            saleLineId,
            itemId,
            itemVariantId,
            operatorUserId,
            quantity,
            resolvedUnitPrice,
            actualUnitPrice,
            reason.Trim(),
            occurredAt);
    }
}
