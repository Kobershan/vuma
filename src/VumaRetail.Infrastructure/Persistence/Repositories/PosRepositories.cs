using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Platform;
using VumaRetail.Application.Pos;
using VumaRetail.Domain.Platform;
using VumaRetail.Domain.Pos;

namespace VumaRetail.Infrastructure.Persistence.Repositories;

/// <summary>EF Core implementation of <see cref="ISaleRepository"/>.</summary>
/// <param name="context">The database context.</param>
/// <remarks>
/// Every method that returns a sale a caller might write to loads its lines and tenders with it. The
/// aggregate recomputes its totals from those collections, so a sale loaded without them would zero
/// itself on the next <c>AddLine</c> — a bug that would look like a pricing fault and be nowhere near
/// the pricing code. Read-only list queries that never write are the exception and say so.
/// </remarks>
public sealed class SaleRepository(VumaRetailDbContext context) : ISaleRepository
{
    /// <inheritdoc />
    public Task<Sale?> FindAsync(Guid saleId, CancellationToken cancellationToken = default)
        => WithChildren(context.Sales).FirstOrDefaultAsync(sale => sale.Id == saleId, cancellationToken);

    /// <inheritdoc />
    public Task<Sale?> FindByNumberAsync(string saleNumber, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(saleNumber);

        string normalized = saleNumber.Trim();

        return WithChildren(context.Sales)
            .FirstOrDefaultAsync(sale => sale.SaleNumber == normalized, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Sale>> ListParkedAsync(
        Guid terminalId, CancellationToken cancellationToken = default)
        => await WithChildren(context.Sales)
            .Where(sale => sale.TerminalId == terminalId && sale.Status == SaleStatus.Parked)
            .OrderBy(sale => sale.OpenedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Sale>> ListForSessionAsync(
        Guid tillSessionId, CancellationToken cancellationToken = default)
        => await WithChildren(context.Sales)
            .Where(sale => sale.TillSessionId == tillSessionId)
            .OrderBy(sale => sale.OpenedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public Task<int> CountUnfinishedForSessionAsync(
        Guid tillSessionId, CancellationToken cancellationToken = default)
        => context.Sales
            .AsNoTracking()
            .CountAsync(
                sale => sale.TillSessionId == tillSessionId
                    && (sale.Status == SaleStatus.Open || sale.Status == SaleStatus.Parked),
                cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<(Sale Sale, SaleLine Line)>> ListRefusedStockIssuesAsync(
        Guid? locationId, int limit, CancellationToken cancellationToken = default)
    {
        // A join rather than a load of every sale with a refused line, because this report is read by a
        // manager scanning for exceptions and the interesting rows are a tiny fraction of the table.
        // ix_sale_lines_stock_issue_refused is the partial index it seeks on.
        IQueryable<Sale> sales = context.Sales.AsNoTracking();

        if (locationId is { } location)
        {
            sales = sales.Where(sale => sale.LocationId == location);
        }

        var rows = await context.SaleLines
            .AsNoTracking()
            .Where(line => line.StockIssue == StockIssueStatus.Refused && !line.IsVoided)
            .Join(sales, line => line.SaleId, sale => sale.Id, (line, sale) => new { sale, line })
            .Where(row => row.sale.Status == SaleStatus.Completed)
            .OrderByDescending(row => row.sale.CompletedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. rows.Select(row => (row.sale, row.line))];
    }

    /// <inheritdoc />
    public void Add(Sale sale) => context.Sales.Add(sale);

    private static IQueryable<Sale> WithChildren(IQueryable<Sale> sales)
        => sales.Include(sale => sale.Lines).Include(sale => sale.Tenders);
}

/// <summary>EF Core implementation of <see cref="ITillSessionRepository"/>.</summary>
/// <param name="context">The database context.</param>
public sealed class TillSessionRepository(VumaRetailDbContext context) : ITillSessionRepository
{
    /// <inheritdoc />
    public Task<TillSession?> FindAsync(Guid tillSessionId, CancellationToken cancellationToken = default)
        => context.TillSessions.FirstOrDefaultAsync(session => session.Id == tillSessionId, cancellationToken);

    /// <inheritdoc />
    public Task<TillSession?> FindOpenForTerminalAsync(
        Guid terminalId, CancellationToken cancellationToken = default)
        => context.TillSessions.FirstOrDefaultAsync(
            session => session.TerminalId == terminalId && session.Status == TillSessionStatus.Open,
            cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<TillSession>> ListRecentAsync(
        Guid? terminalId, int limit, CancellationToken cancellationToken = default)
    {
        IQueryable<TillSession> query = context.TillSessions.AsNoTracking();

        if (terminalId is { } terminal)
        {
            query = query.Where(session => session.TerminalId == terminal);
        }

        return await query
            .OrderByDescending(session => session.OpenedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Add(TillSession session) => context.TillSessions.Add(session);
}

/// <summary>EF Core implementation of <see cref="IReceiptPrintRepository"/>.</summary>
/// <param name="context">The database context.</param>
/// <remarks>
/// There is deliberately no update or remove path, the same way <c>StockLedgerRepository</c> has none.
/// <c>AuditInterceptor</c> refuses any modification to an <c>IImmutableRecord</c>; this repository
/// gives that no surface to be called through in the first place.
/// </remarks>
public sealed class ReceiptPrintRepository(VumaRetailDbContext context) : IReceiptPrintRepository
{
    /// <inheritdoc />
    public Task<int> CountForSaleAsync(Guid saleId, CancellationToken cancellationToken = default)
        => context.ReceiptPrints.AsNoTracking().CountAsync(print => print.SaleId == saleId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReceiptPrint>> ListForSaleAsync(
        Guid saleId, CancellationToken cancellationToken = default)
        => await context.ReceiptPrints
            .AsNoTracking()
            .Where(print => print.SaleId == saleId)
            .OrderBy(print => print.PrintedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(ReceiptPrint print) => context.ReceiptPrints.Add(print);
}

/// <summary>EF Core implementation of <see cref="ITenantRepository"/>.</summary>
/// <param name="context">The database context.</param>
/// <remarks>
/// A tenant row is subject to the global query filter like every other entity — a <c>Tenant</c>'s own
/// <c>TenantId</c> is its own <c>Id</c>, which is what makes it the root of its isolation boundary.
/// So this only ever returns the ambient tenant, and asking for another one returns <c>null</c> rather
/// than somebody else's registration details. That is the correct behaviour for POS's only caller —
/// the receipt, which asks for `sale.TenantId` and is always inside that tenant's own scope.
/// </remarks>
public sealed class TenantRepository(VumaRetailDbContext context) : ITenantRepository
{
    /// <inheritdoc />
    public Task<Tenant?> FindAsync(Guid tenantId, CancellationToken cancellationToken = default)
        => context.Tenants.AsNoTracking().FirstOrDefaultAsync(tenant => tenant.Id == tenantId, cancellationToken);
}
