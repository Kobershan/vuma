using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Identity;
using VumaRetail.Application.Platform;
using VumaRetail.Domain.Identity;
using VumaRetail.Domain.Platform;
using VumaRetail.Domain.Pos;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.Pos.Queries;

/// <summary>One sale, with its lines and tenders.</summary>
/// <param name="SaleId">The sale.</param>
public sealed record GetSaleQuery(Guid SaleId) : IQuery<Sale>;

/// <summary>Reads a sale.</summary>
/// <param name="sales">Sale lookup.</param>
public sealed class GetSaleQueryHandler(ISaleRepository sales) : IQueryHandler<GetSaleQuery, Sale>
{
    /// <inheritdoc />
    public async Task<Sale> HandleAsync(GetSaleQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await sales.FindAsync(query.SaleId, cancellationToken).ConfigureAwait(false)
            ?? throw new PosNotFoundException("sale", query.SaleId);
    }
}

/// <summary>Every sale set aside at a terminal.</summary>
/// <param name="TerminalId">The terminal.</param>
public sealed record ListParkedSalesQuery(Guid TerminalId) : IQuery<IReadOnlyList<Sale>>;

/// <summary>Reads the parked sales.</summary>
/// <param name="sales">Sale lookup.</param>
public sealed class ListParkedSalesQueryHandler(ISaleRepository sales)
    : IQueryHandler<ListParkedSalesQuery, IReadOnlyList<Sale>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Sale>> HandleAsync(
        ListParkedSalesQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await sales.ListParkedAsync(query.TerminalId, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Builds the receipt for a sale.</summary>
/// <param name="SaleId">The sale.</param>
/// <remarks>
/// A query, not a command: building the document changes nothing. Recording that a copy came out of a
/// printer is <c>RecordReceiptPrintCommand</c>, and the two are separate so that previewing a receipt
/// on screen does not enter the reprint log.
/// </remarks>
public sealed record BuildReceiptQuery(Guid SaleId) : IQuery<ReceiptDocument>;

/// <summary>
/// Projects a sale, its store and its tenant into the slip a printer or a screen renders.
/// </summary>
/// <param name="sales">Sale lookup.</param>
/// <param name="stores">The store's trading name and address.</param>
/// <param name="tenants">The tenant's VAT registration number, which a tax invoice must carry.</param>
/// <param name="users">The operator's display name for the "served by" line.</param>
/// <param name="prints">How many times this receipt has already been printed.</param>
public sealed class BuildReceiptQueryHandler(
    ISaleRepository sales,
    IStoreRepository stores,
    ITenantRepository tenants,
    IUserRepository users,
    IReceiptPrintRepository prints) : IQueryHandler<BuildReceiptQuery, ReceiptDocument>
{
    /// <inheritdoc />
    public async Task<ReceiptDocument> HandleAsync(
        BuildReceiptQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        Sale sale = await sales.FindAsync(query.SaleId, cancellationToken).ConfigureAwait(false)
            ?? throw new PosNotFoundException("sale", query.SaleId);

        Store? store = sale.StoreId is { } storeId
            ? await stores.FindAsync(storeId, cancellationToken).ConfigureAwait(false)
            : null;

        Tenant? tenant = await tenants.FindAsync(sale.TenantId, cancellationToken).ConfigureAwait(false);
        User? operatorUser = await users.FindAsync(sale.OperatorUserId, cancellationToken).ConfigureAwait(false);

        int alreadyPrinted = await prints.CountForSaleAsync(sale.Id, cancellationToken).ConfigureAwait(false);

        List<ReceiptLine> lines =
        [
            .. sale.LiveLines
                .OrderBy(line => line.LineNumber)
                .Select(line => new ReceiptLine(
                    line.Description,
                    line.Quantity,
                    line.UnitPrice,
                    line.DiscountAmount,
                    line.Gross,
                    line.TaxCode)),
        ];

        // Grouped per rate because a VAT invoice must break the tax out that way — a single "VAT" total
        // is not a compliant slip once a tenant sells anything zero-rated alongside standard-rated.
        List<ReceiptTaxLine> taxLines =
        [
            .. sale.LiveLines
                .GroupBy(line => line.TaxCode, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new ReceiptTaxLine(
                    group.Key,
                    group.Aggregate(Money.Zero(sale.Currency), (running, line) => running + line.Net),
                    group.Aggregate(Money.Zero(sale.Currency), (running, line) => running + line.Tax))),
        ];

        return new ReceiptDocument(
            sale.SaleNumber,
            store?.Name ?? tenant?.TradingName ?? "Vuma Retail",
            FormatAddress(store?.Address),
            tenant?.TaxRegistrationNumber,
            sale.CompletedAt ?? sale.OpenedAt,
            sale.TerminalId,
            operatorUser?.DisplayName,
            lines,
            taxLines,
            sale.Net,
            sale.Tax,
            sale.Gross,
            [.. sale.Tenders.Select(tender => new ReceiptTender(tender.Type, tender.Amount, tender.Reference))],
            sale.ChangeGiven,
            alreadyPrinted > 0,
            Footer: null);
    }

    private static string? FormatAddress(Address? address)
    {
        if (address is null)
        {
            return null;
        }

        string[] parts =
        [
            address.Line1,
            address.Line2 ?? string.Empty,
            address.City,
            address.Region ?? string.Empty,
            address.PostalCode ?? string.Empty,
        ];

        return string.Join(", ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }
}

/// <summary>A till session and the cash position derived from its sales.</summary>
/// <param name="Session">The session.</param>
/// <param name="ExpectedCash">
/// What the drawer should hold right now — float plus cash taken less change given. Derived on every
/// read, so a supervisor can take a mid-shift reading without closing the session.
/// </param>
/// <param name="SalesCompleted">How many sales have completed on this session.</param>
/// <param name="SalesUnfinished">How many are still open or parked — the count that blocks a close.</param>
public sealed record TillSessionView(
    TillSession Session,
    Money ExpectedCash,
    int SalesCompleted,
    int SalesUnfinished);

/// <summary>One till session and its derived cash position.</summary>
/// <param name="TillSessionId">The session.</param>
public sealed record GetTillSessionQuery(Guid TillSessionId) : IQuery<TillSessionView>;

/// <summary>Reads a session and derives its cash position from its own sales.</summary>
/// <param name="sessions">Session lookup.</param>
/// <param name="sales">The session's sales.</param>
public sealed class GetTillSessionQueryHandler(ITillSessionRepository sessions, ISaleRepository sales)
    : IQueryHandler<GetTillSessionQuery, TillSessionView>
{
    /// <inheritdoc />
    public async Task<TillSessionView> HandleAsync(
        GetTillSessionQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        TillSession session = await sessions.FindAsync(query.TillSessionId, cancellationToken).ConfigureAwait(false)
            ?? throw new PosNotFoundException("till session", query.TillSessionId);

        IReadOnlyList<Sale> sessionSales = await sales
            .ListForSessionAsync(session.Id, cancellationToken)
            .ConfigureAwait(false);

        Money cashTaken = sessionSales.Aggregate(
            Money.Zero(session.Currency),
            (running, sale) => running + sale.CashContribution);

        return new TillSessionView(
            session,
            session.OpeningFloat + cashTaken,
            sessionSales.Count(sale => sale.Status is SaleStatus.Completed),
            sessionSales.Count(sale => sale.Status is SaleStatus.Open or SaleStatus.Parked));
    }
}

/// <summary>Recent till sessions, newest first.</summary>
/// <param name="TerminalId">One terminal, or <c>null</c> for every terminal in the tenant.</param>
/// <param name="Limit">How many to return.</param>
public sealed record ListTillSessionsQuery(Guid? TerminalId, int Limit = 50) : IQuery<IReadOnlyList<TillSession>>;

/// <summary>Reads recent sessions.</summary>
/// <param name="sessions">Session lookup.</param>
public sealed class ListTillSessionsQueryHandler(ITillSessionRepository sessions)
    : IQueryHandler<ListTillSessionsQuery, IReadOnlyList<TillSession>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<TillSession>> HandleAsync(
        ListTillSessionsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await sessions
            .ListRecentAsync(query.TerminalId, Math.Clamp(query.Limit, 1, 200), cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>A sale line that completed without relieving stock.</summary>
/// <param name="SaleId">The sale.</param>
/// <param name="SaleNumber">Its receipt number, which is what a manager searches by.</param>
/// <param name="SaleLineId">The line.</param>
/// <param name="LocationId">Where the stock should have come off.</param>
/// <param name="ItemId">The item, when it has no variants.</param>
/// <param name="ItemVariantId">The variant.</param>
/// <param name="Description">What it was.</param>
/// <param name="Quantity">How much was sold and not relieved.</param>
/// <param name="Reason">Why the ledger refused it.</param>
/// <param name="CompletedAt">When the sale completed.</param>
public sealed record RefusedStockIssue(
    Guid SaleId,
    string SaleNumber,
    Guid SaleLineId,
    Guid LocationId,
    Guid? ItemId,
    Guid? ItemVariantId,
    string Description,
    Quantity Quantity,
    string Reason,
    DateTimeOffset? CompletedAt);

/// <summary>
/// The reconciliation queue ADR-073 creates: sales that went through while the ledger said there was
/// nothing to sell.
/// </summary>
/// <param name="LocationId">One stock location, or <c>null</c> for the whole tenant.</param>
/// <param name="Limit">How many to return.</param>
public sealed record ListRefusedStockIssuesQuery(Guid? LocationId, int Limit = 100)
    : IQuery<IReadOnlyList<RefusedStockIssue>>;

/// <summary>Reads the reconciliation queue.</summary>
/// <param name="sales">Sale lookup.</param>
public sealed class ListRefusedStockIssuesQueryHandler(ISaleRepository sales)
    : IQueryHandler<ListRefusedStockIssuesQuery, IReadOnlyList<RefusedStockIssue>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<RefusedStockIssue>> HandleAsync(
        ListRefusedStockIssuesQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<(Sale Sale, SaleLine Line)> rows = await sales
            .ListRefusedStockIssuesAsync(query.LocationId, Math.Clamp(query.Limit, 1, 500), cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. rows.Select(row => new RefusedStockIssue(
                row.Sale.Id,
                row.Sale.SaleNumber,
                row.Line.Id,
                row.Sale.LocationId,
                row.Line.ItemId,
                row.Line.ItemVariantId,
                row.Line.Description,
                row.Line.Quantity,
                row.Line.StockIssueNote ?? "Refused by the stock ledger.",
                row.Sale.CompletedAt)),
        ];
    }
}

/// <summary>Resolves a scanned barcode into something the till can ring up.</summary>
/// <param name="Barcode">The scan, exactly as the scanner delivered it.</param>
public sealed record LookupBarcodeQuery(string Barcode) : IQuery<SellableItem>;

/// <summary>Reads the barcode.</summary>
/// <param name="catalog">Barcode and item lookup.</param>
public sealed class LookupBarcodeQueryHandler(ISellableItemResolver catalog)
    : IQueryHandler<LookupBarcodeQuery, SellableItem>
{
    /// <inheritdoc />
    public async Task<SellableItem> HandleAsync(
        LookupBarcodeQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await catalog.ResolveBarcodeAsync(query.Barcode, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Every print of one sale's receipt, oldest first.</summary>
/// <param name="SaleId">The sale.</param>
public sealed record ListReceiptPrintsQuery(Guid SaleId) : IQuery<IReadOnlyList<ReceiptPrint>>;

/// <summary>Reads the print log.</summary>
/// <param name="prints">Print log lookup.</param>
public sealed class ListReceiptPrintsQueryHandler(IReceiptPrintRepository prints)
    : IQueryHandler<ListReceiptPrintsQuery, IReadOnlyList<ReceiptPrint>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ReceiptPrint>> HandleAsync(
        ListReceiptPrintsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await prints.ListForSaleAsync(query.SaleId, cancellationToken).ConfigureAwait(false);
    }
}
