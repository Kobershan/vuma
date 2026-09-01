namespace VumaRetail.Contracts.Sales;

/// <summary>The id of something the <c>sales</c> module just created.</summary>
/// <param name="Id">The new row's id.</param>
public sealed record SalesIdResponse(Guid Id);

/// <summary>Creates a price list.</summary>
/// <param name="Code">The list code, unique per tenant.</param>
/// <param name="Name">The list's name.</param>
/// <param name="Currency">The ISO 4217 currency every line is denominated in.</param>
/// <param name="Kind">Retail, Wholesale or Staff.</param>
/// <param name="PricesIncludeTax">True when the prices are authored tax-inclusive, as a shelf price is.</param>
/// <param name="Priority">Which list wins when several price the same item. Higher wins.</param>
/// <param name="EffectiveFrom">The first day it may be used.</param>
/// <param name="EffectiveTo">The last day, or <c>null</c> for open-ended.</param>
/// <param name="StoreId">The store it is scoped to, or <c>null</c> for tenant-wide.</param>
public sealed record CreatePriceListRequest(
    string Code,
    string Name,
    string Currency,
    string Kind,
    bool PricesIncludeTax,
    int Priority,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo = null,
    Guid? StoreId = null);

/// <summary>Renames a price list, re-ranks it and moves its effective window.</summary>
/// <param name="Name">The new name.</param>
/// <param name="Priority">The new priority.</param>
/// <param name="EffectiveFrom">The new first day.</param>
/// <param name="EffectiveTo">The new last day, or <c>null</c>.</param>
public sealed record AmendPriceListRequest(
    string Name, int Priority, DateOnly EffectiveFrom, DateOnly? EffectiveTo = null);

/// <summary>Puts a price on a list, or reprices the break already there.</summary>
/// <param name="ItemId">The item, when it has no variants.</param>
/// <param name="ItemVariantId">The variant.</param>
/// <param name="UnitPrice">What one unit sells for.</param>
/// <param name="Currency">The ISO 4217 currency, which must be the list's.</param>
/// <param name="MinimumQuantity">The quantity this price starts applying at. <c>1</c> for the ordinary price.</param>
public sealed record SetPriceListLineRequest(
    Guid? ItemId,
    Guid? ItemVariantId,
    decimal UnitPrice,
    string Currency,
    decimal MinimumQuantity = 1m);

/// <summary>One price on a list, as returned by the API.</summary>
/// <param name="Id">The line's id.</param>
/// <param name="ItemId">The item priced, when it has no variants.</param>
/// <param name="ItemVariantId">The variant priced.</param>
/// <param name="UnitPrice">What one unit sells for.</param>
/// <param name="MinimumQuantity">The quantity this price starts applying at.</param>
public sealed record PriceListLineResponse(
    Guid Id,
    Guid? ItemId,
    Guid? ItemVariantId,
    decimal UnitPrice,
    decimal MinimumQuantity);

/// <summary>A price list, as returned by the API.</summary>
/// <param name="Id">The list's id.</param>
/// <param name="Code">The list code.</param>
/// <param name="Name">The list's name.</param>
/// <param name="Currency">The ISO 4217 currency.</param>
/// <param name="Kind">Retail, Wholesale or Staff.</param>
/// <param name="PricesIncludeTax">True when the prices are authored tax-inclusive.</param>
/// <param name="Priority">Which list wins when several price the same item.</param>
/// <param name="EffectiveFrom">The first day it may be used.</param>
/// <param name="EffectiveTo">The last day, or <c>null</c>.</param>
/// <param name="IsActive">False once retired. Retired, never deleted.</param>
/// <param name="StoreId">The store it is scoped to, or <c>null</c> for tenant-wide.</param>
/// <param name="Lines">Its prices.</param>
public sealed record PriceListResponse(
    Guid Id,
    string Code,
    string Name,
    string Currency,
    string Kind,
    bool PricesIncludeTax,
    int Priority,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    bool IsActive,
    Guid? StoreId,
    IReadOnlyList<PriceListLineResponse> Lines);

/// <summary>Creates a special.</summary>
/// <param name="Code">The promotion code, unique per tenant.</param>
/// <param name="Name">The name, as it reads on a receipt.</param>
/// <param name="Kind">PercentageOff, AmountOff, FixedPrice, MultibuyForAmount or BuyXGetYFree.</param>
/// <param name="EffectiveFrom">The first day it runs.</param>
/// <param name="EffectiveTo">The last day, or <c>null</c> for open-ended.</param>
/// <param name="DiscountPercentage">The percentage off, for PercentageOff.</param>
/// <param name="RewardAmount">The amount off, fixed price, or bundle price, depending on the kind.</param>
/// <param name="Currency">The ISO 4217 currency of <paramref name="RewardAmount"/>.</param>
/// <param name="RequiredQuantity">The 3 in "3 for R50", the X in "buy X get Y free".</param>
/// <param name="FreeQuantity">The Y in "buy X get Y free".</param>
/// <param name="Priority">Which promotion applies first. Higher wins.</param>
/// <param name="IsExclusive">True to stop lower-priority promotions once this fires.</param>
/// <param name="Days">The days it runs on, for example <c>Weekdays</c> or <c>Friday, Saturday</c>.</param>
/// <param name="StartsAt">When it starts each day, store-local, or <c>null</c> for all day.</param>
/// <param name="EndsAt">When it stops each day, store-local, or <c>null</c> for all day.</param>
/// <param name="StoreId">The store it runs at, or <c>null</c> for every store.</param>
public sealed record CreatePromotionRequest(
    string Code,
    string Name,
    string Kind,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo = null,
    decimal? DiscountPercentage = null,
    decimal? RewardAmount = null,
    string? Currency = null,
    decimal? RequiredQuantity = null,
    decimal? FreeQuantity = null,
    int Priority = 0,
    bool IsExclusive = false,
    string? Days = null,
    TimeOnly? StartsAt = null,
    TimeOnly? EndsAt = null,
    Guid? StoreId = null);

/// <summary>Renames a promotion, re-ranks it and moves its windows.</summary>
/// <param name="Name">The new name.</param>
/// <param name="Priority">The new priority.</param>
/// <param name="IsExclusive">Whether it stops lower-priority promotions.</param>
/// <param name="EffectiveFrom">The new first day.</param>
/// <param name="EffectiveTo">The new last day, or <c>null</c>.</param>
/// <param name="Days">The days it runs on, or <c>null</c> for every day.</param>
/// <param name="StartsAt">When it starts each day, or <c>null</c> for all day.</param>
/// <param name="EndsAt">When it stops each day, or <c>null</c> for all day.</param>
public sealed record AmendPromotionRequest(
    string Name,
    int Priority,
    bool IsExclusive,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo = null,
    string? Days = null,
    TimeOnly? StartsAt = null,
    TimeOnly? EndsAt = null);

/// <summary>Adds something for a promotion to apply to.</summary>
/// <param name="ItemId">The item, when the promotion targets one.</param>
/// <param name="ItemVariantId">The variant, when it targets one.</param>
/// <param name="CategoryCode">The category, when it targets a whole shelf.</param>
public sealed record AddPromotionLineRequest(
    Guid? ItemId = null, Guid? ItemVariantId = null, string? CategoryCode = null);

/// <summary>What a promotion applies to, as returned by the API.</summary>
/// <param name="Id">The line's id.</param>
/// <param name="ItemId">The item targeted.</param>
/// <param name="ItemVariantId">The variant targeted.</param>
/// <param name="CategoryCode">The category targeted.</param>
public sealed record PromotionLineResponse(
    Guid Id, Guid? ItemId, Guid? ItemVariantId, string? CategoryCode);

/// <summary>A promotion, as returned by the API.</summary>
/// <param name="Id">The promotion's id.</param>
/// <param name="Code">The promotion code.</param>
/// <param name="Name">The name, as it reads on a receipt.</param>
/// <param name="Kind">What shape of reward it is.</param>
/// <param name="DiscountPercentage">The percentage off, when that is the kind.</param>
/// <param name="RewardAmount">The monetary parameter, when the kind has one.</param>
/// <param name="RewardCurrency">That amount's ISO 4217 currency.</param>
/// <param name="RequiredQuantity">How many the reward needs.</param>
/// <param name="FreeQuantity">How many come free.</param>
/// <param name="EffectiveFrom">The first day it runs.</param>
/// <param name="EffectiveTo">The last day, or <c>null</c>.</param>
/// <param name="Days">The days it runs on, or <c>null</c> for every day.</param>
/// <param name="StartsAt">When it starts each day, or <c>null</c>.</param>
/// <param name="EndsAt">When it stops each day, or <c>null</c>.</param>
/// <param name="Priority">Which promotion applies first.</param>
/// <param name="IsExclusive">Whether it stops lower-priority promotions.</param>
/// <param name="IsActive">False once retired. Retired, never deleted.</param>
/// <param name="StoreId">The store it runs at, or <c>null</c> for every store.</param>
/// <param name="Lines">What it applies to. Empty means everything.</param>
public sealed record PromotionResponse(
    Guid Id,
    string Code,
    string Name,
    string Kind,
    decimal? DiscountPercentage,
    decimal? RewardAmount,
    string? RewardCurrency,
    decimal? RequiredQuantity,
    decimal? FreeQuantity,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string? Days,
    TimeOnly? StartsAt,
    TimeOnly? EndsAt,
    int Priority,
    bool IsExclusive,
    bool IsActive,
    Guid? StoreId,
    IReadOnlyList<PromotionLineResponse> Lines);

/// <summary>One promotion that fired on a resolved price.</summary>
/// <param name="PromotionId">The promotion.</param>
/// <param name="Code">Its code.</param>
/// <param name="Name">Its name, as it should read on the screen and the receipt.</param>
/// <param name="Kind">What shape of reward it was.</param>
/// <param name="DiscountAmount">What it took off the line.</param>
/// <param name="WasClamped">True when it was cut back so the line could not go below zero.</param>
public sealed record AppliedPromotionResponse(
    Guid PromotionId,
    string Code,
    string Name,
    string Kind,
    decimal DiscountAmount,
    bool WasClamped);

/// <summary>What something should sell for, and why.</summary>
/// <param name="PriceListId">The list the price came off.</param>
/// <param name="PriceListCode">That list's code.</param>
/// <param name="PriceListLineId">The exact quantity break that won.</param>
/// <param name="PricesIncludeTax">True when the winning list is authored tax-inclusive.</param>
/// <param name="UnitPrice">
/// What one unit costs before promotions, at full precision. Pass this straight into
/// <c>POST /pos/sales/{saleId}/lines</c> — it is deliberately not rounded, so the till rounds once on
/// the extended amount.
/// </param>
/// <param name="ExtendedPrice">Unit price times quantity, before promotions.</param>
/// <param name="DiscountAmount">Everything the promotions took off. Pass this as the line's discount.</param>
/// <param name="NetPayable">What the line comes to, rounded once. The number to put on a screen.</param>
/// <param name="Currency">The ISO 4217 currency.</param>
/// <param name="Promotions">Every promotion that fired, in the order it was applied.</param>
/// <param name="Explanation">How the price was arrived at, in words a cashier can read out.</param>
public sealed record PriceResolutionResponse(
    Guid? PriceListId,
    string? PriceListCode,
    Guid? PriceListLineId,
    bool PricesIncludeTax,
    decimal UnitPrice,
    decimal ExtendedPrice,
    decimal DiscountAmount,
    decimal NetPayable,
    string Currency,
    IReadOnlyList<AppliedPromotionResponse> Promotions,
    string Explanation);

/// <summary>Raises a return against a completed sale.</summary>
/// <param name="SaleId">The sale the goods came off.</param>
/// <param name="Reason">Why they came back.</param>
/// <param name="RefundTenderType">How the refund is being given — Cash, Card, Voucher or MobileMoney.</param>
public sealed record CreateSalesReturnRequest(Guid SaleId, string Reason, string RefundTenderType);

/// <summary>Puts one of the original sale's lines onto a draft return.</summary>
/// <param name="SaleLineId">The original line the goods came off.</param>
/// <param name="Quantity">How much is coming back.</param>
/// <param name="UnitOfMeasure">The unit it is counted in, which must be the original line's.</param>
public sealed record AddSalesReturnLineRequest(Guid SaleLineId, decimal Quantity, string UnitOfMeasure);

/// <summary>One line coming back, as returned by the API.</summary>
/// <param name="Id">The line's id.</param>
/// <param name="SaleLineId">The original sale line.</param>
/// <param name="ItemId">The item returned.</param>
/// <param name="ItemVariantId">The variant returned.</param>
/// <param name="Description">What the original receipt called it.</param>
/// <param name="Quantity">How much is coming back.</param>
/// <param name="UnitOfMeasure">The unit it is counted in.</param>
/// <param name="OriginalQuantity">How much the original line sold.</param>
/// <param name="PreviouslyReturnedQuantity">How much had already come back when this line was raised.</param>
/// <param name="UnitPrice">What one unit was actually charged at.</param>
/// <param name="TaxCode">The tax code the original line was rung up under.</param>
/// <param name="Net">The refund excluding tax.</param>
/// <param name="Tax">The tax coming back, pro-rata off the original line's stored tax.</param>
/// <param name="Gross">What the customer gets back for this line.</param>
/// <param name="StockReturn">Pending, Posted or Refused.</param>
/// <param name="StockReturnNote">Why the stock receipt was refused, when it was.</param>
public sealed record SalesReturnLineResponse(
    Guid Id,
    Guid SaleLineId,
    Guid? ItemId,
    Guid? ItemVariantId,
    string Description,
    decimal Quantity,
    string UnitOfMeasure,
    decimal OriginalQuantity,
    decimal PreviouslyReturnedQuantity,
    decimal UnitPrice,
    string TaxCode,
    decimal Net,
    decimal Tax,
    decimal Gross,
    string StockReturn,
    string? StockReturnNote);

/// <summary>A return, as returned by the API.</summary>
/// <param name="Id">The return's id.</param>
/// <param name="ReturnNumber">The credit slip number.</param>
/// <param name="SaleId">The sale it is against.</param>
/// <param name="Status">Draft, Completed or Cancelled.</param>
/// <param name="LocationId">The stock location the goods come back into.</param>
/// <param name="CustomerId">The customer, when the original sale identified one.</param>
/// <param name="Currency">The ISO 4217 currency, bound from the original sale.</param>
/// <param name="Reason">Why the goods came back.</param>
/// <param name="RefundTenderType">How the refund is given.</param>
/// <param name="AuthorisedByUserId">Who authorised it.</param>
/// <param name="Net">The refund excluding tax.</param>
/// <param name="Tax">The tax coming back.</param>
/// <param name="Gross">What the customer gets back.</param>
/// <param name="RaisedAt">When it was raised, UTC.</param>
/// <param name="CompletedAt">When it completed, or <c>null</c>.</param>
/// <param name="CancelledAt">When it was cancelled, or <c>null</c>.</param>
/// <param name="Lines">What is coming back.</param>
public sealed record SalesReturnResponse(
    Guid Id,
    string ReturnNumber,
    Guid SaleId,
    string Status,
    Guid LocationId,
    Guid? CustomerId,
    string Currency,
    string Reason,
    string RefundTenderType,
    Guid AuthorisedByUserId,
    decimal Net,
    decimal Tax,
    decimal Gross,
    DateTimeOffset RaisedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? CancelledAt,
    IReadOnlyList<SalesReturnLineResponse> Lines);

/// <summary>What a completed return hands back to the counter.</summary>
/// <param name="SalesReturnId">The return.</param>
/// <param name="ReturnNumber">The credit slip number.</param>
/// <param name="Net">The refund excluding tax.</param>
/// <param name="Tax">The tax coming back.</param>
/// <param name="Gross">What to hand the customer.</param>
/// <param name="Currency">The ISO 4217 currency.</param>
/// <param name="RefundTenderType">How the refund is given.</param>
/// <param name="StockReturnsRefused">
/// How many lines completed without putting stock back. Zero on a normal return; anything else is a
/// reconciliation the store owes itself (ADR-073).
/// </param>
public sealed record SalesReturnCompletionResponse(
    Guid SalesReturnId,
    string ReturnNumber,
    decimal Net,
    decimal Tax,
    decimal Gross,
    string Currency,
    string RefundTenderType,
    int StockReturnsRefused);

/// <summary>Records that an operator sold something at a price the resolver did not give them.</summary>
/// <param name="ItemId">The item, when it has no variants.</param>
/// <param name="ItemVariantId">The variant.</param>
/// <param name="Quantity">How much was sold at the overridden price.</param>
/// <param name="UnitOfMeasure">The unit it was sold in.</param>
/// <param name="ResolvedUnitPrice">What the resolver said one unit should cost.</param>
/// <param name="ActualUnitPrice">What one unit was actually sold for.</param>
/// <param name="Currency">The ISO 4217 currency both prices are in.</param>
/// <param name="Reason">Why. Required — an override with no reason is the one nobody can investigate.</param>
/// <param name="SaleId">The sale it happened on, when there was one by then.</param>
/// <param name="SaleLineId">The line it happened on, when the line already existed.</param>
public sealed record RecordPriceOverrideRequest(
    Guid? ItemId,
    Guid? ItemVariantId,
    decimal Quantity,
    string UnitOfMeasure,
    decimal ResolvedUnitPrice,
    decimal ActualUnitPrice,
    string Currency,
    string Reason,
    Guid? SaleId = null,
    Guid? SaleLineId = null);

/// <summary>One recorded price override.</summary>
/// <param name="Id">The log entry's id.</param>
/// <param name="SaleId">The sale it happened on, when there was one.</param>
/// <param name="SaleLineId">The line it happened on, when there was one.</param>
/// <param name="ItemId">The item sold off-price.</param>
/// <param name="ItemVariantId">The variant sold off-price.</param>
/// <param name="OperatorUserId">Who did it.</param>
/// <param name="Quantity">How much was sold at the overridden price.</param>
/// <param name="UnitOfMeasure">The unit it was sold in.</param>
/// <param name="ResolvedUnitPrice">What the resolver said.</param>
/// <param name="ActualUnitPrice">What was charged.</param>
/// <param name="Variance">What the override cost the shop across the whole quantity. Negative is the normal direction.</param>
/// <param name="Currency">The ISO 4217 currency.</param>
/// <param name="Reason">Why, in the operator's words.</param>
/// <param name="OccurredAt">When, UTC.</param>
public sealed record PriceOverrideResponse(
    Guid Id,
    Guid? SaleId,
    Guid? SaleLineId,
    Guid? ItemId,
    Guid? ItemVariantId,
    Guid OperatorUserId,
    decimal Quantity,
    string UnitOfMeasure,
    decimal ResolvedUnitPrice,
    decimal ActualUnitPrice,
    decimal Variance,
    string Currency,
    string Reason,
    DateTimeOffset OccurredAt);
