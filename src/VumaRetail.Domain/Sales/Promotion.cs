using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Sales;

/// <summary>
/// A special: a rule that takes money off a basket, for a while, under stated conditions.
/// </summary>
/// <remarks>
/// <para>
/// <b>A promotion is configuration, not code.</b> Running "3 for R50 on Fridays" is a row in this
/// table and a row in <see cref="PromotionLine"/>; it is never a deployment. That is the whole point of
/// the stage — a shop changes its specials weekly and cannot wait for a release to do it.
/// </para>
/// <para>
/// The reward parameters are deliberately flat nullable columns rather than a polymorphic hierarchy or
/// a JSON blob. Five kinds cover what a South African shop actually runs, the set changes rarely, and
/// a flat row is queryable, diffable and importable in bulk (Stage 11). <see cref="EnsureParametersMatchKind"/>
/// is what keeps the flatness honest — a kind can only be created with the parameters it needs.
/// </para>
/// <para>
/// Monetary parameters follow ADR-067: a nullable amount plus its currency, with a computed
/// <see cref="Money"/> accessor over the pair. EF Core 9 cannot map an optional complex property, and
/// that failure appears at model-building time rather than compile time.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.Bidirectional, ConflictPolicy.CloudWins)]
public sealed class Promotion : Entity
{
    private readonly List<PromotionLine> _lines = [];

    private Promotion(
        Guid tenantId,
        Guid? storeId,
        string code,
        string name,
        PromotionKind kind,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo,
        int priority,
        bool isExclusive)
        : base(tenantId, storeId)
    {
        Code = code;
        Name = name;
        Kind = kind;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        Priority = priority;
        IsExclusive = isExclusive;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private Promotion()
    {
    }

    /// <summary>The promotion's unique code within the tenant, upper-cased.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>The promotion's name, as it should read on a receipt.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>What shape of reward this is.</summary>
    public PromotionKind Kind { get; private set; }

    /// <summary>The percentage off, for <see cref="PromotionKind.PercentageOff"/>. 0–100.</summary>
    public decimal? DiscountPercentage { get; private set; }

    /// <summary>The reward amount's numeric value. Paired with <see cref="RewardCurrency"/> per ADR-067.</summary>
    /// <remarks>
    /// Carries the amount off for <see cref="PromotionKind.AmountOff"/>, the fixed unit price for
    /// <see cref="PromotionKind.FixedPrice"/>, and the bundle price for
    /// <see cref="PromotionKind.MultibuyForAmount"/>. One column because exactly one of them is ever set.
    /// </remarks>
    public decimal? RewardAmount { get; private set; }

    /// <summary>The reward amount's currency, paired with <see cref="RewardAmount"/>.</summary>
    public string? RewardCurrency { get; private set; }

    /// <summary>The reward as money, or <c>null</c> when this kind carries no monetary parameter.</summary>
    public Money? Reward => RewardAmount is { } amount ? new Money(amount, RewardCurrency!) : null;

    /// <summary>
    /// How many units the reward needs — the 3 in "3 for R50", the X in "buy X get Y free". <c>null</c>
    /// for the kinds that apply to every unit.
    /// </summary>
    public decimal? RequiredQuantity { get; private set; }

    /// <summary>The Y in "buy X get Y free".</summary>
    public decimal? FreeQuantity { get; private set; }

    /// <summary>The first day this promotion runs.</summary>
    public DateOnly EffectiveFrom { get; private set; }

    /// <summary>The last day it runs, inclusive, or <c>null</c> for open-ended.</summary>
    public DateOnly? EffectiveTo { get; private set; }

    /// <summary>
    /// The days of the week it runs on, or <c>null</c> for every day — a happy hour or a
    /// Friday-only special.
    /// </summary>
    public PromotionDays? Days { get; private set; }

    /// <summary>The time of day it starts, or <c>null</c> for all day. Store-local time.</summary>
    public TimeOnly? StartsAt { get; private set; }

    /// <summary>The time of day it stops, or <c>null</c> for all day. Store-local time.</summary>
    public TimeOnly? EndsAt { get; private set; }

    /// <summary>
    /// Which promotion applies first when several match. Higher wins; ties break on <see cref="Code"/>
    /// so that the same basket always prices the same way.
    /// </summary>
    public int Priority { get; private set; }

    /// <summary>
    /// True when this promotion, having fired, stops every lower-priority one. A "50% off everything"
    /// clearance is exclusive; a "R2 off bread" is usually not.
    /// </summary>
    public bool IsExclusive { get; private set; }

    /// <summary>True while the promotion may fire. Deactivate, not delete (§7 rule 8).</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>What this promotion applies to.</summary>
    public IReadOnlyList<PromotionLine> Lines => _lines;

    /// <summary>Creates a percentage-off promotion.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The store it runs at, or <c>null</c> for every store.</param>
    /// <param name="code">The promotion code, upper-cased.</param>
    /// <param name="name">The name, as it reads on a receipt.</param>
    /// <param name="percentage">The percentage off, 0–100.</param>
    /// <param name="effectiveFrom">The first day it runs.</param>
    /// <param name="effectiveTo">The last day it runs, or <c>null</c>.</param>
    /// <param name="priority">Which promotion applies first. Higher wins.</param>
    /// <param name="isExclusive">True to stop lower-priority promotions once this fires.</param>
    /// <exception cref="SalesRuleException">The percentage is outside 0–100, or the window is inverted.</exception>
    public static Promotion PercentageOff(
        Guid tenantId,
        Guid? storeId,
        string code,
        string name,
        decimal percentage,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo,
        int priority = 0,
        bool isExclusive = false)
    {
        Promotion promotion = CreateCore(
            tenantId, storeId, code, name, PromotionKind.PercentageOff,
            effectiveFrom, effectiveTo, priority, isExclusive);

        promotion.DiscountPercentage = percentage;
        promotion.EnsureParametersMatchKind();
        return promotion;
    }

    /// <summary>Creates an amount-off-each-unit promotion.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The store it runs at, or <c>null</c> for every store.</param>
    /// <param name="code">The promotion code, upper-cased.</param>
    /// <param name="name">The name, as it reads on a receipt.</param>
    /// <param name="amountOff">How much comes off each unit.</param>
    /// <param name="effectiveFrom">The first day it runs.</param>
    /// <param name="effectiveTo">The last day it runs, or <c>null</c>.</param>
    /// <param name="priority">Which promotion applies first. Higher wins.</param>
    /// <param name="isExclusive">True to stop lower-priority promotions once this fires.</param>
    /// <exception cref="SalesRuleException">The amount is negative, or the window is inverted.</exception>
    public static Promotion AmountOff(
        Guid tenantId,
        Guid? storeId,
        string code,
        string name,
        Money amountOff,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo,
        int priority = 0,
        bool isExclusive = false)
    {
        Promotion promotion = CreateCore(
            tenantId, storeId, code, name, PromotionKind.AmountOff,
            effectiveFrom, effectiveTo, priority, isExclusive);

        promotion.SetReward(amountOff);
        promotion.EnsureParametersMatchKind();
        return promotion;
    }

    /// <summary>Creates a fixed-unit-price promotion — "anything on this shelf, R10".</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The store it runs at, or <c>null</c> for every store.</param>
    /// <param name="code">The promotion code, upper-cased.</param>
    /// <param name="name">The name, as it reads on a receipt.</param>
    /// <param name="unitPrice">What one unit costs while this runs.</param>
    /// <param name="effectiveFrom">The first day it runs.</param>
    /// <param name="effectiveTo">The last day it runs, or <c>null</c>.</param>
    /// <param name="priority">Which promotion applies first. Higher wins.</param>
    /// <param name="isExclusive">True to stop lower-priority promotions once this fires.</param>
    /// <exception cref="SalesRuleException">The price is negative, or the window is inverted.</exception>
    public static Promotion FixedPrice(
        Guid tenantId,
        Guid? storeId,
        string code,
        string name,
        Money unitPrice,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo,
        int priority = 0,
        bool isExclusive = false)
    {
        Promotion promotion = CreateCore(
            tenantId, storeId, code, name, PromotionKind.FixedPrice,
            effectiveFrom, effectiveTo, priority, isExclusive);

        promotion.SetReward(unitPrice);
        promotion.EnsureParametersMatchKind();
        return promotion;
    }

    /// <summary>Creates a multibuy promotion — "3 for R50".</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The store it runs at, or <c>null</c> for every store.</param>
    /// <param name="code">The promotion code, upper-cased.</param>
    /// <param name="name">The name, as it reads on a receipt.</param>
    /// <param name="requiredQuantity">How many make a bundle.</param>
    /// <param name="bundlePrice">What the bundle costs.</param>
    /// <param name="effectiveFrom">The first day it runs.</param>
    /// <param name="effectiveTo">The last day it runs, or <c>null</c>.</param>
    /// <param name="priority">Which promotion applies first. Higher wins.</param>
    /// <param name="isExclusive">True to stop lower-priority promotions once this fires.</param>
    /// <exception cref="SalesRuleException">The quantity or price is not positive, or the window is inverted.</exception>
    public static Promotion MultibuyForAmount(
        Guid tenantId,
        Guid? storeId,
        string code,
        string name,
        decimal requiredQuantity,
        Money bundlePrice,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo,
        int priority = 0,
        bool isExclusive = false)
    {
        Promotion promotion = CreateCore(
            tenantId, storeId, code, name, PromotionKind.MultibuyForAmount,
            effectiveFrom, effectiveTo, priority, isExclusive);

        promotion.RequiredQuantity = requiredQuantity;
        promotion.SetReward(bundlePrice);
        promotion.EnsureParametersMatchKind();
        return promotion;
    }

    /// <summary>Creates a buy-X-get-Y-free promotion.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The store it runs at, or <c>null</c> for every store.</param>
    /// <param name="code">The promotion code, upper-cased.</param>
    /// <param name="name">The name, as it reads on a receipt.</param>
    /// <param name="requiredQuantity">How many must be bought.</param>
    /// <param name="freeQuantity">How many come free with them.</param>
    /// <param name="effectiveFrom">The first day it runs.</param>
    /// <param name="effectiveTo">The last day it runs, or <c>null</c>.</param>
    /// <param name="priority">Which promotion applies first. Higher wins.</param>
    /// <param name="isExclusive">True to stop lower-priority promotions once this fires.</param>
    /// <exception cref="SalesRuleException">A quantity is not positive, or the window is inverted.</exception>
    public static Promotion BuyXGetYFree(
        Guid tenantId,
        Guid? storeId,
        string code,
        string name,
        decimal requiredQuantity,
        decimal freeQuantity,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo,
        int priority = 0,
        bool isExclusive = false)
    {
        Promotion promotion = CreateCore(
            tenantId, storeId, code, name, PromotionKind.BuyXGetYFree,
            effectiveFrom, effectiveTo, priority, isExclusive);

        promotion.RequiredQuantity = requiredQuantity;
        promotion.FreeQuantity = freeQuantity;
        promotion.EnsureParametersMatchKind();
        return promotion;
    }

    /// <summary>Renames the promotion, re-ranks it and moves its effective window.</summary>
    /// <param name="name">The new name, as it reads on a receipt.</param>
    /// <param name="priority">The new priority. Higher applies first.</param>
    /// <param name="isExclusive">True to stop lower-priority promotions once this fires.</param>
    /// <param name="effectiveFrom">The new first day.</param>
    /// <param name="effectiveTo">The new last day, or <c>null</c>.</param>
    /// <exception cref="SalesRuleException">The effective window ends before it begins.</exception>
    /// <remarks>
    /// The reward parameters are deliberately not amendable. Changing "3 for R50" into "20% off" on the
    /// same row would rewrite what every already-priced basket was told it was getting; a changed offer
    /// is a new promotion, and the old one is deactivated. What can move is when it runs and how it
    /// ranks, which is what a manager actually adjusts.
    /// </remarks>
    public void Amend(
        string name, int priority, bool isExclusive, DateOnly effectiveFrom, DateOnly? effectiveTo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (effectiveTo is not null && effectiveTo < effectiveFrom)
        {
            throw SalesRuleException.EffectiveWindowInverted();
        }

        Name = name.Trim();
        Priority = priority;
        IsExclusive = isExclusive;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
    }

    /// <summary>Restricts the promotion to certain days and times — a happy hour.</summary>
    /// <param name="days">The days it runs on, or <c>null</c> for every day.</param>
    /// <param name="startsAt">When it starts each day, store-local, or <c>null</c> for all day.</param>
    /// <param name="endsAt">When it stops each day, store-local, or <c>null</c> for all day.</param>
    public void RestrictToWindow(PromotionDays? days, TimeOnly? startsAt, TimeOnly? endsAt)
    {
        Days = days;
        StartsAt = startsAt;
        EndsAt = endsAt;
    }

    /// <summary>Adds something for the promotion to apply to.</summary>
    /// <param name="itemId">The item, when the promotion targets one.</param>
    /// <param name="itemVariantId">The variant, when it targets one.</param>
    /// <param name="categoryCode">The category, when it targets a whole shelf.</param>
    /// <returns>The new line.</returns>
    /// <exception cref="SalesRuleException">The line targets none of item, variant or category, or more than one.</exception>
    public PromotionLine AddLine(Guid? itemId, Guid? itemVariantId, string? categoryCode)
    {
        PromotionLine created = PromotionLine.Create(
            TenantId, StoreId, Id, itemId, itemVariantId, categoryCode);

        _lines.Add(created);
        return created;
    }

    /// <summary>
    /// True when this promotion may fire at the given local moment — inside its date window, on a day
    /// it runs, and inside its time window.
    /// </summary>
    /// <param name="on">The day, store-local.</param>
    /// <param name="at">The time of day, store-local.</param>
    /// <remarks>
    /// A time window that wraps past midnight (22:00–02:00) is treated as running through the
    /// small hours rather than as an empty window, because a late-night garage forecourt is a real shop.
    /// </remarks>
    public bool IsLiveAt(DateOnly on, TimeOnly at)
    {
        if (!IsActive || on < EffectiveFrom || (EffectiveTo is not null && on > EffectiveTo))
        {
            return false;
        }

        if (Days is { } days && !days.Includes(on.DayOfWeek))
        {
            return false;
        }

        return (StartsAt, EndsAt) switch
        {
            (null, null) => true,
            ({ } start, null) => at >= start,
            (null, { } end) => at <= end,
            ({ } start, { } end) when start <= end => at >= start && at <= end,
            ({ } start, { } end) => at >= start || at <= end,
        };
    }

    /// <summary>True when this promotion applies to the given item, variant or category.</summary>
    /// <param name="itemId">The item being sold, when it has no variants.</param>
    /// <param name="itemVariantId">The variant being sold.</param>
    /// <param name="categoryCode">The category the item sits in, when known.</param>
    /// <remarks>
    /// A promotion with no lines applies to everything — that is how a store-wide clearance is
    /// expressed, and it is why <see cref="IsExclusive"/> exists.
    /// </remarks>
    public bool AppliesTo(Guid? itemId, Guid? itemVariantId, string? categoryCode)
        => _lines.Count == 0
            || _lines.Exists(line => line.Matches(itemId, itemVariantId, categoryCode));

    /// <summary>Retires the promotion. History is retained; nothing is deleted.</summary>
    public void Deactivate() => IsActive = false;

    /// <summary>Brings a retired promotion back into use.</summary>
    public void Activate() => IsActive = true;

    private static Promotion CreateCore(
        Guid tenantId,
        Guid? storeId,
        string code,
        string name,
        PromotionKind kind,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo,
        int priority,
        bool isExclusive)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A promotion must belong to a tenant.", nameof(tenantId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (effectiveTo is not null && effectiveTo < effectiveFrom)
        {
            throw SalesRuleException.EffectiveWindowInverted();
        }

        return new Promotion(
            tenantId,
            storeId,
            code.Trim().ToUpperInvariant(),
            name.Trim(),
            kind,
            effectiveFrom,
            effectiveTo,
            priority,
            isExclusive);
    }

    private void SetReward(Money reward)
    {
        if (reward.IsNegative)
        {
            throw SalesRuleException.PriceMustNotBeNegative();
        }

        RewardAmount = reward.Amount;
        RewardCurrency = reward.Currency;
    }

    private void EnsureParametersMatchKind()
    {
        switch (Kind)
        {
            case PromotionKind.PercentageOff:
                if (DiscountPercentage is not (>= 0m and <= 100m))
                {
                    throw SalesRuleException.PercentageOutOfRange();
                }

                break;

            case PromotionKind.AmountOff:
            case PromotionKind.FixedPrice:
                if (Reward is null)
                {
                    throw SalesRuleException.PromotionParameterMismatch(Kind, "needs a monetary amount");
                }

                break;

            case PromotionKind.MultibuyForAmount:
                if (Reward is null)
                {
                    throw SalesRuleException.PromotionParameterMismatch(Kind, "needs a bundle price");
                }

                if (RequiredQuantity is not > 0m)
                {
                    throw SalesRuleException.PromotionParameterMismatch(
                        Kind, "needs a positive bundle quantity");
                }

                break;

            case PromotionKind.BuyXGetYFree:
                if (RequiredQuantity is not > 0m)
                {
                    throw SalesRuleException.PromotionParameterMismatch(
                        Kind, "needs a positive required quantity");
                }

                if (FreeQuantity is not > 0m)
                {
                    throw SalesRuleException.PromotionParameterMismatch(
                        Kind, "needs a positive free quantity");
                }

                break;

            default:
                throw SalesRuleException.PromotionParameterMismatch(Kind, "is not a kind this build knows");
        }
    }
}

/// <summary>What shape of reward a <see cref="Promotion"/> gives.</summary>
public enum PromotionKind
{
    /// <summary>A percentage off every matching unit.</summary>
    PercentageOff = 0,

    /// <summary>A fixed amount off every matching unit.</summary>
    AmountOff = 1,

    /// <summary>A fixed price per matching unit, whatever the list says.</summary>
    FixedPrice = 2,

    /// <summary>N units for a stated bundle price — "3 for R50".</summary>
    MultibuyForAmount = 3,

    /// <summary>Buy X, get Y free.</summary>
    BuyXGetYFree = 4,
}

/// <summary>The days of the week a <see cref="Promotion"/> runs on.</summary>
/// <remarks>
/// A flags enum rather than a child table: a day-of-week restriction is one small closed set that is
/// read on every price resolution, and joining a table to discover that a special runs on Fridays
/// would be a query per line.
/// </remarks>
[Flags]
public enum PromotionDays
{
    /// <summary>No days. Not a useful configuration; present so the flags enum has a zero.</summary>
    None = 0,

    /// <summary>Monday.</summary>
    Monday = 1 << 0,

    /// <summary>Tuesday.</summary>
    Tuesday = 1 << 1,

    /// <summary>Wednesday.</summary>
    Wednesday = 1 << 2,

    /// <summary>Thursday.</summary>
    Thursday = 1 << 3,

    /// <summary>Friday.</summary>
    Friday = 1 << 4,

    /// <summary>Saturday.</summary>
    Saturday = 1 << 5,

    /// <summary>Sunday.</summary>
    Sunday = 1 << 6,

    /// <summary>Monday to Friday.</summary>
    Weekdays = Monday | Tuesday | Wednesday | Thursday | Friday,

    /// <summary>Saturday and Sunday.</summary>
    Weekend = Saturday | Sunday,

    /// <summary>Every day.</summary>
    EveryDay = Weekdays | Weekend,
}

/// <summary>Helpers for <see cref="PromotionDays"/>.</summary>
public static class PromotionDaysExtensions
{
    /// <summary>True when the set includes the given day.</summary>
    /// <param name="days">The set of days a promotion runs on.</param>
    /// <param name="day">The day to test.</param>
    public static bool Includes(this PromotionDays days, DayOfWeek day)
        => days.HasFlag(ToFlag(day));

    private static PromotionDays ToFlag(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => PromotionDays.Monday,
        DayOfWeek.Tuesday => PromotionDays.Tuesday,
        DayOfWeek.Wednesday => PromotionDays.Wednesday,
        DayOfWeek.Thursday => PromotionDays.Thursday,
        DayOfWeek.Friday => PromotionDays.Friday,
        DayOfWeek.Saturday => PromotionDays.Saturday,
        _ => PromotionDays.Sunday,
    };
}
