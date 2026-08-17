using VumaRetail.Application.Abstractions.Procurement;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Procurement;

namespace VumaRetail.Application.Procurement;

/// <summary>One line of a supplier's invoice, as it arrived off their document.</summary>
/// <param name="PurchaseOrderLineId">
/// The order line the supplier says this is for, or <c>null</c> when they have invoiced for something
/// that is not on the order at all.
/// </param>
/// <param name="Description">What the supplier called it. Used when there is no order line to name it.</param>
/// <param name="Quantity">How much they are claiming.</param>
/// <param name="UnitCost">What they are charging per unit.</param>
public sealed record SupplierInvoiceLine(
    Guid? PurchaseOrderLineId, string Description, Quantity Quantity, Money UnitCost);

/// <summary>
/// Compares what was ordered against what arrived against what the supplier is charging for, and
/// produces the match document that says whether anybody may pay it.
/// </summary>
/// <remarks>
/// <para>
/// A service rather than logic in the handler, for <c>CONVENTIONS.md</c> §4's reason. Two callers
/// already need it — the match command and the seed — and Stage 21b will need a third when a connected
/// supplier's invoice arrives over the network rather than off a piece of paper. More importantly it is
/// the piece of this stage most worth testing in isolation: every interesting case is a combination of
/// three numbers, and a pure function over three numbers can be tested exhaustively where a handler
/// cannot.
/// </para>
/// <para>
/// <b>It reads; it does not write.</b> The engine builds the document and hands it back. Whether the
/// document is stored, and whether anybody releases it, is the caller's — which is what makes a dry-run
/// preview of a match possible without a transaction.
/// </para>
/// </remarks>
public interface IThreeWayMatchEngine
{
    /// <summary>Runs the comparison and returns the resulting match document.</summary>
    /// <param name="order">The order, with its lines loaded.</param>
    /// <param name="supplierInvoiceNumber">The supplier's own invoice number.</param>
    /// <param name="invoiceDate">The date on their invoice.</param>
    /// <param name="claimedNet">What they are claiming, excluding tax.</param>
    /// <param name="claimedTax">The tax they are claiming.</param>
    /// <param name="lines">Their invoice's lines.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The match, whatever its verdict — including a blocked one (ADR-082).</returns>
    Task<SupplierInvoiceMatch> MatchAsync(
        PurchaseOrder order,
        string supplierInvoiceNumber,
        DateOnly invoiceDate,
        Money claimedNet,
        Money claimedTax,
        IReadOnlyCollection<SupplierInvoiceLine> lines,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IThreeWayMatchEngine" />
/// <param name="receipts">What actually arrived — the middle leg of the three.</param>
/// <param name="matches">What earlier released matches already claimed.</param>
/// <param name="options">The tenant's tolerances, registered as a singleton exactly as <c>ImportOptions</c> is.</param>
/// <param name="clock">The only source of time.</param>
public sealed class ThreeWayMatchEngine(
    IGoodsReceiptRepository receipts,
    ISupplierInvoiceMatchRepository matches,
    ProcurementOptions options,
    Abstractions.IClock clock) : IThreeWayMatchEngine
{
    /// <inheritdoc />
    public async Task<SupplierInvoiceMatch> MatchAsync(
        PurchaseOrder order,
        string supplierInvoiceNumber,
        DateOnly invoiceDate,
        Money claimedNet,
        Money claimedTax,
        IReadOnlyCollection<SupplierInvoiceLine> lines,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(lines);

        if (lines.Count == 0)
        {
            throw ProcurementRuleException.MatchHasNoLines();
        }

        SupplierInvoiceMatch match = SupplierInvoiceMatch.Open(
            order,
            supplierInvoiceNumber,
            invoiceDate,
            claimedNet,
            claimedTax,
            options.PriceTolerancePercentage,
            ResolveToleranceFloor(options, order.Currency),
            clock.UtcNow);

        // The received leg, read once for the whole document rather than per line. Two invoice lines
        // against the same order line are refused by the aggregate anyway, but reading per line would
        // also mean N queries for an invoice that is usually one delivery.
        IReadOnlyDictionary<Guid, Quantity> received =
            await SumReceivedAsync(order, cancellationToken).ConfigureAwait(false);

        foreach (SupplierInvoiceLine line in lines)
        {
            await AddLineAsync(match, order, line, received, cancellationToken).ConfigureAwait(false);
        }

        return match;
    }

    /// <summary>
    /// The floor, in the order's own currency, or zero when the configured floor is denominated in
    /// something else.
    /// </summary>
    /// <remarks>
    /// Dropping it rather than converting it is the conservative answer and the honest one: a rand floor
    /// applied to a dollar order would be roughly eighteen times too generous, and converting needs a
    /// rate and a rate date, which is Finance's job (<c>Money</c>'s own remarks). With the floor at zero
    /// the percentage still applies, so a foreign-currency order is matched more strictly rather than
    /// not at all.
    /// </remarks>
    private static Money ResolveToleranceFloor(ProcurementOptions settings, string currency)
        => settings.FloorFor(currency) ?? Money.Zero(currency);

    private async Task AddLineAsync(
        SupplierInvoiceMatch match,
        PurchaseOrder order,
        SupplierInvoiceLine line,
        IReadOnlyDictionary<Guid, Quantity> received,
        CancellationToken cancellationToken)
    {
        PurchaseOrderLine? orderLine = line.PurchaseOrderLineId is { } lineId
            ? order.Lines.FirstOrDefault(candidate => candidate.Id == lineId)
            : null;

        if (orderLine is null)
        {
            // Not an exception. An invoice line the order does not have is the single most interesting
            // thing an invoice can contain, and throwing would leave the only trace in whoever tried to
            // enter it. The document blocks and the row says why.
            match.AddUnknownLine(line.Description, line.Quantity, line.UnitCost);

            return;
        }

        decimal previouslyInvoiced = await matches
            .SumInvoicedQuantityAsync(orderLine.Id, match.Id, cancellationToken)
            .ConfigureAwait(false);

        Quantity receivedQuantity = received.TryGetValue(orderLine.Id, out Quantity quantity)
            ? quantity
            : Quantity.Zero(orderLine.Quantity.UnitOfMeasure);

        match.AddLine(
            orderLine,
            line.Quantity,
            line.UnitCost,
            receivedQuantity,
            new Quantity(previouslyInvoiced, orderLine.Quantity.UnitOfMeasure));
    }

    /// <summary>
    /// How much has actually been accepted against each of the order's lines, across every completed
    /// receipt.
    /// </summary>
    /// <remarks>
    /// Summed from the receipts rather than read off <c>PurchaseOrderLine.ReceivedQuantity</c>, and the
    /// difference matters exactly once: a receipt whose stock posting the ledger refused (ADR-073) still
    /// arrived, and the supplier is entitled to be paid for it. Both figures agree in the normal case;
    /// where they do not, the goods on the dock are the truth.
    /// </remarks>
    private async Task<IReadOnlyDictionary<Guid, Quantity>> SumReceivedAsync(
        PurchaseOrder order, CancellationToken cancellationToken)
    {
        IReadOnlyList<GoodsReceipt> completed = await receipts
            .ListForOrderAsync(order.Id, cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, Quantity> totals = [];

        foreach (GoodsReceipt receipt in completed.Where(candidate
            => candidate.Status is GoodsReceiptStatus.Completed))
        {
            foreach (GoodsReceiptLine line in receipt.Lines)
            {
                totals[line.PurchaseOrderLineId] =
                    totals.TryGetValue(line.PurchaseOrderLineId, out Quantity running)
                        ? running + line.AcceptedQuantity
                        : line.AcceptedQuantity;
            }
        }

        return totals;
    }
}
