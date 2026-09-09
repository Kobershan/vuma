using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.FieldSales;
using VumaRetail.Domain.FieldSales;
using VumaRetail.Domain.Primitives;

#pragma warning disable CS1591
#pragma warning disable CA1062

namespace VumaRetail.Application.FieldSales.Queries;

// ---------------------------------------------------------------------------
// Pro forma view
// ---------------------------------------------------------------------------

/// <summary>One pro forma line on the view.</summary>
public sealed record ProFormaLineView(
    Guid LineId,
    Guid? ItemId,
    Guid? ItemVariantId,
    decimal QuantityValue,
    string QuantityUom,
    Money UnitPrice,
    Money Discount,
    string TaxCode,
    Money Tax,
    Money Net,
    Money Gross,
    string PackSize,
    Money AvailableAtCapture,
    DateTimeOffset AvailabilityAsAt);

/// <summary>One pro forma with its lines and decision trail.</summary>
public sealed record ProFormaView(
    Guid ProFormaId,
    string ProFormaNumber,
    Guid RepId,
    Guid CompanyId,
    Guid PartnerId,
    string Currency,
    ProFormaStatus Status,
    Money Gross,
    Money? RepriceDelta,
    Guid? ApprovalRequestId,
    Guid? ConvertedOrderId,
    string? DecisionReason,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<ProFormaLineView> Lines);

/// <summary>Reads one pro forma. Territory-enforced: another rep's document is refused.</summary>
public sealed record GetProFormaQuery(Guid ProFormaId, Guid CallerRepId) : IQuery<ProFormaView>;

/// <summary>Handler for <see cref="GetProFormaQuery"/>.</summary>
public sealed class GetProFormaQueryHandler(
    IProFormaOrderRepository proFormas,
    IRepRepository reps,
    ITenantContext tenant)
    : IQueryHandler<GetProFormaQuery, ProFormaView>
{
    public async Task<ProFormaView> HandleAsync(GetProFormaQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        ProFormaOrder order = await proFormas.FindAsync(query.ProFormaId, cancellationToken).ConfigureAwait(false)
            ?? throw new FieldSalesException("PROFORMA_NOT_FOUND", $"No pro forma {query.ProFormaId}.");

        if (order.TenantId != tenant.TenantId)
        {
            throw FieldSalesException.Forbidden("read across tenants");
        }

        if (order.RepId != query.CallerRepId)
        {
            Rep? caller = await reps.FindAsync(query.CallerRepId, cancellationToken).ConfigureAwait(false);
            if (caller is null || caller.TenantId != tenant.TenantId)
            {
                throw FieldSalesException.Forbidden("read another rep's document");
            }
        }

        return Map(order);
    }

    internal static ProFormaView Map(ProFormaOrder order)
        => new(
            order.Id,
            order.ProFormaNumber,
            order.RepId,
            order.CompanyId ?? Guid.Empty,
            order.PartnerId,
            order.Currency,
            order.Status,
            order.Gross,
            order.RepriceDelta,
            order.ApprovalRequestId,
            order.ConvertedOrderId,
            order.DecisionReason,
            order.ExpiresAt,
            [.. order.Lines.Select(line => new ProFormaLineView(
                line.Id, line.ItemId, line.ItemVariantId, line.QuantityValue, line.QuantityUom,
                line.UnitPrice, line.DiscountAmount, line.TaxCode, line.TaxAmount, line.Net, line.Gross,
                line.PackSizeDescription, line.AvailableAtCapture, line.AvailabilityAsAt))]);
}

// ---------------------------------------------------------------------------
// Availability
// ---------------------------------------------------------------------------

/// <summary>One company's available for one SKU, with its as-at.</summary>
public sealed record RepAvailabilityRow(Guid CompanyId, decimal Available, DateTimeOffset AsAt);

/// <summary>Group-wide available for what the rep asked, per company, stamped.</summary>
public sealed record RepAvailabilityView(
    Guid? ItemId,
    Guid? ItemVariantId,
    IReadOnlyList<RepAvailabilityRow> Companies,
    bool HasStaleContributor);

/// <summary>Reads group-wide available for a rep. Territory- and company-enforced.</summary>
public sealed record GetRepAvailabilityQuery(
    Guid RepId,
    Guid? ItemId,
    Guid? ItemVariantId) : IQuery<RepAvailabilityView>;

/// <summary>Handler for <see cref="GetRepAvailabilityQuery"/>.</summary>
public sealed class GetRepAvailabilityQueryHandler(
    IRepRepository reps,
    IAvailabilityProbe availability,
    ITenantContext tenant)
    : IQueryHandler<GetRepAvailabilityQuery, RepAvailabilityView>
{
    public async Task<RepAvailabilityView> HandleAsync(GetRepAvailabilityQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        Rep rep = await reps.FindAsync(query.RepId, cancellationToken).ConfigureAwait(false)
            ?? throw FieldSalesException.Forbidden("quote as an unknown rep");

        if (!rep.IsActive || rep.TenantId != tenant.TenantId)
        {
            throw FieldSalesException.Forbidden("quote as an inactive or foreign rep");
        }

        List<RepAvailabilityRow> rows = [];
        bool stale = false;
        foreach (Guid companyId in rep.CompanyIds)
        {
            AvailabilityProbeResult probed = await availability.ProbeAsync(
                    companyId, query.ItemId, query.ItemVariantId, cancellationToken)
                .ConfigureAwait(false);
            rows.Add(new RepAvailabilityRow(companyId, probed.Available, probed.AsAt));
            stale = stale || probed.IsStale;
        }

        return new RepAvailabilityView(query.ItemId, query.ItemVariantId, rows, stale);
    }
}

// ---------------------------------------------------------------------------
// Performance
// ---------------------------------------------------------------------------

/// <summary>One period's figures with its comparison and variance.</summary>
public sealed record RepPerformanceView(
    Guid RepId,
    Guid? CompanyId,
    DateOnly Period,
    DateOnly CompareTo,
    decimal NetValue,
    decimal CompareNetValue,
    decimal Variance,
    decimal VariancePercent,
    int Version,
    string Reason);

/// <summary>Reads a closed period with a comparison period. Own-or-team enforced.</summary>
public sealed record GetRepPerformanceQuery(
    Guid RepId,
    Guid? CompanyId,
    DateOnly Period,
    DateOnly CompareTo,
    Guid CallerRepId,
    bool CallerCanViewTeam) : IQuery<RepPerformanceView>;

/// <summary>Handler for <see cref="GetRepPerformanceQuery"/>.</summary>
public sealed class GetRepPerformanceQueryHandler(
    IRepPerformanceRepository snapshots,
    IRepRepository reps,
    ITenantContext tenant)
    : IQueryHandler<GetRepPerformanceQuery, RepPerformanceView>
{
    public async Task<RepPerformanceView> HandleAsync(GetRepPerformanceQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.RepId != query.CallerRepId && !query.CallerCanViewTeam)
        {
            throw FieldSalesException.Forbidden("read another rep's performance");
        }

        Rep rep = await reps.FindAsync(query.RepId, cancellationToken).ConfigureAwait(false)
            ?? throw FieldSalesException.Forbidden("read performance for an unknown rep");

        if (rep.TenantId != tenant.TenantId)
        {
            throw FieldSalesException.Forbidden("read performance across tenants");
        }

        RepPerformanceSnapshot? current = await snapshots.FindLatestAsync(
                query.RepId, query.CompanyId, query.Period, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new FieldSalesException(
                "PERFORMANCE_NOT_SNAPSHOTTED",
                $"No snapshot for {query.Period:yyyy-MM}; close the period first.");

        RepPerformanceSnapshot? compare = await snapshots.FindLatestAsync(
                query.RepId, query.CompanyId, query.CompareTo, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new FieldSalesException(
                "PERFORMANCE_NOT_SNAPSHOTTED",
                $"No snapshot for {query.CompareTo:yyyy-MM}; close the period first.");

        decimal variance = current.NetValue.Amount - compare.NetValue.Amount;
        decimal percent = compare.NetValue.Amount == 0m
            ? 0m
            : decimal.Round(variance / compare.NetValue.Amount * 100m, 2, MidpointRounding.AwayFromZero);

        return new RepPerformanceView(
            query.RepId, query.CompanyId, query.Period, query.CompareTo,
            current.NetValue.Amount, compare.NetValue.Amount, variance, percent,
            current.Version, current.Reason);
    }
}
