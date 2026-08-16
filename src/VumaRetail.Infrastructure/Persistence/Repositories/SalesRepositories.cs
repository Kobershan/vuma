using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Abstractions.Sales;
using VumaRetail.Domain.Sales;

namespace VumaRetail.Infrastructure.Persistence.Repositories;

/// <summary>EF Core implementation of <see cref="IPriceListRepository"/>.</summary>
/// <param name="context">The database context.</param>
/// <remarks>
/// Every method that returns a list a caller might write to loads its lines with it, for the reason
/// <c>SaleRepository</c> gives: an aggregate whose children are missing quietly behaves as though it
/// had none. <see cref="ListCandidatesAsync"/> is the deliberate exception — it filters the lines to
/// one stock-keeping unit, because it runs once per scanned item and a full load would make the till
/// slower with every product the shop adds.
/// </remarks>
public sealed class PriceListRepository(VumaRetailDbContext context) : IPriceListRepository
{
    /// <inheritdoc />
    public Task<PriceList?> FindAsync(Guid priceListId, CancellationToken cancellationToken = default)
        => context.PriceLists
            .Include(list => list.Lines)
            .FirstOrDefaultAsync(list => list.Id == priceListId, cancellationToken);

    /// <inheritdoc />
    public Task<PriceList?> FindByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        string normalized = code.Trim().ToUpperInvariant();

        return context.PriceLists
            .Include(list => list.Lines)
            .FirstOrDefaultAsync(list => list.Code == normalized, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        string normalized = code.Trim().ToUpperInvariant();

        return context.PriceLists.AsNoTracking().AnyAsync(list => list.Code == normalized, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PriceList>> ListAsync(
        bool includeInactive, CancellationToken cancellationToken = default)
    {
        IQueryable<PriceList> query = context.PriceLists.AsNoTracking().Include(list => list.Lines);

        if (!includeInactive)
        {
            query = query.Where(list => list.IsActive);
        }

        return await query
            .OrderByDescending(list => list.Priority)
            .ThenBy(list => list.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PriceList>> ListCandidatesAsync(
        Guid? itemId,
        Guid? itemVariantId,
        Guid? storeId,
        DateOnly onDate,
        CancellationToken cancellationToken = default)
        => await context.PriceLists
            .AsNoTracking()
            .Where(list => list.IsActive)
            .Where(list => list.EffectiveFrom <= onDate)
            .Where(list => list.EffectiveTo == null || list.EffectiveTo >= onDate)
            .Where(list => list.StoreId == null || list.StoreId == storeId)

            // Filtered include: the resolver only ever asks about one stock-keeping unit, and every
            // quantity break for it has to come back so FindPrice can pick the right one. A list that
            // does not price this item comes back with no lines and loses to the next candidate, which
            // is the behaviour ChooseList relies on.
            .Include(list => list.Lines
                .Where(line => line.ItemId == itemId && line.ItemVariantId == itemVariantId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(PriceList priceList) => context.PriceLists.Add(priceList);
}

/// <summary>EF Core implementation of <see cref="IPromotionRepository"/>.</summary>
/// <param name="context">The database context.</param>
public sealed class PromotionRepository(VumaRetailDbContext context) : IPromotionRepository
{
    /// <inheritdoc />
    public Task<Promotion?> FindAsync(Guid promotionId, CancellationToken cancellationToken = default)
        => context.Promotions
            .Include(promotion => promotion.Lines)
            .FirstOrDefaultAsync(promotion => promotion.Id == promotionId, cancellationToken);

    /// <inheritdoc />
    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        string normalized = code.Trim().ToUpperInvariant();

        return context.Promotions
            .AsNoTracking()
            .AnyAsync(promotion => promotion.Code == normalized, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Promotion>> ListLiveAsync(
        Guid? storeId, DateOnly onDate, CancellationToken cancellationToken = default)
        => await context.Promotions
            .AsNoTracking()
            .Where(promotion => promotion.IsActive)
            .Where(promotion => promotion.EffectiveFrom <= onDate)
            .Where(promotion => promotion.EffectiveTo == null || promotion.EffectiveTo >= onDate)
            .Where(promotion => promotion.StoreId == null || promotion.StoreId == storeId)
            .Include(promotion => promotion.Lines)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Promotion>> ListAsync(
        bool includeInactive, CancellationToken cancellationToken = default)
    {
        IQueryable<Promotion> query = context.Promotions
            .AsNoTracking()
            .Include(promotion => promotion.Lines);

        if (!includeInactive)
        {
            query = query.Where(promotion => promotion.IsActive);
        }

        return await query
            .OrderByDescending(promotion => promotion.Priority)
            .ThenBy(promotion => promotion.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Add(Promotion promotion) => context.Promotions.Add(promotion);
}

/// <summary>EF Core implementation of <see cref="ISalesReturnRepository"/>.</summary>
/// <param name="context">The database context.</param>
public sealed class SalesReturnRepository(VumaRetailDbContext context) : ISalesReturnRepository
{
    /// <inheritdoc />
    public Task<SalesReturn?> FindAsync(Guid salesReturnId, CancellationToken cancellationToken = default)
        => context.SalesReturns
            .Include(salesReturn => salesReturn.Lines)
            .FirstOrDefaultAsync(salesReturn => salesReturn.Id == salesReturnId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<SalesReturn>> ListForSaleAsync(
        Guid saleId, CancellationToken cancellationToken = default)
        => await context.SalesReturns
            .AsNoTracking()
            .Include(salesReturn => salesReturn.Lines)
            .Where(salesReturn => salesReturn.SaleId == saleId)
            .OrderBy(salesReturn => salesReturn.RaisedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<SalesReturn>> ListForPeriodAsync(
        DateOnly from, DateOnly to, int limit, CancellationToken cancellationToken = default)
    {
        // The period is inclusive at both ends and completed_at is a timestamptz, so the upper bound is
        // the start of the day after rather than the start of `to` — otherwise every return raised
        // after midnight on the last day of a month falls out of the month's report.
        DateTimeOffset start = new(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        DateTimeOffset end = new(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        return await context.SalesReturns
            .AsNoTracking()
            .Include(salesReturn => salesReturn.Lines)
            .Where(salesReturn => salesReturn.Status == SalesReturnStatus.Completed)
            .Where(salesReturn => salesReturn.CompletedAt >= start && salesReturn.CompletedAt < end)
            .OrderByDescending(salesReturn => salesReturn.CompletedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<decimal> SumReturnedQuantityAsync(
        Guid saleLineId, Guid? excludingReturnId, CancellationToken cancellationToken = default)
    {
        // Drafts count. A return sitting on another terminal's screen is goods the shop has already
        // taken back over the counter, and waiting for it to complete is how the same item is refunded
        // twice. Cancelled documents do not: nothing came back on them.
        IQueryable<Guid> live = context.SalesReturns
            .AsNoTracking()
            .Where(salesReturn => salesReturn.Status != SalesReturnStatus.Cancelled)
            .Select(salesReturn => salesReturn.Id);

        IQueryable<SalesReturnLine> lines = context.SalesReturnLines
            .AsNoTracking()
            .Where(line => line.SaleLineId == saleLineId)
            .Where(line => live.Contains(line.SalesReturnId));

        if (excludingReturnId is { } excluded)
        {
            lines = lines.Where(line => line.SalesReturnId != excluded);
        }

        // SumAsync over an empty set returns the default rather than null for a non-nullable decimal
        // projection, which is exactly the zero this wants.
        return await lines
            .SumAsync(line => line.Quantity.Value, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Add(SalesReturn salesReturn) => context.SalesReturns.Add(salesReturn);
}

/// <summary>EF Core implementation of <see cref="IPriceOverrideLogRepository"/>.</summary>
/// <param name="context">The database context.</param>
/// <remarks>
/// There is deliberately no update or remove path, the same way <c>ReceiptPrintRepository</c> has none.
/// <c>AuditInterceptor</c> refuses any modification to an <c>IImmutableRecord</c>; this repository gives
/// that no surface to be called through in the first place.
/// </remarks>
public sealed class PriceOverrideLogRepository(VumaRetailDbContext context) : IPriceOverrideLogRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<PriceOverrideLog>> ListForPeriodAsync(
        DateOnly from,
        DateOnly to,
        Guid? operatorUserId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset start = new(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        DateTimeOffset end = new(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        IQueryable<PriceOverrideLog> query = context.PriceOverrideLogs
            .AsNoTracking()
            .Where(entry => entry.OccurredAt >= start && entry.OccurredAt < end);

        if (operatorUserId is { } operatorId)
        {
            query = query.Where(entry => entry.OperatorUserId == operatorId);
        }

        return await query
            .OrderByDescending(entry => entry.OccurredAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Add(PriceOverrideLog entry) => context.PriceOverrideLogs.Add(entry);
}
