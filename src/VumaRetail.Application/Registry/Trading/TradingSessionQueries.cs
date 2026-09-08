using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry.Trading;

#pragma warning disable CS1591
#pragma warning disable CA1062

namespace VumaRetail.Application.Registry.Trading;

// ---------------------------------------------------------------------------
// Session view
// ---------------------------------------------------------------------------

/// <summary>One line on the session view.</summary>
public sealed record SessionLineView(
    Guid LineId,
    Guid CompanyId,
    string Barcode,
    string Description,
    decimal QuantityValue,
    string QuantityUom,
    Money UnitPrice,
    Money Discount,
    string TaxCode,
    Money Tax,
    Money Net,
    Money Gross,
    string PackSize,
    bool IsVoided);

/// <summary>One company segment on the session view.</summary>
public sealed record SessionSegmentView(
    Guid CompanyId,
    Money Net,
    Money Tax,
    Money Gross,
    Money? Allocation,
    string? AllocationBasis,
    string? InvoiceNumber,
    IReadOnlyList<SessionLineView> Lines);

/// <summary>The whole session: segments, per-company tax, allocation, invoices when done.</summary>
public sealed record TradingSessionView(
    Guid SessionId,
    string SessionNumber,
    Guid SessionCompanyId,
    string Currency,
    TradingSessionStatus Status,
    Money Gross,
    string? TenderType,
    Money? TenderAmount,
    string? FailureReason,
    IReadOnlyList<string> UnwoundInvoiceNumbers,
    IReadOnlyList<SessionSegmentView> Segments);

/// <summary>Reads one trading session with its per-segment tax.</summary>
public sealed record GetTradingSessionQuery(Guid SessionId) : IQuery<TradingSessionView>;

/// <summary>Handler for <see cref="GetTradingSessionQuery"/>.</summary>
public sealed class GetTradingSessionQueryHandler(
    ITradingSessionRepository sessions,
    ITenantContext tenant)
    : IQueryHandler<GetTradingSessionQuery, TradingSessionView>
{
    public async Task<TradingSessionView> HandleAsync(GetTradingSessionQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        TradingSession session = await sessions.FindAsync(query.SessionId, cancellationToken).ConfigureAwait(false)
            ?? throw new TradingSessionException(
                "TRADING_SESSION_NOT_FOUND", $"No trading session {query.SessionId}.");

        if (session.TenantId != tenant.TenantId)
        {
            throw new TradingSessionException(
                "TRADING_SESSION_WRONG_TENANT", $"Trading session {query.SessionId} belongs to another tenant.");
        }

        return Map(session);
    }

    internal static TradingSessionView Map(TradingSession session)
        => new(
            session.Id,
            session.SessionNumber,
            session.SessionCompanyId,
            session.Currency,
            session.Status,
            session.Gross,
            session.TenderType,
            session.TenderAmount,
            session.FailureReason,
            session.UnwoundInvoiceNumbers,
            [.. session.Segments.Where(segment => !segment.IsRemoved).Select(segment => new SessionSegmentView(
                segment.CompanyId,
                segment.Net,
                segment.Tax,
                segment.Gross,
                segment.TenderAllocation,
                segment.AllocationBasis,
                segment.ResultingInvoiceNumber,
                [.. segment.Lines.Select(line => new SessionLineView(
                    line.Id,
                    line.CompanyId,
                    line.Barcode,
                    line.Description,
                    line.QuantityValue,
                    line.QuantityUom,
                    line.UnitPrice,
                    line.DiscountAmount,
                    line.TaxCode,
                    line.TaxAmount,
                    line.Net,
                    line.Gross,
                    line.PackSizeDescription,
                    line.IsVoided))]))]);
}

// ---------------------------------------------------------------------------
// Documents (invoice ids + the non-fiscal summary model)
// ---------------------------------------------------------------------------

/// <summary>The customer-facing summary model. Not a fiscal document.</summary>
public sealed record BasketSummaryModel(
    string SessionNumber,
    IReadOnlyList<SummaryInvoice> Invoices,
    Money Total,
    string NotATaxInvoiceStatement);

/// <summary>One invoice on the summary. Names are joined at the edge from the registry.</summary>
public sealed record SummaryInvoice(Guid CompanyId, Guid InvoiceId, string InvoiceNumber, Money Subtotal);

/// <summary>Reads the session's documents: per-company invoice ids plus the basket summary.</summary>
public sealed record GetSessionDocumentsQuery(Guid SessionId) : IQuery<BasketSummaryModel>;

/// <summary>Handler for <see cref="GetSessionDocumentsQuery"/>.</summary>
public sealed class GetSessionDocumentsQueryHandler(
    ITradingSessionRepository sessions,
    ITenantContext tenant)
    : IQueryHandler<GetSessionDocumentsQuery, BasketSummaryModel>
{
    public async Task<BasketSummaryModel> HandleAsync(GetSessionDocumentsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        TradingSession session = await sessions.FindAsync(query.SessionId, cancellationToken).ConfigureAwait(false)
            ?? throw new TradingSessionException(
                "TRADING_SESSION_NOT_FOUND", $"No trading session {query.SessionId}.");

        if (session.TenantId != tenant.TenantId)
        {
            throw new TradingSessionException(
                "TRADING_SESSION_WRONG_TENANT", $"Trading session {query.SessionId} belongs to another tenant.");
        }

        if (session.Status is not TradingSessionStatus.Completed)
        {
            throw TradingSessionException.IllegalTransition(session.Status, "have documents read");
        }

        IReadOnlyList<TradingSessionSegment> posted = [.. session.Segments.Where(s => !s.IsRemoved)];
        string numbers = string.Join(" and ", posted.Select(s => s.ResultingInvoiceNumber));

        // The sentence the law wants on the customer's copy: this paper is a summary, and it
        // names the real tax invoices. No VAT summary ever appears on this model (rule: the
        // summary carries subtotals and one total, never a VAT breakdown).
        return new BasketSummaryModel(
            session.SessionNumber,
            [.. posted.Select(segment => new SummaryInvoice(
                segment.CompanyId,
                segment.ResultingInvoiceId ?? Guid.Empty,
                segment.ResultingInvoiceNumber ?? string.Empty,
                segment.Gross))],
            session.Gross,
            $"This is not a tax invoice. Your tax invoices are {numbers}.");
    }
}
