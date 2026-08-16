using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Pos;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Sales;

/// <summary>
/// One line coming back — a quantity off one original <see cref="SaleLine"/>, refunded at what was
/// actually charged for it.
/// </summary>
/// <remarks>
/// <para>
/// <b>ADR-075 lives here.</b> The tax on this line is <em>derived from the tax stored on the original
/// line</em>, pro-rata to the quantity coming back — never recomputed by putting the refund through
/// today's tax rules. If it were recomputed, a rate change between the sale and the return would refund
/// a different amount of VAT than was declared on it, and the shop's output tax would stop
/// reconciling to its own receipts. That is the failure mode the rule exists to prevent, and it is why
/// this class takes no <c>ITaxCalculator</c>.
/// </para>
/// <para>
/// <see cref="OriginalQuantity"/> and <see cref="PreviouslyReturnedQuantity"/> are snapshots, and they
/// are the reason business rule 5 is a database guarantee and not only an aggregate one:
/// <c>ck_sales_return_lines_within_quantity_sold</c> asserts
/// <c>quantity + previously_returned &lt;= original_quantity</c> on the row itself. A row written around
/// the aggregate — by a bulk import, a repair script, a future bug — still cannot claim more than was
/// sold. What the constraint cannot settle is two returns raced against the same line at the same
/// instant, because each one's snapshot is honest at the moment it is taken; that case is settled by
/// the aggregate reading the committed sum inside the command's transaction.
/// </para>
/// <para>
/// <see cref="StockReturn"/> is ADR-073's shape, restated for goods moving the other way: the customer
/// is standing at the counter and the item is already back over it, so a ledger that refuses the
/// receipt must not fail the refund. The refusal is recorded on the row a manager can query.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class SalesReturnLine : Entity
{
    private SalesReturnLine(
        Guid tenantId,
        Guid? storeId,
        Guid salesReturnId,
        Guid saleLineId,
        Guid? itemId,
        Guid? itemVariantId,
        string description,
        Quantity quantity,
        Quantity originalQuantity,
        decimal previouslyReturnedQuantity,
        Money unitPrice,
        string taxCode,
        Money net,
        Money tax,
        Money gross,
        Guid? originalStockLedgerEntryId)
        : base(tenantId, storeId)
    {
        SalesReturnId = salesReturnId;
        SaleLineId = saleLineId;
        OriginalStockLedgerEntryId = originalStockLedgerEntryId;
        ItemId = itemId;
        ItemVariantId = itemVariantId;
        Description = description;
        Quantity = quantity;
        OriginalQuantity = originalQuantity;
        PreviouslyReturnedQuantity = previouslyReturnedQuantity;
        UnitPrice = unitPrice;
        TaxCode = taxCode;
        Net = net;
        Tax = tax;
        Gross = gross;
        StockReturn = StockReturnStatus.Pending;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private SalesReturnLine()
    {
    }

    /// <summary>The return document this line belongs to.</summary>
    public Guid SalesReturnId { get; private set; }

    /// <summary>The original <see cref="SaleLine"/> this is a return of.</summary>
    public Guid SaleLineId { get; private set; }

    /// <summary>The item returned, when it has no variants.</summary>
    public Guid? ItemId { get; private set; }

    /// <summary>The variant returned.</summary>
    public Guid? ItemVariantId { get; private set; }

    /// <summary>What the original receipt called this. Copied, not looked up — the same reason Stage 09 snapshotted it.</summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>How much is coming back. Always positive.</summary>
    public Quantity Quantity { get; private set; }

    /// <summary>How much the original line sold. Snapshotted so the database can check the rule.</summary>
    public Quantity OriginalQuantity { get; private set; }

    /// <summary>How much had already come back on earlier returns when this line was raised.</summary>
    public decimal PreviouslyReturnedQuantity { get; private set; }

    /// <summary>What one unit was actually charged at — the original line's price, not today's (business rule 6).</summary>
    public Money UnitPrice { get; private set; }

    /// <summary>The tax code the original line was rung up under.</summary>
    public string TaxCode { get; private set; } = string.Empty;

    /// <summary>The refund excluding tax.</summary>
    public Money Net { get; private set; }

    /// <summary>The tax coming back, pro-rata off the original line's stored tax (ADR-075).</summary>
    public Money Tax { get; private set; }

    /// <summary>What the customer gets back for this line — <see cref="Net"/> plus <see cref="Tax"/>.</summary>
    public Money Gross { get; private set; }

    /// <summary>
    /// The <c>inventory.stock_ledger_entries</c> row the <em>original sale line</em> produced, or
    /// <c>null</c> when its issue was refused.
    /// </summary>
    /// <remarks>
    /// Snapshotted at the moment the return line is raised, because it is what the goods must be
    /// received back at: the cost is on the issue entry, not on the sale line, and looking it up later
    /// through a sale that has since been reloaded is a second chance to get the wrong one. A
    /// <c>null</c> here is not a fault — it means the original sale completed without relieving stock
    /// (ADR-073), and the return has no cost basis to receive against.
    /// </remarks>
    public Guid? OriginalStockLedgerEntryId { get; private set; }

    /// <summary>What happened when this line's stock was put back.</summary>
    public StockReturnStatus StockReturn { get; private set; }

    /// <summary>The <c>inventory.stock_ledger_entries</c> row this line produced, when it produced one.</summary>
    public Guid? StockLedgerEntryId { get; private set; }

    /// <summary>Why the stock receipt was refused, when it was.</summary>
    public string? StockReturnNote { get; private set; }

    /// <summary>
    /// Builds a return line from the original sale line, pro-rating its stored money to the quantity
    /// coming back. Prefer <see cref="SalesReturn.AddLine"/>, which enforces the document's rules.
    /// </summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The store the return happened at.</param>
    /// <param name="salesReturnId">The return document.</param>
    /// <param name="soldLine">The original line.</param>
    /// <param name="quantity">How much is coming back.</param>
    /// <param name="previouslyReturnedQuantity">How much had already come back on earlier returns.</param>
    /// <exception cref="SalesRuleException">The quantity is not positive, or exceeds what was sold.</exception>
    public static SalesReturnLine ForSoldLine(
        Guid tenantId,
        Guid? storeId,
        Guid salesReturnId,
        SaleLine soldLine,
        Quantity quantity,
        decimal previouslyReturnedQuantity)
    {
        ArgumentNullException.ThrowIfNull(soldLine);

        if (quantity.Value <= 0m)
        {
            throw SalesRuleException.QuantityMustBePositive();
        }

        if (previouslyReturnedQuantity + quantity.Value > soldLine.Quantity.Value)
        {
            throw SalesRuleException.ReturnExceedsQuantitySold(
                soldLine.Quantity.Value, previouslyReturnedQuantity, quantity.Value);
        }

        // Cumulative pro-rata, not per-return pro-rata. Each amount is the rounded share of everything
        // returned *so far* less the rounded share of everything returned *before* this line — so the
        // partial refunds telescope and their sum is exactly the original line, whatever the quantities
        // were split into.
        //
        // Rounding each return independently does not have that property, and the failure is a refund
        // of money the shop never took: half of a R60.00 line whose tax is R7.83 rounds to R26.09 net
        // plus R3.92 tax = R30.01, so returning both halves refunds R60.02. Business rule 5 bounds the
        // *quantity* that can come back and would not have caught it.
        decimal soldQuantity = soldLine.Quantity.Value;
        decimal fractionBefore = previouslyReturnedQuantity / soldQuantity;
        decimal fractionAfter = (previouslyReturnedQuantity + quantity.Value) / soldQuantity;

        Money gross = Share(soldLine.Gross, fractionAfter) - Share(soldLine.Gross, fractionBefore);
        Money tax = Share(soldLine.Tax, fractionAfter) - Share(soldLine.Tax, fractionBefore);

        // Net is the remainder rather than a third independent rounding, so the line satisfies
        // net + tax = gross exactly — the invariant SaleLine.Ring asserts and
        // ck_sales_return_lines_balances re-asserts at the boundary.
        Money net = gross - tax;

        return new SalesReturnLine(
            tenantId,
            storeId,
            salesReturnId,
            soldLine.Id,
            soldLine.ItemId,
            soldLine.ItemVariantId,
            soldLine.Description,
            quantity,
            soldLine.Quantity,
            previouslyReturnedQuantity,
            soldLine.UnitPrice,
            soldLine.TaxCode,
            net,
            tax,
            gross,
            soldLine.StockLedgerEntryId);
    }

    /// <summary>
    /// The rounded share of an original amount for a cumulative fraction of the line, at full
    /// precision until the single rounding at the end (business rule 2).
    /// </summary>
    private static Money Share(Money original, decimal fraction)
        => (original * fraction).RoundToCurrencyScale();

    /// <summary>Records that this line's stock went back on the shelf.</summary>
    /// <param name="stockLedgerEntryId">The ledger entry that was posted.</param>
    public void RecordStockReturned(Guid stockLedgerEntryId)
    {
        StockReturn = StockReturnStatus.Posted;
        StockLedgerEntryId = stockLedgerEntryId;
        StockReturnNote = null;
    }

    /// <summary>
    /// Records that the ledger refused to take this line's stock back, and why. The refund still
    /// stands — see the class remarks and ADR-073.
    /// </summary>
    /// <param name="reason">The refusal, in words a manager reconciling it can act on.</param>
    public void RecordStockReturnRefused(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        StockReturn = StockReturnStatus.Refused;
        StockLedgerEntryId = null;
        StockReturnNote = reason.Length > 500 ? reason[..500] : reason;
    }
}

/// <summary>What happened when a <see cref="SalesReturnLine"/>'s stock was put back.</summary>
/// <remarks>
/// The mirror of <c>Pos.StockIssueStatus</c>, declared separately rather than shared. The two are the
/// same three states about opposite movements, and a shared enum called <c>StockIssueStatus</c> on a
/// return line would read as the wrong direction to everyone who came after.
/// </remarks>
public enum StockReturnStatus
{
    /// <summary>On the document but not yet received — the return has not completed.</summary>
    Pending = 0,

    /// <summary>Back on the shelf. <see cref="SalesReturnLine.StockLedgerEntryId"/> names the ledger row.</summary>
    Posted = 1,

    /// <summary>
    /// The ledger refused it. The refund stands and the goods are physically back; this row is the
    /// queue a manager reconciles (ADR-073).
    /// </summary>
    Refused = 2,
}
