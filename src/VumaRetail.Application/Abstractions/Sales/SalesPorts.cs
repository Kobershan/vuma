using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Sales;

namespace VumaRetail.Application.Abstractions.Sales;

/// <summary>
/// What is being priced: a stock-keeping unit, a quantity, a store, and the moment it is being sold at.
/// </summary>
/// <remarks>
/// A request rather than eight parameters because every one of them is load-bearing and half of them
/// are optional — a positional call would be a row of nulls at the call site, and the two
/// <see cref="Guid"/>? item fields next to each other are exactly the pair somebody transposes.
/// </remarks>
/// <param name="ItemId">The item, when it has no variants. Exactly one of this and <paramref name="ItemVariantId"/>.</param>
/// <param name="ItemVariantId">The variant. Exactly one of this and <paramref name="ItemId"/>.</param>
/// <param name="CategoryCode">
/// The category the item sits in, when the caller knows it. Only promotions targeting a whole shelf
/// read it; a request that omits it simply never matches a category-targeted promotion.
/// </param>
/// <param name="Quantity">How many are being sold. Drives quantity breaks and every multi-unit promotion.</param>
/// <param name="StoreId">The store, or <c>null</c> for a tenant-wide question.</param>
/// <param name="OnDate">The day, store-local — normally the document date.</param>
/// <param name="AtTime">The time of day, store-local. Only a happy-hour promotion reads it.</param>
/// <param name="Currency">
/// The currency the answer must come back in. A price list denominated in anything else is refused
/// rather than converted (business rule 10) — converting needs a rate and a rate date, which is
/// Finance's job.
/// </param>
public sealed record PriceResolutionRequest(
    Guid? ItemId,
    Guid? ItemVariantId,
    string? CategoryCode,
    decimal Quantity,
    Guid? StoreId,
    DateOnly OnDate,
    TimeOnly AtTime,
    string Currency);

/// <summary>One promotion that fired, and what it took off.</summary>
/// <param name="PromotionId">The promotion.</param>
/// <param name="Code">Its code.</param>
/// <param name="Name">Its name, as it should read on the screen and the receipt.</param>
/// <param name="Kind">What shape of reward it was.</param>
/// <param name="DiscountAmount">What it took off the line, across the whole quantity.</param>
/// <param name="WasClamped">
/// True when this promotion wanted to take off more than was left and was cut back to zero
/// (business rule 4). A till that pays a customer to leave is a defect, and a promotion silently
/// producing less than it advertised is a support call — so the clamp is reported, not swallowed.
/// </param>
public sealed record AppliedPromotion(
    Guid PromotionId,
    string Code,
    string Name,
    PromotionKind Kind,
    Money DiscountAmount,
    bool WasClamped);

/// <summary>
/// What an item should sell for, and — just as importantly — why.
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="Explanation"/> is not optional and is not a log line</b> (business rule 8). A cashier
/// who cannot tell a customer why the till says R18.99 will override it, and an override is a real cost
/// that shows up in <see cref="PriceOverrideLog"/> rather than in the pricing configuration where the
/// actual mistake is. Making the reasoning part of the answer is what stops the pricing engine being a
/// black box the shop floor works around.
/// </para>
/// <para>
/// <b>The shape is chosen to make business rule 2 unbreakable by the caller.</b> The resolver returns a
/// <see cref="UnitPrice"/> at full precision and a <see cref="DiscountAmount"/> for the whole line, which
/// is exactly the pair <c>AddSaleLineCommand</c> already takes — so POS multiplies and rounds once, on
/// the extended amount, using the code it already had. Had this returned a rounded unit price, every
/// caller would multiply a rounded number by a quantity and §4.14's cent would be back.
/// </para>
/// </remarks>
/// <param name="PriceListId">The list the price came off, or <c>null</c> when nothing priced it.</param>
/// <param name="PriceListCode">That list's code, for the explanation.</param>
/// <param name="PriceListLineId">The exact line — the quantity break that won.</param>
/// <param name="PricesIncludeTax">
/// True when the winning list is authored tax-inclusive, as a South African shelf price is. Passed on
/// so a caller can tell the tax engine what it is holding; the matched tax rule still decides the split.
/// </param>
/// <param name="UnitPrice">What one unit costs before promotions, at full precision. Never rounded here.</param>
/// <param name="ExtendedPrice"><see cref="UnitPrice"/> times the quantity, before promotions.</param>
/// <param name="DiscountAmount">Everything the promotions took off, across the whole quantity.</param>
/// <param name="NetPayable">
/// What the line comes to — extended less discount, rounded once to the currency's scale. This is the
/// number to put on a screen; it is not what is stored on a sale line, which POS derives itself.
/// </param>
/// <param name="Promotions">Every promotion that fired, in the order it was applied.</param>
/// <param name="Explanation">How the price was arrived at, in words a cashier can read out.</param>
public sealed record PriceResolution(
    Guid? PriceListId,
    string? PriceListCode,
    Guid? PriceListLineId,
    bool PricesIncludeTax,
    Money UnitPrice,
    Money ExtendedPrice,
    Money DiscountAmount,
    Money NetPayable,
    IReadOnlyList<AppliedPromotion> Promotions,
    string Explanation);

/// <summary>
/// Works out what something should sell for, under the tenant's price lists and live promotions.
/// </summary>
/// <remarks>
/// <para>
/// The port ADR-072 was waiting for. Stage 09's <c>SaleLine.UnitPrice</c> is still supplied and stored
/// as sold — a weighed, open-price or manually reduced line needs that path forever — and this is what
/// the till calls to get the number it supplies. <b>No Stage 09 file changes shape.</b>
/// </para>
/// <para>
/// It sits in <c>Application.Abstractions.Sales</c> and is implemented in <c>Application/Sales</c>,
/// which is exactly the <see cref="Finance.ITaxCalculator"/> pattern Stage 09 established: a
/// cross-module call goes through a published port so the calling module depends on a contract rather
/// than on the other module's internals.
/// </para>
/// </remarks>
public interface IPriceResolver
{
    /// <summary>Resolves a price, with its explanation.</summary>
    /// <param name="request">What is being priced.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="SalesNotFoundException">Nothing prices that item at that store on that day.</exception>
    /// <exception cref="SalesRuleException">
    /// The winning list is denominated in a different currency than the request asked for
    /// (business rule 10) — refused with a code the caller can act on, never a 500.
    /// </exception>
    Task<PriceResolution> ResolveAsync(
        PriceResolutionRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Reads and writes <see cref="PriceList"/> aggregates.</summary>
/// <remarks>
/// Repositories return tracked entities and never commit; the unit of work is the pipeline's
/// (<c>CLAUDE.md</c> §7 rule 2). There is no <c>AddLine</c> here for the same reason
/// <c>ISaleRepository</c> has none: a line reached without its list is a line the list's rules never
/// saw.
/// </remarks>
public interface IPriceListRepository
{
    /// <summary>Finds a list with its lines loaded, or <c>null</c>.</summary>
    /// <param name="priceListId">The list.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<PriceList?> FindAsync(Guid priceListId, CancellationToken cancellationToken = default);

    /// <summary>Finds a list by its code, with its lines loaded.</summary>
    /// <param name="code">The code, case-insensitive.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<PriceList?> FindByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>True when a list already uses that code in this tenant.</summary>
    /// <param name="code">The code, case-insensitive.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>Every list, newest effective first. Bounded by how many a tenant maintains.</summary>
    /// <param name="includeInactive">True to include retired lists.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<PriceList>> ListAsync(
        bool includeInactive, CancellationToken cancellationToken = default);

    /// <summary>
    /// The lists that could price something at a store on a day, highest priority first, with the
    /// lines for one stock-keeping unit loaded.
    /// </summary>
    /// <param name="itemId">The item, when it has no variants.</param>
    /// <param name="itemVariantId">The variant.</param>
    /// <param name="storeId">The store, or <c>null</c> for tenant-wide only.</param>
    /// <param name="onDate">The day the list must be effective on.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <remarks>
    /// Narrowed to one stock-keeping unit on purpose: resolution happens once per scanned line, and
    /// loading every line of a fifty-thousand-row list to find one price would make the till slower
    /// with every product the shop adds.
    /// </remarks>
    Task<IReadOnlyList<PriceList>> ListCandidatesAsync(
        Guid? itemId,
        Guid? itemVariantId,
        Guid? storeId,
        DateOnly onDate,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a new list.</summary>
    /// <param name="priceList">The list.</param>
    void Add(PriceList priceList);
}

/// <summary>Reads and writes <see cref="Promotion"/> aggregates.</summary>
public interface IPromotionRepository
{
    /// <summary>Finds a promotion with its lines loaded, or <c>null</c>.</summary>
    /// <param name="promotionId">The promotion.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<Promotion?> FindAsync(Guid promotionId, CancellationToken cancellationToken = default);

    /// <summary>True when a promotion already uses that code in this tenant.</summary>
    /// <param name="code">The code, case-insensitive.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every active promotion whose date window covers a day at a store, with its lines loaded.
    /// </summary>
    /// <param name="storeId">The store, or <c>null</c> for tenant-wide only.</param>
    /// <param name="onDate">The day.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <remarks>
    /// Deliberately not narrowed to one item. A promotion with no lines applies to everything, and a
    /// category-targeted one matches on a code rather than an id, so neither can be filtered in SQL
    /// without the query knowing the catalogue. The day's live set is small — a shop runs tens of
    /// specials, not thousands — and the engine filters it in memory, which is also what makes the
    /// engine testable without a database.
    /// </remarks>
    Task<IReadOnlyList<Promotion>> ListLiveAsync(
        Guid? storeId, DateOnly onDate, CancellationToken cancellationToken = default);

    /// <summary>Every promotion, newest first, for the maintenance screen.</summary>
    /// <param name="includeInactive">True to include retired promotions.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<Promotion>> ListAsync(
        bool includeInactive, CancellationToken cancellationToken = default);

    /// <summary>Adds a new promotion.</summary>
    /// <param name="promotion">The promotion.</param>
    void Add(Promotion promotion);
}

/// <summary>Reads and writes <see cref="SalesReturn"/> aggregates.</summary>
public interface ISalesReturnRepository
{
    /// <summary>
    /// Finds a return with its lines loaded, or <c>null</c>. Loading the lines is not optional — the
    /// aggregate recomputes its totals from them.
    /// </summary>
    /// <param name="salesReturnId">The return.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<SalesReturn?> FindAsync(Guid salesReturnId, CancellationToken cancellationToken = default);

    /// <summary>Every return raised against one sale, oldest first.</summary>
    /// <param name="saleId">The sale.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<SalesReturn>> ListForSaleAsync(
        Guid saleId, CancellationToken cancellationToken = default);

    /// <summary>Returns completed in a period, newest first.</summary>
    /// <param name="from">The first day, inclusive.</param>
    /// <param name="to">The last day, inclusive.</param>
    /// <param name="limit">How many to return.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<SalesReturn>> ListForPeriodAsync(
        DateOnly from, DateOnly to, int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// How much of one original sale line has already come back, across every return document except
    /// the one being built.
    /// </summary>
    /// <param name="saleLineId">The original sale line.</param>
    /// <param name="excludingReturnId">The document being built, so its own lines are not counted twice.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <remarks>
    /// Cancelled returns are excluded — nothing came back on them. Drafts are <em>included</em>: a
    /// return sitting on another terminal's screen is goods the shop has already accepted back over the
    /// counter, and counting it only once it completes is how the same item gets refunded twice.
    /// </remarks>
    Task<decimal> SumReturnedQuantityAsync(
        Guid saleLineId, Guid? excludingReturnId, CancellationToken cancellationToken = default);

    /// <summary>Adds a new return.</summary>
    /// <param name="salesReturn">The return.</param>
    void Add(SalesReturn salesReturn);
}

/// <summary>Reads and appends the immutable <see cref="PriceOverrideLog"/>.</summary>
/// <remarks>
/// No update or remove path, the same way <c>IReceiptPrintRepository</c> has none. The audit
/// interceptor would refuse a modification anyway; this interface gives it no surface to be attempted
/// through.
/// </remarks>
public interface IPriceOverrideLogRepository
{
    /// <summary>Overrides recorded in a period, newest first.</summary>
    /// <param name="from">The first day, inclusive.</param>
    /// <param name="to">The last day, inclusive.</param>
    /// <param name="operatorUserId">Narrow to one operator, or <c>null</c> for everybody.</param>
    /// <param name="limit">How many to return.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<PriceOverrideLog>> ListForPeriodAsync(
        DateOnly from,
        DateOnly to,
        Guid? operatorUserId,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>Appends an override. Nothing added through this method is ever updated or removed.</summary>
    /// <param name="entry">The override.</param>
    void Add(PriceOverrideLog entry);
}
