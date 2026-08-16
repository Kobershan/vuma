using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Sales;
using VumaRetail.Domain.Sales;

namespace VumaRetail.Application.Sales.Queries;

/// <summary>
/// Asks what something should sell for, and why.
/// </summary>
/// <param name="Request">What is being priced.</param>
/// <remarks>
/// A query, not a command: resolving a price changes nothing. Selling at something other than the
/// answer is <c>RecordPriceOverrideCommand</c>, and the two being separate is what keeps a cashier
/// checking a price off the override log.
/// </remarks>
public sealed record ResolvePriceQuery(PriceResolutionRequest Request) : IQuery<PriceResolution>;

/// <summary>Delegates to the resolver.</summary>
/// <param name="resolver">The price resolver.</param>
public sealed class ResolvePriceQueryHandler(IPriceResolver resolver)
    : IQueryHandler<ResolvePriceQuery, PriceResolution>
{
    /// <inheritdoc />
    public async Task<PriceResolution> HandleAsync(
        ResolvePriceQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await resolver.ResolveAsync(query.Request, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Every price list, for the maintenance screen.</summary>
/// <param name="IncludeInactive">True to include retired lists.</param>
public sealed record ListPriceListsQuery(bool IncludeInactive = false) : IQuery<IReadOnlyList<PriceList>>;

/// <summary>Reads the price lists.</summary>
/// <param name="priceLists">Price list lookup.</param>
public sealed class ListPriceListsQueryHandler(IPriceListRepository priceLists)
    : IQueryHandler<ListPriceListsQuery, IReadOnlyList<PriceList>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<PriceList>> HandleAsync(
        ListPriceListsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await priceLists.ListAsync(query.IncludeInactive, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>One price list, with its lines.</summary>
/// <param name="PriceListId">The list.</param>
public sealed record GetPriceListQuery(Guid PriceListId) : IQuery<PriceList>;

/// <summary>Reads the price list.</summary>
/// <param name="priceLists">Price list lookup.</param>
public sealed class GetPriceListQueryHandler(IPriceListRepository priceLists)
    : IQueryHandler<GetPriceListQuery, PriceList>
{
    /// <inheritdoc />
    public async Task<PriceList> HandleAsync(
        GetPriceListQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await priceLists.FindAsync(query.PriceListId, cancellationToken).ConfigureAwait(false)
            ?? throw new SalesNotFoundException("price list", query.PriceListId);
    }
}

/// <summary>
/// The specials a store is running on a given day — what a shelf-edge label run or a "today's deals"
/// screen reads.
/// </summary>
/// <param name="StoreId">The store, or <c>null</c> for tenant-wide promotions only.</param>
/// <param name="OnDate">The day, store-local.</param>
/// <remarks>
/// Answers the day, not the minute. A happy-hour promotion appears here even outside its time window,
/// because "what is on special today" and "what would fire on this basket right now" are different
/// questions and the second one is <see cref="ResolvePriceQuery"/>.
/// </remarks>
public sealed record ListEffectivePromotionsQuery(Guid? StoreId, DateOnly OnDate)
    : IQuery<IReadOnlyList<Promotion>>;

/// <summary>Reads the day's live promotions.</summary>
/// <param name="promotions">Promotion lookup.</param>
public sealed class ListEffectivePromotionsQueryHandler(IPromotionRepository promotions)
    : IQueryHandler<ListEffectivePromotionsQuery, IReadOnlyList<Promotion>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Promotion>> HandleAsync(
        ListEffectivePromotionsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await promotions
            .ListLiveAsync(query.StoreId, query.OnDate, cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>Every promotion, for the maintenance screen.</summary>
/// <param name="IncludeInactive">True to include retired promotions.</param>
public sealed record ListPromotionsQuery(bool IncludeInactive = false) : IQuery<IReadOnlyList<Promotion>>;

/// <summary>Reads the promotions.</summary>
/// <param name="promotions">Promotion lookup.</param>
public sealed class ListPromotionsQueryHandler(IPromotionRepository promotions)
    : IQueryHandler<ListPromotionsQuery, IReadOnlyList<Promotion>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Promotion>> HandleAsync(
        ListPromotionsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await promotions.ListAsync(query.IncludeInactive, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>One return, with its lines.</summary>
/// <param name="SalesReturnId">The return.</param>
public sealed record GetSalesReturnQuery(Guid SalesReturnId) : IQuery<SalesReturn>;

/// <summary>Reads the return.</summary>
/// <param name="returns">Return lookup.</param>
public sealed class GetSalesReturnQueryHandler(ISalesReturnRepository returns)
    : IQueryHandler<GetSalesReturnQuery, SalesReturn>
{
    /// <inheritdoc />
    public async Task<SalesReturn> HandleAsync(
        GetSalesReturnQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await returns.FindAsync(query.SalesReturnId, cancellationToken).ConfigureAwait(false)
            ?? throw new SalesNotFoundException("sales return", query.SalesReturnId);
    }
}

/// <summary>Every return raised against one sale.</summary>
/// <param name="SaleId">The sale.</param>
/// <remarks>
/// The question a cashier asks before accepting goods back: "has any of this already come back?" The
/// aggregate answers it authoritatively when the line is added; this is the same answer a person can
/// read.
/// </remarks>
public sealed record ListSaleReturnsQuery(Guid SaleId) : IQuery<IReadOnlyList<SalesReturn>>;

/// <summary>Reads the sale's returns.</summary>
/// <param name="returns">Return lookup.</param>
public sealed class ListSaleReturnsQueryHandler(ISalesReturnRepository returns)
    : IQueryHandler<ListSaleReturnsQuery, IReadOnlyList<SalesReturn>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<SalesReturn>> HandleAsync(
        ListSaleReturnsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await returns.ListForSaleAsync(query.SaleId, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Returns completed in a period.</summary>
/// <param name="From">The first day, inclusive.</param>
/// <param name="To">The last day, inclusive.</param>
/// <param name="Limit">How many to return.</param>
public sealed record ListReturnsForPeriodQuery(DateOnly From, DateOnly To, int Limit = 100)
    : IQuery<IReadOnlyList<SalesReturn>>;

/// <summary>Reads the period's returns.</summary>
/// <param name="returns">Return lookup.</param>
public sealed class ListReturnsForPeriodQueryHandler(ISalesReturnRepository returns)
    : IQueryHandler<ListReturnsForPeriodQuery, IReadOnlyList<SalesReturn>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<SalesReturn>> HandleAsync(
        ListReturnsForPeriodQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await returns
            .ListForPeriodAsync(query.From, query.To, query.Limit, cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>
/// Price overrides recorded in a period — the shrinkage report this stage's log exists to make
/// possible.
/// </summary>
/// <param name="From">The first day, inclusive.</param>
/// <param name="To">The last day, inclusive.</param>
/// <param name="OperatorUserId">Narrow to one operator, or <c>null</c> for everybody.</param>
/// <param name="Limit">How many to return.</param>
public sealed record ListPriceOverridesQuery(
    DateOnly From, DateOnly To, Guid? OperatorUserId = null, int Limit = 100)
    : IQuery<IReadOnlyList<PriceOverrideLog>>;

/// <summary>Reads the override log.</summary>
/// <param name="overrides">The append-only override log.</param>
public sealed class ListPriceOverridesQueryHandler(IPriceOverrideLogRepository overrides)
    : IQueryHandler<ListPriceOverridesQuery, IReadOnlyList<PriceOverrideLog>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<PriceOverrideLog>> HandleAsync(
        ListPriceOverridesQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await overrides
            .ListForPeriodAsync(query.From, query.To, query.OperatorUserId, query.Limit, cancellationToken)
            .ConfigureAwait(false);
    }
}
