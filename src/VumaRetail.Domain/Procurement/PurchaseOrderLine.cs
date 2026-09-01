using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Procurement;

/// <summary>One thing the shop has committed to buy, at a stated cost.</summary>
/// <remarks>
/// <para>
/// <b>Tax is computed once, here, at authoring time</b> (ADR-075). The caller resolves it through
/// <c>ITaxCalculator</c> and hands the three amounts in; nothing downstream recomputes an order's tax
/// from its total. A rate change between order and invoice must not restate what was ordered, and the
/// three-way match compares the supplier's claim against these stored figures rather than against
/// today's rules.
/// </para>
/// <para>
/// <see cref="ReceivedQuantity"/> and <see cref="InvoicedQuantity"/> are the running totals business
/// rules 6 and 11 are checked against. They are maintained rather than derived: two receipts racing
/// against the same line each reading a recomputed sum would both see the low figure and both pass.
/// </para>
/// <para>
/// The over-receipt tolerance is a <em>parameter</em> to <see cref="RecordReceived"/>, not a field on
/// this line. It is tenant policy, it can change between two deliveries against the same order, and a
/// line that stored its own copy would enforce whatever the policy was on the day it was authored —
/// which is the wrong answer in both directions.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.Bidirectional, ConflictPolicy.CloudWins)]
public sealed class PurchaseOrderLine : Entity
{
    private PurchaseOrderLine(
        Guid tenantId,
        Guid? storeId,
        Guid purchaseOrderId,
        Guid? itemId,
        Guid? itemVariantId,
        string description,
        Quantity quantity,
        Money unitCost,
        string taxCode,
        Money net,
        Money tax,
        Money gross,
        Guid? purchaseRequisitionLineId)
        : base(tenantId, storeId)
    {
        PurchaseOrderId = purchaseOrderId;
        ItemId = itemId;
        ItemVariantId = itemVariantId;
        Description = description;
        Quantity = quantity;
        UnitCost = unitCost;
        TaxCode = taxCode;
        Net = net;
        Tax = tax;
        Gross = gross;
        PurchaseRequisitionLineId = purchaseRequisitionLineId;
        ReceivedQuantity = Quantity.Zero(quantity.UnitOfMeasure);
        RejectedQuantity = Quantity.Zero(quantity.UnitOfMeasure);
        InvoicedQuantity = Quantity.Zero(quantity.UnitOfMeasure);
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private PurchaseOrderLine()
    {
    }

    /// <summary>The order this line belongs to.</summary>
    public Guid PurchaseOrderId { get; private set; }

    /// <summary>The item, when it has no variants.</summary>
    public Guid? ItemId { get; private set; }

    /// <summary>The variant.</summary>
    public Guid? ItemVariantId { get; private set; }

    /// <summary>What is being bought, as it appears on the order the supplier receives.</summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>How much was ordered. Always positive.</summary>
    public Quantity Quantity { get; private set; }

    /// <summary>What one costs, in the order's currency.</summary>
    public Money UnitCost { get; private set; }

    /// <summary>The tax code this line is bought under.</summary>
    public string TaxCode { get; private set; } = string.Empty;

    /// <summary>The line excluding tax.</summary>
    public Money Net { get; private set; }

    /// <summary>The tax on the line, computed once at authoring time (ADR-075).</summary>
    public Money Tax { get; private set; }

    /// <summary>What the line commits — <see cref="Net"/> plus <see cref="Tax"/>.</summary>
    public Money Gross { get; private set; }

    /// <summary>How much has actually arrived and been accepted, cumulatively.</summary>
    public Quantity ReceivedQuantity { get; private set; }

    /// <summary>How much arrived and was turned away, cumulatively. Never entered stock.</summary>
    public Quantity RejectedQuantity { get; private set; }

    /// <summary>How much has been invoiced by the supplier on released matches, cumulatively.</summary>
    public Quantity InvoicedQuantity { get; private set; }

    /// <summary>The requisition line this satisfies, or <c>null</c>.</summary>
    public Guid? PurchaseRequisitionLineId { get; private set; }

    /// <summary>
    /// What one unit costs <em>excluding tax</em> — the cost basis stock is valued at (business rule 8,
    /// ADR-086).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not <see cref="UnitCost"/>, and the difference is the whole of ADR-086. A South African supplier
    /// quotes VAT-inclusive (<c>CLAUDE.md</c> §9), so <see cref="UnitCost"/> on a typical order is the
    /// gross figure — and valuing stock at it capitalises recoverable input VAT into inventory, which
    /// overstates the balance sheet by the VAT rate and overstates cost of sales when the goods sell.
    /// It also leaves the goods-received-not-invoiced account permanently short: the receipt credits it
    /// gross while the three-way match debits it net, and the difference never clears.
    /// </para>
    /// <para>
    /// Derived from the line's stored <see cref="Net"/> rather than recomputed through the tax engine,
    /// because ADR-075 says nothing downstream recomputes an order's tax — the net was settled once, at
    /// authoring time, and this is that number divided by what was bought. A tenant who is not VAT
    /// registered has a rule that computes zero tax, so net equals gross and this changes nothing for
    /// them.
    /// </para>
    /// </remarks>
    public Money NetUnitCost => Net / Quantity.Value;

    /// <summary>How much is still to come.</summary>
    public Quantity OutstandingQuantity
        => Quantity.Value <= ReceivedQuantity.Value
            ? Quantity.Zero(Quantity.UnitOfMeasure)
            : Quantity - ReceivedQuantity;

    /// <summary>True once everything ordered has arrived.</summary>
    public bool IsFullyReceived => ReceivedQuantity >= Quantity;

    /// <summary>
    /// Builds an order line. Prefer <see cref="PurchaseOrder.AddLine"/>, which enforces the document's
    /// currency and status rules.
    /// </summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The store buying.</param>
    /// <param name="purchaseOrderId">The order.</param>
    /// <param name="itemId">The item, when it has no variants.</param>
    /// <param name="itemVariantId">The variant.</param>
    /// <param name="description">What is being bought.</param>
    /// <param name="quantity">How much. Positive.</param>
    /// <param name="unitCost">What one costs.</param>
    /// <param name="taxCode">The tax code.</param>
    /// <param name="net">The line excluding tax.</param>
    /// <param name="tax">The tax on the line.</param>
    /// <param name="purchaseRequisitionLineId">The requisition line this satisfies, or <c>null</c>.</param>
    /// <exception cref="ProcurementRuleException">
    /// The line names the wrong number of SKUs, the quantity is not positive, or a cost is negative.
    /// </exception>
    public static PurchaseOrderLine For(
        Guid tenantId,
        Guid? storeId,
        Guid purchaseOrderId,
        Guid? itemId,
        Guid? itemVariantId,
        string description,
        Quantity quantity,
        Money unitCost,
        string taxCode,
        Money net,
        Money tax,
        Guid? purchaseRequisitionLineId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(taxCode);

        ProcurementLineRules.RequireExactlyOneStockKeepingUnit(itemId, itemVariantId);
        ProcurementLineRules.RequirePositive(quantity);
        ProcurementLineRules.RequireNonNegative(unitCost);
        ProcurementLineRules.RequireNonNegative(net);
        ProcurementLineRules.RequireNonNegative(tax);

        return new PurchaseOrderLine(
            tenantId, storeId, purchaseOrderId, itemId, itemVariantId, description.Trim(), quantity,
            unitCost, taxCode.Trim().ToUpperInvariant(), net, tax, net + tax, purchaseRequisitionLineId);
    }

    /// <summary>
    /// Adds an accepted delivery to the running received total, refusing anything past the tolerance.
    /// </summary>
    /// <param name="quantity">The accepted quantity. Positive.</param>
    /// <param name="tolerancePercentage">
    /// How far past the ordered quantity the cumulative total may go, as a percentage. Zero means none.
    /// </param>
    /// <exception cref="ProcurementRuleException">
    /// The quantity is not positive, or the cumulative total would exceed what was ordered beyond
    /// tolerance.
    /// </exception>
    internal void RecordReceived(Quantity quantity, decimal tolerancePercentage)
    {
        ProcurementLineRules.RequirePositive(quantity);

        if (tolerancePercentage is < 0m or > 100m)
        {
            throw ProcurementRuleException.ToleranceOutOfRange();
        }

        decimal ceiling = Quantity.Value * (1m + (tolerancePercentage / 100m));
        decimal proposed = ReceivedQuantity.Value + quantity.Value;

        // Compared at Quantity's own scale rather than raw. Without the round, a 5% tolerance on 3 units
        // gives a ceiling of 3.1499999... in decimal arithmetic and a delivery of exactly 3.15 is refused
        // for a reason nobody on a loading dock could ever discover.
        if (decimal.Round(proposed, Primitives.Quantity.Scale, MidpointRounding.AwayFromZero)
            > decimal.Round(ceiling, Primitives.Quantity.Scale, MidpointRounding.AwayFromZero))
        {
            throw ProcurementRuleException.ReceiptExceedsOrderedQuantity(
                Quantity.Value, ReceivedQuantity.Value, quantity.Value, tolerancePercentage);
        }

        ReceivedQuantity += quantity;
    }

    /// <summary>Adds a turned-away quantity to the running rejected total. Nothing entered stock.</summary>
    /// <param name="quantity">The rejected quantity. Positive.</param>
    internal void RecordRejected(Quantity quantity)
    {
        ProcurementLineRules.RequirePositive(quantity);

        RejectedQuantity += quantity;
    }

    /// <summary>Adds an invoiced quantity to the running total, once a match has been released.</summary>
    /// <param name="quantity">The invoiced quantity. Positive.</param>
    internal void RecordInvoiced(Quantity quantity)
    {
        ProcurementLineRules.RequirePositive(quantity);

        InvoicedQuantity += quantity;
    }
}
