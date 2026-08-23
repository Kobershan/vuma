using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Pos;

/// <summary>
/// One thing rung up on a sale: what it was, how much of it, what it was sold for, and what happened
/// to the stock behind it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The line carries the price it was sold at, not a price it looked up</b> (ADR-072). Stage 06
/// shipped no price by design and the price list, promotions and specials engine are Stage 10; until
/// then the caller supplies the price, which is also what an open-price or weighed line does forever.
/// The tax figures are computed by the tax rules engine at ring-up and stored, so tomorrow's rate
/// change does not restate yesterday's receipt.
/// </para>
/// <para>
/// <see cref="StockIssueStatus"/> is the honest half of R1. A till must be able to sell the thing that
/// is physically in the customer's hand, and Stage 08's ledger must not go negative (its rule 4). Both
/// hold: the sale completes, the issue is refused, and the line says so in a way a manager can query.
/// See ADR-073.
/// </para>
/// <para>
/// Voiding a line sets <see cref="IsVoided"/> rather than deleting the row — the fact that something
/// was rung up and taken off again is exactly what an audit wants to see (R6), and it is the oldest
/// shrinkage pattern there is.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class SaleLine : Entity
{
    private SaleLine(
        Guid id,
        Guid tenantId,
        Guid? storeId,
        Guid saleId,
        int lineNumber,
        Guid? itemId,
        Guid? itemVariantId,
        string description,
        Quantity quantity,
        Money unitPrice,
        Money discountAmount,
        string taxCode,
        Money net,
        Money tax,
        Money gross)
        : base(id, tenantId, storeId)
    {
        SaleId = saleId;
        LineNumber = lineNumber;
        ItemId = itemId;
        ItemVariantId = itemVariantId;
        Description = description;
        Quantity = quantity;
        UnitPrice = unitPrice;
        DiscountAmount = discountAmount;
        TaxCode = taxCode;
        Net = net;
        Tax = tax;
        Gross = gross;
        StockIssue = StockIssueStatus.Pending;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private SaleLine()
    {
    }

    /// <summary>The sale this line belongs to.</summary>
    public Guid SaleId { get; private set; }

    /// <summary>Its position on the receipt, 1-based. Stable once assigned, including after a void.</summary>
    public int LineNumber { get; private set; }

    /// <summary>The item sold, when it has no variants.</summary>
    public Guid? ItemId { get; private set; }

    /// <summary>The variant sold.</summary>
    public Guid? ItemVariantId { get; private set; }

    /// <summary>
    /// What the receipt says this was, snapshotted at ring-up. Renaming the item next month must not
    /// rewrite what a customer was handed.
    /// </summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>How much was sold. Always positive — a return is a Stage 10 document, not a negative line.</summary>
    public Quantity Quantity { get; private set; }

    /// <summary>What one unit was sold for, before the line discount.</summary>
    public Money UnitPrice { get; private set; }

    /// <summary>A manual discount off this line. Zero when there is none.</summary>
    public Money DiscountAmount { get; private set; }

    /// <summary>The tax code the rules engine was asked for, for example <c>STANDARD</c>.</summary>
    public string TaxCode { get; private set; } = string.Empty;

    /// <summary>The line excluding tax.</summary>
    public Money Net { get; private set; }

    /// <summary>The tax on the line.</summary>
    public Money Tax { get; private set; }

    /// <summary>The line as the customer sees it — <see cref="Net"/> plus <see cref="Tax"/>.</summary>
    public Money Gross { get; private set; }

    /// <summary>True once the line has been taken off the sale. It stays on the record.</summary>
    public bool IsVoided { get; private set; }

    /// <summary>When the line was voided, or <c>null</c>.</summary>
    public DateTimeOffset? VoidedAt { get; private set; }

    /// <summary>What happened when this line's stock was relieved.</summary>
    public StockIssueStatus StockIssue { get; private set; }

    /// <summary>The <c>inventory.stock_ledger_entries</c> row this line produced, when it produced one.</summary>
    public Guid? StockLedgerEntryId { get; private set; }

    /// <summary>Why the stock issue was refused, when it was.</summary>
    public string? StockIssueNote { get; private set; }

    /// <summary>Quantity times unit price, before the discount.</summary>
    public Money ExtendedPrice => UnitPrice * Quantity.Value;

    /// <summary>Rings a line up.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The store the sale happened at.</param>
    /// <param name="saleId">The sale.</param>
    /// <param name="lineNumber">Its position on the receipt, 1-based.</param>
    /// <param name="itemId">The item, when it has no variants. Exactly one of this and <paramref name="itemVariantId"/>.</param>
    /// <param name="itemVariantId">The variant. Exactly one of this and <paramref name="itemId"/>.</param>
    /// <param name="description">What the receipt says this was.</param>
    /// <param name="quantity">How much. Must be positive.</param>
    /// <param name="unitPrice">What one unit was sold for. May be zero; may not be negative.</param>
    /// <param name="discountAmount">A manual discount. May be zero; may not exceed the extended price.</param>
    /// <param name="taxCode">The tax code the amounts below were computed under.</param>
    /// <param name="net">The line excluding tax, from the tax rules engine.</param>
    /// <param name="tax">The tax, from the tax rules engine.</param>
    /// <param name="gross">Net plus tax.</param>
    /// <param name="id">
    /// The line's identity, or <c>null</c> to mint one here. §4.11: a terminal that rang this line up
    /// offline supplies the id it already used, so replaying <c>AddSaleLineCommand</c> after a dropped
    /// acknowledgement finds the line that already exists instead of appending a second one.
    /// </param>
    /// <exception cref="PosRuleException">A structural invariant was broken.</exception>
    public static SaleLine Ring(
        Guid tenantId,
        Guid? storeId,
        Guid saleId,
        int lineNumber,
        Guid? itemId,
        Guid? itemVariantId,
        string description,
        Quantity quantity,
        Money unitPrice,
        Money discountAmount,
        string taxCode,
        Money net,
        Money tax,
        Money gross,
        Guid? id = null)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A sale line must belong to a tenant.", nameof(tenantId));
        }

        if (saleId == Guid.Empty)
        {
            throw new ArgumentException("A sale line must belong to a sale.", nameof(saleId));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(lineNumber, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(taxCode);

        EnsureExactlyOneStockKeepingUnit(itemId, itemVariantId);

        if (quantity.Value <= 0m)
        {
            throw PosRuleException.MustBePositive("quantity");
        }

        if (unitPrice.IsNegative)
        {
            throw PosRuleException.MustBePositive("unit price");
        }

        if (discountAmount.IsNegative)
        {
            throw PosRuleException.MustBePositive("discount");
        }

        Money extended = unitPrice * quantity.Value;

        if (discountAmount.Amount > extended.Amount)
        {
            throw PosRuleException.DiscountExceedsLine(discountAmount, extended);
        }

        if ((net + tax).Amount != gross.Amount)
        {
            throw PosRuleException.LineDoesNotBalance(net, tax, gross);
        }

        return new SaleLine(
            id ?? UuidV7.NewGuid(),
            tenantId,
            storeId,
            saleId,
            lineNumber,
            itemId,
            itemVariantId,
            description.Trim(),
            quantity,
            unitPrice,
            discountAmount,
            taxCode.Trim().ToUpperInvariant(),
            net,
            tax,
            gross);
    }

    /// <summary>Takes the line off the sale, leaving it on the record.</summary>
    /// <param name="voidedAt">When, UTC.</param>
    /// <exception cref="PosRuleException">The line is already voided.</exception>
    public void Void(DateTimeOffset voidedAt)
    {
        if (IsVoided)
        {
            throw PosRuleException.LineAlreadyVoided();
        }

        IsVoided = true;
        VoidedAt = voidedAt;
    }

    /// <summary>Records that this line's stock was relieved.</summary>
    /// <param name="stockLedgerEntryId">The ledger entry that was posted.</param>
    public void RecordStockIssued(Guid stockLedgerEntryId)
    {
        StockIssue = StockIssueStatus.Posted;
        StockLedgerEntryId = stockLedgerEntryId;
        StockIssueNote = null;
    }

    /// <summary>
    /// Records that the ledger refused to relieve this line's stock, and why. The sale still stands —
    /// see ADR-073 and the class remarks.
    /// </summary>
    /// <param name="reason">The refusal, in words a manager reconciling it can act on.</param>
    public void RecordStockIssueRefused(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        StockIssue = StockIssueStatus.Refused;
        StockLedgerEntryId = null;
        StockIssueNote = reason.Length > 500 ? reason[..500] : reason;
    }

    /// <summary>Throws unless exactly one of an item or a variant is named.</summary>
    /// <param name="itemId">The item, or <c>null</c>.</param>
    /// <param name="itemVariantId">The variant, or <c>null</c>.</param>
    /// <exception cref="PosRuleException">Neither, or both, are set.</exception>
    /// <remarks>
    /// The same rule <c>inventory</c>'s <c>StockItemReference</c> enforces, restated here with a POS
    /// error code rather than shared: a till operator reading <c>INVENTORY_…</c> on a line they just
    /// scanned learns nothing about which of their two actions was wrong.
    /// </remarks>
    public static void EnsureExactlyOneStockKeepingUnit(Guid? itemId, Guid? itemVariantId)
    {
        bool hasItem = itemId is not null && itemId != Guid.Empty;
        bool hasVariant = itemVariantId is not null && itemVariantId != Guid.Empty;

        if (hasItem == hasVariant)
        {
            throw PosRuleException.ExactlyOneItemOrVariantRequired();
        }
    }
}

/// <summary>What happened when a <see cref="SaleLine"/>'s stock was relieved.</summary>
public enum StockIssueStatus
{
    /// <summary>Rung up but not yet issued — the sale has not completed.</summary>
    Pending = 0,

    /// <summary>Issued. <see cref="SaleLine.StockLedgerEntryId"/> names the ledger row.</summary>
    Posted = 1,

    /// <summary>
    /// The ledger refused it — almost always because the shelf and the system disagree. The sale
    /// stands; this is the queue a manager reconciles with a stocktake (ADR-073).
    /// </summary>
    Refused = 2,
}
