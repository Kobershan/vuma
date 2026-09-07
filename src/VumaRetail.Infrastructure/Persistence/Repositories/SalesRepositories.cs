using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Sales;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Sales;
using VumaRetail.Domain.Sales.Analytics;
using VumaRetail.Domain.Sales.Invoices;
using VumaRetail.Domain.Sales.Quotes;

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
/// <param name="tenant">The ambient tenant, for the row-locked read's raw-SQL predicate.</param>
public sealed class SalesReturnRepository(VumaRetailDbContext context, ITenantContext tenant) : ISalesReturnRepository
{
    /// <inheritdoc />
    public Task<SalesReturn?> FindAsync(Guid salesReturnId, CancellationToken cancellationToken = default)
        => context.SalesReturns
            .Include(salesReturn => salesReturn.Lines)
            .FirstOrDefaultAsync(salesReturn => salesReturn.Id == salesReturnId, cancellationToken);

    /// <inheritdoc />
    public async Task<SalesReturn?> FindForUpdateAsync(Guid salesReturnId, CancellationToken cancellationToken = default)
    {
        // The lock is taken with a raw, columns-only SELECT rather than SELECT * against the mapped
        // entity set: SalesReturn's Money complex properties (Net/Tax/Gross) do not round-trip through
        // FromSqlInterpolated's own column-shape matching the way a plain-scalar entity like ImportBatch
        // does, and fail with "column ... does not exist" against columns that are really there. Locking
        // the row is all raw SQL needs to do; the actual read goes through the normal, fully-supported
        // LINQ path below, which already holds this transaction's lock by the time it runs.
        //
        // The tenant predicate is written out rather than left to the global query filter — raw SQL does
        // not reach inside it (the same reasoning ImportBatchRepository's FindForUpdateAsync gives).
        Guid lockedId = await context.Database
            .SqlQuery<Guid>(
                $"""
                 SELECT id AS "Value" FROM sales.sales_returns
                 WHERE id = {salesReturnId} AND tenant_id = {tenant.TenantId} AND deleted_at IS NULL
                 FOR UPDATE
                 """)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (lockedId == Guid.Empty)
        {
            return null;
        }

        SalesReturn? salesReturn = await context.SalesReturns
            .FirstOrDefaultAsync(candidate => candidate.Id == salesReturnId, cancellationToken)
            .ConfigureAwait(false);

        if (salesReturn is null)
        {
            return null;
        }

        // Loaded after the lock, not joined into it — FOR UPDATE across a join would lock every line
        // row too, and nothing else contends for lines except through the return this caller now holds.
        await context.Entry(salesReturn).Collection(item => item.Lines).LoadAsync(cancellationToken)
            .ConfigureAwait(false);

        return salesReturn;
    }

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

/// <summary>EF Core implementation of <see cref="IQuoteRepository"/>.</summary>
/// <param name="context">The database context.</param>
/// <remarks>
/// Lines are always loaded with the quote: an aggregate whose children are missing quietly
/// behaves as though it had none, and a quoteless quote would issue, accept and convert.
/// </remarks>
public sealed class QuoteRepository(VumaRetailDbContext context) : IQuoteRepository
{
    public Task<Quote?> FindAsync(Guid id, CancellationToken cancellationToken = default)
        => context.Quotes
            .Include(quote => quote.Lines)
            .FirstOrDefaultAsync(q => q.Id == id, cancellationToken);

    public Task<Quote?> FindByNumberAsync(string number, CancellationToken cancellationToken = default)
        => context.Quotes
            .Include(quote => quote.Lines)
            .FirstOrDefaultAsync(q => q.QuoteNumber == number, cancellationToken);

    public async Task<IReadOnlyList<Quote>> ListForCustomerAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        return await context.Quotes
            .Include(quote => quote.Lines)
            .Where(q => q.CustomerId == customerId).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Quote>> ListAsync(QuoteStatus? status, DateTimeOffset? validUntil, CancellationToken cancellationToken = default)
    {
        var query = context.Quotes.Include(quote => quote.Lines).AsQueryable();
        if (status.HasValue)
        {
            query = query.Where(q => q.Status == status.Value);
        }

        if (validUntil.HasValue)
        {
            query = query.Where(q => q.ValidUntil <= validUntil.Value);
        }

        return await query.ToListAsync(cancellationToken);
    }

    public void Add(Quote quote) => context.Quotes.Add(quote);
}

/// <summary>EF Core implementation of <see cref="IInvoiceRepository"/>.</summary>
/// <param name="context">The database context.</param>
/// <remarks>
/// Lines are always loaded with the invoice, for the same reason as quotes: totals are
/// recomputed from the stored lines, and a lineless invoice would post at zero.
/// </remarks>
public sealed class InvoiceRepository(VumaRetailDbContext context) : IInvoiceRepository
{
    public Task<Invoice?> FindAsync(Guid id, CancellationToken cancellationToken = default)
        => context.Invoices
            .Include(invoice => invoice.Lines)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

    public Task<Invoice?> FindByNumberAsync(string number, CancellationToken cancellationToken = default)
        => context.Invoices
            .Include(invoice => invoice.Lines)
            .FirstOrDefaultAsync(i => i.InvoiceNumber == number, cancellationToken);

    public async Task<IReadOnlyList<Invoice>> ListForCompanyAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        return await context.Invoices
            .Include(invoice => invoice.Lines)
            .Where(i => i.CompanyId == companyId).ToListAsync(cancellationToken);
    }

    public void Add(Invoice invoice) => context.Invoices.Add(invoice);
}

/// <summary>EF Core implementation of <see cref="ISalesAnalyticsRepository"/>.</summary>
/// <param name="context">The database context.</param>
/// <remarks>
/// The stored grain is daily per company per channel; weekly, monthly and year-to-date views
/// roll those facts up in memory at read time, so one rebuild feeds every period without four
/// copies of the truth drifting apart.
/// </remarks>
public sealed class SalesAnalyticsRepository(VumaRetailDbContext context) : ISalesAnalyticsRepository
{
    public async Task<IReadOnlyList<SalesAnalytics>> GetByCompanyAsync(Guid companyId, AnalyticsPeriod period, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        List<SalesAnalytics> days = await context.SalesAnalytics
            .AsNoTracking()
            .Where(a => a.CompanyId == companyId && a.Period == AnalyticsPeriod.Daily && a.PeriodStart >= from && a.PeriodEnd <= to)
            .OrderBy(a => a.PeriodStart)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return RollUp(days, period);
    }

    public async Task<IReadOnlyList<SalesAnalytics>> GetGroupAsync(AnalyticsPeriod period, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        List<SalesAnalytics> days = await context.SalesAnalytics
            .AsNoTracking()
            .Where(a => a.Period == AnalyticsPeriod.Daily && a.PeriodStart >= from && a.PeriodEnd <= to)
            .OrderBy(a => a.PeriodStart)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return RollUp(days, period);
    }

    public async Task RebuildAsync(Guid? companyId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        IQueryable<SalesAnalytics> stale = context.SalesAnalytics
            .Where(a => a.Period == AnalyticsPeriod.Daily && a.PeriodStart >= from && a.PeriodEnd < to);

        if (companyId.HasValue)
        {
            stale = stale.Where(a => a.CompanyId == companyId.Value);
        }

        context.SalesAnalytics.RemoveRange(stale);

        IQueryable<Invoice> posted = context.Invoices
            .Include(invoice => invoice.Lines)
            .Where(invoice => invoice.Status == InvoiceStatus.Posted)
            .Where(invoice => invoice.PostedAt >= from && invoice.PostedAt < to);

        if (companyId.HasValue)
        {
            posted = posted.Where(invoice => invoice.CompanyId == companyId.Value);
        }

        List<Invoice> invoices = await posted.ToListAsync(cancellationToken).ConfigureAwait(false);

        var buckets = invoices
            .Where(invoice => invoice.CompanyId.HasValue)
            .GroupBy(invoice => (
                Company: invoice.CompanyId!.Value,
                Day: DayFloor(invoice.PostedAt!.Value),
                invoice.TenantId,
                invoice.StoreId,
                Channel: invoice.SourceDocumentType.ToString(),
                invoice.Currency))
            .OrderBy(bucket => bucket.Key.Day)
            .ToList();

        foreach (var bucket in buckets)
        {
            SalesAnalytics row = SalesAnalytics.Create(
                bucket.Key.TenantId,
                bucket.Key.StoreId,
                bucket.Key.Company,
                AnalyticsPeriod.Daily,
                bucket.Key.Day,
                bucket.Key.Day.AddDays(1),
                categoryCode: null,
                bucket.Key.Channel,
                bucket.Key.Currency);

            Money revenue = Money.Zero(bucket.Key.Currency);
            Money tax = Money.Zero(bucket.Key.Currency);
            int lines = 0;
            foreach (Invoice invoice in bucket)
            {
                revenue += invoice.Gross;
                tax += invoice.Tax;
                lines += invoice.Lines.Count;
            }

            // Cost of sale is zero here by construction: an invoice snapshots price and tax, not
            // cost — cost arrives with Stage 08's valuation postings, which a follow-up joins in.
            // Reporting revenue as margin would be the lie; reporting cost as unknown is the truth.
            row.Aggregate(revenue, Money.Zero(bucket.Key.Currency), tax, bucket.Count(), lines, to, isStale: false);
            context.SalesAnalytics.Add(row);
        }
    }

    private static DateTimeOffset DayFloor(DateTimeOffset instant)
        => new DateTimeOffset(instant.UtcDateTime.Date, TimeSpan.Zero);

    private static IReadOnlyList<SalesAnalytics> RollUp(IReadOnlyList<SalesAnalytics> days, AnalyticsPeriod period)
    {
        if (period == AnalyticsPeriod.Daily || days.Count == 0)
        {
            return days;
        }

        return days
            .GroupBy(day => (
                day.TenantId,
                day.StoreId,
                day.CompanyId,
                Bucket: BucketStart(day.PeriodStart, period),
                day.CategoryCode,
                day.Channel,
                day.Currency))
            .OrderBy(bucket => bucket.Key.Bucket)
            .Select(bucket =>
            {
                SalesAnalytics first = bucket.First();
                SalesAnalytics rolled = SalesAnalytics.Create(
                    bucket.Key.TenantId,
                    bucket.Key.StoreId,
                    bucket.Key.CompanyId ?? Guid.Empty,
                    period,
                    bucket.Key.Bucket,
                    BucketEnd(bucket.Key.Bucket, period),
                    bucket.Key.CategoryCode,
                    bucket.Key.Channel,
                    bucket.Key.Currency);

                Money revenue = Money.Zero(bucket.Key.Currency);
                Money cost = Money.Zero(bucket.Key.Currency);
                Money tax = Money.Zero(bucket.Key.Currency);
                int orders = 0;
                int lines = 0;
                DateTimeOffset asAt = first.AsAt;
                bool stale = false;
                foreach (SalesAnalytics day in bucket)
                {
                    revenue += day.Revenue;
                    cost += day.CostOfSale;
                    tax += day.TaxLiability;
                    orders += day.OrderCount;
                    lines += day.LineCount;
                    if (day.AsAt > asAt)
                    {
                        asAt = day.AsAt;
                    }

                    stale = stale || day.IsStale;
                }

                rolled.Aggregate(revenue, cost, tax, orders, lines, asAt, stale);
                return rolled;
            })
            .ToList();
    }

    private static DateTimeOffset BucketStart(DateTimeOffset day, AnalyticsPeriod period)
    {
        DateOnly date = DateOnly.FromDateTime(day.UtcDateTime);
        return period switch
        {
            AnalyticsPeriod.Weekly => new DateTimeOffset(
                date.AddDays(-(((int)date.DayOfWeek + 6) % 7)).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            AnalyticsPeriod.Monthly => new DateTimeOffset(
                new DateOnly(date.Year, date.Month, 1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            AnalyticsPeriod.YearToDate => new DateTimeOffset(
                new DateOnly(date.Year, 1, 1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            _ => day,
        };
    }

    private static DateTimeOffset BucketEnd(DateTimeOffset bucketStart, AnalyticsPeriod period)
    {
        DateTime date = bucketStart.UtcDateTime;
        return period switch
        {
            AnalyticsPeriod.Weekly => bucketStart.AddDays(7),
            AnalyticsPeriod.Monthly => new DateTimeOffset(new DateOnly(date.Year, date.Month, 1).AddMonths(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            AnalyticsPeriod.YearToDate => new DateTimeOffset(new DateOnly(date.Year + 1, 1, 1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            _ => bucketStart.AddDays(1),
        };
    }
}
