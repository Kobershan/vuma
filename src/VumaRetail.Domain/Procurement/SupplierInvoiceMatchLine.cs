using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Procurement;

/// <summary>
/// One line of the comparison: ordered against received against invoiced, with the two variances that
/// decide whether it passes.
/// </summary>
/// <remarks>
/// <para>
/// Every figure it compares is snapshotted onto the row. The order can be amended, further goods can
/// arrive and the tolerance policy can change, all after this comparison was run — a match that
/// recomputed itself on read would tell a different story every time somebody opened it, which is the
/// opposite of what an audit record is for.
/// </para>
/// <para>
/// <b>The price comparison is on the extended line, not the unit cost.</b> A one-cent unit difference
/// on 5,000 units is R50 and matters; the same one cent on 3 units does not. Comparing extended amounts
/// is also what makes the absolute floor meaningful — <c>PriceToleranceFloor</c> is a per-line rand
/// amount, and a floor compared against a unit cost would be a different rule at every pack size.
/// </para>
/// <para>
/// <b>Both quantity comparisons are cumulative.</b> <see cref="PreviouslyInvoicedQuantity"/> is what
/// earlier released matches already claimed against the same order line, which is what makes business
/// rule 11 hold across several invoices for one delivery rather than only within one of them. The same
/// shape Stage 10's returns used, for the same reason.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.Bidirectional, ConflictPolicy.CloudWins)]
public sealed class SupplierInvoiceMatchLine : Entity
{
    private SupplierInvoiceMatchLine(
        Guid tenantId,
        Guid? storeId,
        Guid supplierInvoiceMatchId,
        Guid? purchaseOrderLineId,
        Guid? itemId,
        Guid? itemVariantId,
        string description,
        Quantity orderedQuantity,
        Quantity receivedQuantity,
        Quantity invoicedQuantity,
        Quantity previouslyInvoicedQuantity,
        Money orderedUnitCost,
        Money invoicedUnitCost,
        Money orderedValue,
        Money invoicedValue,
        Money priceVariance,
        Quantity quantityVariance,
        ThreeWayMatchStatus status,
        ThreeWayMatchVarianceKind variances)
        : base(tenantId, storeId)
    {
        SupplierInvoiceMatchId = supplierInvoiceMatchId;
        PurchaseOrderLineId = purchaseOrderLineId;
        ItemId = itemId;
        ItemVariantId = itemVariantId;
        Description = description;
        OrderedQuantity = orderedQuantity;
        ReceivedQuantity = receivedQuantity;
        InvoicedQuantity = invoicedQuantity;
        PreviouslyInvoicedQuantity = previouslyInvoicedQuantity;
        OrderedUnitCost = orderedUnitCost;
        InvoicedUnitCost = invoicedUnitCost;
        OrderedValue = orderedValue;
        InvoicedValue = invoicedValue;
        PriceVariance = priceVariance;
        QuantityVariance = quantityVariance;
        Status = status;
        Variances = variances;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private SupplierInvoiceMatchLine()
    {
    }

    /// <summary>The match this line belongs to.</summary>
    public Guid SupplierInvoiceMatchId { get; private set; }

    /// <summary>The order line, or <c>null</c> when the supplier invoiced for something not on the order.</summary>
    public Guid? PurchaseOrderLineId { get; private set; }

    /// <summary>The item, when it has no variants.</summary>
    public Guid? ItemId { get; private set; }

    /// <summary>The variant.</summary>
    public Guid? ItemVariantId { get; private set; }

    /// <summary>What the line is, as the order or the supplier's invoice describes it.</summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>How much the order committed to.</summary>
    public Quantity OrderedQuantity { get; private set; }

    /// <summary>How much has actually been received against it.</summary>
    public Quantity ReceivedQuantity { get; private set; }

    /// <summary>How much this invoice claims.</summary>
    public Quantity InvoicedQuantity { get; private set; }

    /// <summary>How much earlier released matches already claimed against the same order line.</summary>
    public Quantity PreviouslyInvoicedQuantity { get; private set; }

    /// <summary>What the order says one costs.</summary>
    public Money OrderedUnitCost { get; private set; }

    /// <summary>What the supplier is charging per unit.</summary>
    public Money InvoicedUnitCost { get; private set; }

    /// <summary>The invoiced quantity at the <em>order's</em> cost — what the order supports paying.</summary>
    public Money OrderedValue { get; private set; }

    /// <summary>The invoiced quantity at the <em>supplier's</em> cost — what they are asking for.</summary>
    public Money InvoicedValue { get; private set; }

    /// <summary><see cref="InvoicedValue"/> less <see cref="OrderedValue"/>. Positive means overcharged.</summary>
    public Money PriceVariance { get; private set; }

    /// <summary>
    /// Cumulative invoiced less received. Positive means the supplier is billing for goods that have not
    /// arrived (business rule 11).
    /// </summary>
    public Quantity QuantityVariance { get; private set; }

    /// <summary>This line's verdict.</summary>
    public ThreeWayMatchStatus Status { get; private set; }

    /// <summary>Which comparisons this line failed.</summary>
    public ThreeWayMatchVarianceKind Variances { get; private set; }

    /// <summary>
    /// Runs the comparison for one line. Prefer <see cref="SupplierInvoiceMatch.AddLine"/>, which
    /// enforces the document's rules.
    /// </summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The store buying.</param>
    /// <param name="supplierInvoiceMatchId">The match.</param>
    /// <param name="orderLine">The order line being invoiced for.</param>
    /// <param name="invoicedQuantity">How much this invoice claims. Positive.</param>
    /// <param name="invoicedUnitCost">What the supplier charges per unit.</param>
    /// <param name="receivedQuantity">How much has been received against the order line.</param>
    /// <param name="previouslyInvoicedQuantity">How much earlier released matches claimed.</param>
    /// <param name="tolerancePercentage">The percentage tolerance on the extended price.</param>
    /// <param name="toleranceFloor">The absolute per-line floor.</param>
    /// <exception cref="ProcurementRuleException">The invoiced quantity or cost is malformed.</exception>
    public static SupplierInvoiceMatchLine Compare(
        Guid tenantId,
        Guid? storeId,
        Guid supplierInvoiceMatchId,
        PurchaseOrderLine orderLine,
        Quantity invoicedQuantity,
        Money invoicedUnitCost,
        Quantity receivedQuantity,
        Quantity previouslyInvoicedQuantity,
        decimal tolerancePercentage,
        Money toleranceFloor)
    {
        ArgumentNullException.ThrowIfNull(orderLine);

        ProcurementLineRules.RequirePositive(invoicedQuantity);
        ProcurementLineRules.RequireNonNegative(invoicedUnitCost);

        // Rounded once, on the extended amount, on both sides — the §4.14 lesson. Rounding the unit cost
        // first and multiplying would produce a variance made entirely of rounding on any line with a
        // fractional unit cost, and a supplier would be blocked over arithmetic nobody did.
        Money orderedValue = invoicedQuantity.Extend(orderLine.UnitCost).RoundToCurrencyScale();
        Money invoicedValue = invoicedQuantity.Extend(invoicedUnitCost).RoundToCurrencyScale();
        Money priceVariance = invoicedValue - orderedValue;

        Quantity cumulativeInvoiced = previouslyInvoicedQuantity + invoicedQuantity;
        Quantity quantityVariance = cumulativeInvoiced - receivedQuantity;

        ThreeWayMatchVarianceKind variances = ThreeWayMatchVarianceKind.None;
        ThreeWayMatchStatus status = ThreeWayMatchStatus.Matched;

        // Business rule 11 has no tolerance and is not a variance to be judged: goods that have not
        // arrived cannot be paid for, whatever the amount. An under-invoice is not a fault — a supplier
        // billing for less than they delivered is entitled to bill for the rest later.
        if (quantityVariance.Value > 0m)
        {
            variances |= ThreeWayMatchVarianceKind.Quantity;
            status = ThreeWayMatchStatus.Blocked;
        }

        if (!priceVariance.IsZero)
        {
            variances |= ThreeWayMatchVarianceKind.Price;

            decimal allowedByPercentage = Math.Abs(orderedValue.Amount) * (tolerancePercentage / 100m);
            decimal allowed = Math.Max(allowedByPercentage, toleranceFloor.Amount);

            // Either bound passing is enough. The percentage is the rule for a line worth having a rule
            // about; the floor is what stops a 2% tolerance blocking an invoice over eleven cents.
            bool withinTolerance = Math.Abs(priceVariance.Amount)
                <= decimal.Round(allowed, Money.Scale, Money.Rounding);

            if (withinTolerance)
            {
                if (status is ThreeWayMatchStatus.Matched)
                {
                    status = ThreeWayMatchStatus.MatchedWithinTolerance;
                }
            }
            else
            {
                status = ThreeWayMatchStatus.Blocked;
            }
        }

        return new SupplierInvoiceMatchLine(
            tenantId,
            storeId,
            supplierInvoiceMatchId,
            orderLine.Id,
            orderLine.ItemId,
            orderLine.ItemVariantId,
            orderLine.Description,
            orderLine.Quantity,
            receivedQuantity,
            invoicedQuantity,
            previouslyInvoicedQuantity,
            orderLine.UnitCost,
            invoicedUnitCost,
            orderedValue,
            invoicedValue,
            priceVariance,
            quantityVariance,
            status,
            variances);
    }

    /// <summary>
    /// Records a line the supplier invoiced for that the order does not have. Always blocked.
    /// </summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The store buying.</param>
    /// <param name="supplierInvoiceMatchId">The match.</param>
    /// <param name="description">What the supplier called it.</param>
    /// <param name="invoicedQuantity">How much they claim.</param>
    /// <param name="invoicedUnitCost">What they are charging.</param>
    public static SupplierInvoiceMatchLine Unknown(
        Guid tenantId,
        Guid? storeId,
        Guid supplierInvoiceMatchId,
        string description,
        Quantity invoicedQuantity,
        Money invoicedUnitCost)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        ProcurementLineRules.RequirePositive(invoicedQuantity);
        ProcurementLineRules.RequireNonNegative(invoicedUnitCost);

        Money invoicedValue = invoicedQuantity.Extend(invoicedUnitCost).RoundToCurrencyScale();
        Money zero = Money.Zero(invoicedUnitCost.Currency);
        Quantity zeroQuantity = Quantity.Zero(invoicedQuantity.UnitOfMeasure);

        // OrderedValue is zero, so the whole claim is variance — which is exactly right: the order
        // supports paying nothing for a line it does not have.
        return new SupplierInvoiceMatchLine(
            tenantId,
            storeId,
            supplierInvoiceMatchId,
            purchaseOrderLineId: null,
            itemId: null,
            itemVariantId: null,
            description.Trim(),
            zeroQuantity,
            zeroQuantity,
            invoicedQuantity,
            zeroQuantity,
            zero,
            invoicedUnitCost,
            zero,
            invoicedValue,
            invoicedValue,
            invoicedQuantity,
            ThreeWayMatchStatus.Blocked,
            ThreeWayMatchVarianceKind.UnknownLine | ThreeWayMatchVarianceKind.Quantity);
    }
}
