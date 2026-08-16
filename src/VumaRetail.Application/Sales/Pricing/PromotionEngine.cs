using VumaRetail.Application.Abstractions.Sales;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Sales;

namespace VumaRetail.Application.Sales.Pricing;

/// <summary>What is being priced, as much of it as the promotion rules can see.</summary>
/// <param name="ItemId">The item, when it has no variants.</param>
/// <param name="ItemVariantId">The variant.</param>
/// <param name="CategoryCode">The category the item sits in, when the caller knows it.</param>
/// <param name="Quantity">How many are being sold. Must be positive.</param>
/// <param name="UnitPrice">What one unit costs before any promotion — the price list's answer.</param>
/// <param name="OnDate">The day, store-local.</param>
/// <param name="AtTime">The time of day, store-local.</param>
public sealed record PromotionContext(
    Guid? ItemId,
    Guid? ItemVariantId,
    string? CategoryCode,
    decimal Quantity,
    Money UnitPrice,
    DateOnly OnDate,
    TimeOnly AtTime);

/// <summary>What the promotions did to a line.</summary>
/// <param name="DiscountAmount">Everything they took off, across the whole quantity.</param>
/// <param name="Applied">Each promotion that fired, in the order it was applied.</param>
public sealed record PromotionOutcome(Money DiscountAmount, IReadOnlyList<AppliedPromotion> Applied);

/// <summary>
/// Applies a tenant's specials to one line. Pure, synchronous, and completely free of I/O.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purity is the design, not an accident.</b> <c>docs/TESTING.md</c> §3 asks for table-driven tests
/// across the money failure modes, and a pricing engine that reached a database could not have them —
/// every case would need a fixture, so there would be twenty cases instead of two hundred, and the
/// boundaries would go untested. Everything this class needs is passed in; the repository work happens
/// one layer up, in <see cref="PriceResolver"/>.
/// </para>
/// <para>
/// <b>The same basket always yields the same price.</b> Candidates are ordered by
/// <see cref="Promotion.Priority"/> descending and then by <see cref="Promotion.Code"/> ordinal, which
/// is a total order because a code is unique per tenant. Nothing downstream depends on the order rows
/// came back in, and a test shuffles the candidate list and asserts the answer does not move. Without
/// that, two terminals reading the same configuration could charge different prices and neither would
/// be wrong from where it was standing.
/// </para>
/// <para>
/// <b>Promotions stack against what is left, not against the original price.</b> A "R5 off" followed by
/// "10% off" takes 10% of the already-reduced amount. That is why <see cref="Promotion.Priority"/>
/// exists and why order is worth pinning down: the two sequences give different answers, and the shop
/// gets to decide which one it runs rather than discovering it.
/// </para>
/// </remarks>
public static class PromotionEngine
{
    /// <summary>Applies every promotion that fires, in order.</summary>
    /// <param name="context">What is being priced.</param>
    /// <param name="candidates">
    /// The promotions to consider. Normally the day's live set for the store, from
    /// <c>IPromotionRepository.ListLiveAsync</c>; the engine filters them again itself, because the
    /// time-of-day window and the item match cannot be done in SQL.
    /// </param>
    /// <returns>The total discount and the promotions behind it.</returns>
    /// <exception cref="SalesRuleException">The quantity is not positive.</exception>
    public static PromotionOutcome Apply(PromotionContext context, IEnumerable<Promotion> candidates)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(candidates);

        if (context.Quantity <= 0m)
        {
            throw SalesRuleException.QuantityMustBePositive();
        }

        Money extended = context.UnitPrice * context.Quantity;
        Money running = extended;
        List<AppliedPromotion> applied = [];

        foreach (Promotion promotion in Ordered(candidates, context))
        {
            // Recomputed each round rather than carried, so a multibuy that follows a percentage-off
            // bundles the reduced price. Money's division rounds to its own 4-place scale, which is two
            // places finer than any currency this ever settles at — the rounding that matters happens
            // once, on the extended amount, in the caller.
            Money effectiveUnit = running / context.Quantity;
            Money wanted = DiscountFor(promotion, context.Quantity, effectiveUnit, running);

            if (wanted.Amount <= 0m)
            {
                // Configured, live, applicable — and worth nothing on this line. A "3 for R50" on a
                // basket of two is the ordinary case, not an error.
                continue;
            }

            // Business rule 4: a stacked set that would take the line below zero clamps, and says so.
            // A till that pays a customer to leave is a defect; a promotion that quietly gave less than
            // it advertised is a support call nobody can reproduce.
            bool clamped = wanted > running;
            Money actual = clamped ? running : wanted;

            applied.Add(new AppliedPromotion(
                promotion.Id, promotion.Code, promotion.Name, promotion.Kind, actual, clamped));

            running -= actual;

            if (promotion.IsExclusive)
            {
                // A "50% off everything" clearance is not meant to be stacked on top of. Note this
                // stops only *lower-priority* promotions: anything above it has already been applied,
                // which is what the priority number is for.
                break;
            }
        }

        return new PromotionOutcome(extended - running, applied);
    }

    private static IEnumerable<Promotion> Ordered(IEnumerable<Promotion> candidates, PromotionContext context)
        => candidates
            .Where(promotion => IsApplicable(promotion, context))
            .OrderByDescending(promotion => promotion.Priority)
            .ThenBy(promotion => promotion.Code, StringComparer.Ordinal);

    private static bool IsApplicable(Promotion promotion, PromotionContext context)
    {
        if (!promotion.IsLiveAt(context.OnDate, context.AtTime))
        {
            return false;
        }

        if (!promotion.AppliesTo(context.ItemId, context.ItemVariantId, context.CategoryCode))
        {
            return false;
        }

        // A promotion whose reward is denominated in another currency is not applicable rather than an
        // error. Money arithmetic across currencies throws by design, and §4.13 is the record of what
        // happens when a currency mismatch reaches a till as an exception instead of as a decision:
        // the terminal bricks. A misconfigured special should cost the shop the special, not the shift.
        return promotion.Reward is not { } reward
            || string.Equals(reward.Currency, context.UnitPrice.Currency, StringComparison.Ordinal);
    }

    private static Money DiscountFor(
        Promotion promotion, decimal quantity, Money effectiveUnit, Money running)
        => promotion.Kind switch
        {
            PromotionKind.PercentageOff => running * (promotion.DiscountPercentage!.Value / 100m),

            // Off each unit, so it scales with quantity — unlike a percentage, which already does.
            PromotionKind.AmountOff => promotion.Reward!.Value * quantity,

            // "Anything on this shelf, R10." Never raises a price: an item already below the fixed
            // price keeps its lower one (business rule 3), which is the Max below.
            PromotionKind.FixedPrice => Max(
                running - (promotion.Reward!.Value * quantity), Money.Zero(running.Currency)),

            PromotionKind.MultibuyForAmount => MultibuyDiscount(promotion, quantity, effectiveUnit),

            PromotionKind.BuyXGetYFree => BuyXGetYFreeDiscount(promotion, quantity, effectiveUnit),

            _ => Money.Zero(running.Currency),
        };

    /// <summary>"3 for R50" — whole bundles only; the remainder stays at the shelf price.</summary>
    private static Money MultibuyDiscount(Promotion promotion, decimal quantity, Money effectiveUnit)
    {
        decimal required = promotion.RequiredQuantity!.Value;
        decimal bundles = decimal.Floor(quantity / required);

        if (bundles <= 0m)
        {
            return Money.Zero(effectiveUnit.Currency);
        }

        Money perBundleSaving = (effectiveUnit * required) - promotion.Reward!.Value;

        // A bundle price above the shelf price saves nothing rather than costing the customer more.
        return Max(perBundleSaving * bundles, Money.Zero(effectiveUnit.Currency));
    }

    /// <summary>
    /// "Buy 2 get 1 free" — a group is X + Y units, and only whole groups earn the free ones.
    /// </summary>
    /// <remarks>
    /// Buying X and taking Y away free means the customer leaves with X + Y for the price of X, so five
    /// units on a buy-2-get-1 gives one free unit and two left at full price. The alternative reading —
    /// that buying X alone earns Y — hands out free stock the customer never picked up.
    /// </remarks>
    private static Money BuyXGetYFreeDiscount(Promotion promotion, decimal quantity, Money effectiveUnit)
    {
        decimal groupSize = promotion.RequiredQuantity!.Value + promotion.FreeQuantity!.Value;
        decimal groups = decimal.Floor(quantity / groupSize);

        return groups <= 0m
            ? Money.Zero(effectiveUnit.Currency)
            : effectiveUnit * (groups * promotion.FreeQuantity!.Value);
    }

    private static Money Max(Money left, Money right) => left > right ? left : right;
}
