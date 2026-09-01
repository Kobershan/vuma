using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Sales;

/// <summary>
/// One price on a <see cref="PriceList"/> — what an item costs, from a given quantity upwards.
/// </summary>
/// <remarks>
/// <para>
/// A quantity break is a row rather than a feature. "R12.99 each, R11.50 from a dozen" is two lines
/// for the same item at minimum quantities of 1 and 12, which means adding a break is data entry and
/// the resolver needs one rule — take the largest break at or below the quantity being sold — instead
/// of a pricing DSL.
/// </para>
/// <para>
/// The uniqueness rule (list, item/variant, minimum quantity) is what makes Stage 11's bulk price
/// import idempotent: re-importing the same sheet updates rows rather than accumulating duplicates.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.Bidirectional, ConflictPolicy.CloudWins)]
public sealed class PriceListLine : Entity
{
    private PriceListLine(
        Guid tenantId,
        Guid? storeId,
        Guid priceListId,
        Guid? itemId,
        Guid? itemVariantId,
        Money unitPrice,
        decimal minimumQuantity)
        : base(tenantId, storeId)
    {
        PriceListId = priceListId;
        ItemId = itemId;
        ItemVariantId = itemVariantId;
        UnitPrice = unitPrice;
        MinimumQuantity = minimumQuantity;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private PriceListLine()
    {
    }

    /// <summary>The list this price belongs to.</summary>
    public Guid PriceListId { get; private set; }

    /// <summary>The item priced, when it has no variants.</summary>
    public Guid? ItemId { get; private set; }

    /// <summary>The variant priced.</summary>
    public Guid? ItemVariantId { get; private set; }

    /// <summary>What one unit sells for, in the list's currency.</summary>
    public Money UnitPrice { get; private set; }

    /// <summary>
    /// The quantity this price starts applying at. <c>1</c> for the ordinary price; a higher value
    /// makes the row a quantity break.
    /// </summary>
    public decimal MinimumQuantity { get; private set; }

    /// <summary>Creates a price list line. Prefer <see cref="PriceList.AddLine"/>, which enforces the list's rules.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The owning store, inherited from the list.</param>
    /// <param name="priceListId">The list this price belongs to.</param>
    /// <param name="itemId">The item, when it has no variants.</param>
    /// <param name="itemVariantId">The variant.</param>
    /// <param name="unitPrice">What one unit sells for.</param>
    /// <param name="minimumQuantity">The quantity this price starts applying at.</param>
    /// <exception cref="SalesRuleException">
    /// The line names both or neither of item and variant, the price is negative, or the minimum
    /// quantity is not positive.
    /// </exception>
    public static PriceListLine Create(
        Guid tenantId,
        Guid? storeId,
        Guid priceListId,
        Guid? itemId,
        Guid? itemVariantId,
        Money unitPrice,
        decimal minimumQuantity)
    {
        if (itemId.HasValue == itemVariantId.HasValue)
        {
            throw SalesRuleException.ExactlyOneItemOrVariantRequired();
        }

        if (unitPrice.IsNegative)
        {
            throw SalesRuleException.PriceMustNotBeNegative();
        }

        if (minimumQuantity <= 0m)
        {
            throw SalesRuleException.QuantityMustBePositive();
        }

        return new PriceListLine(
            tenantId, storeId, priceListId, itemId, itemVariantId, unitPrice, minimumQuantity);
    }

    /// <summary>Reprices the line.</summary>
    /// <param name="unitPrice">The new unit price, in the same currency.</param>
    /// <exception cref="SalesRuleException">The price is negative or in another currency.</exception>
    public void Reprice(Money unitPrice)
    {
        if (unitPrice.IsNegative)
        {
            throw SalesRuleException.PriceMustNotBeNegative();
        }

        if (!string.Equals(unitPrice.Currency, UnitPrice.Currency, StringComparison.Ordinal))
        {
            throw SalesRuleException.CurrencyMismatch(UnitPrice.Currency, unitPrice.Currency);
        }

        UnitPrice = unitPrice;
    }
}
