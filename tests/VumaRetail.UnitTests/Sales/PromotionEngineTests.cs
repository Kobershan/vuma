using VumaRetail.Application.Abstractions.Sales;
using VumaRetail.Application.Sales.Pricing;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Sales;

namespace VumaRetail.UnitTests.Sales;

/// <summary>
/// The promotions engine's arithmetic, its ordering and its two safety rules.
/// </summary>
/// <remarks>
/// Table-driven, as <c>docs/TESTING.md</c> §3 asks for, which is only possible because the engine is
/// pure: every case here is a row and a call, with no fixture and no database. The boundaries are the
/// point — one below a quantity break, exactly on it, one above — because those are where a pricing
/// engine is wrong in a way nobody notices until a customer counts their change.
/// </remarks>
public sealed class PromotionEngineTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid ItemId = UuidV7.NewGuid();
    private static readonly Guid OtherItemId = UuidV7.NewGuid();
    private static readonly DateOnly Friday = new(2026, 8, 14);
    private static readonly TimeOnly Midday = new(12, 0);

    /// <summary>The specials this file prices against, named so a theory row reads as a sentence.</summary>
    public enum Special
    {
        /// <summary>10% off every matching unit.</summary>
        PercentageOffTen,

        /// <summary>R2 off every matching unit.</summary>
        AmountOffTwo,

        /// <summary>R5 off every matching unit.</summary>
        AmountOffFive,

        /// <summary>R20 off every matching unit — more than most lines are worth, for the clamp case.</summary>
        AmountOffTwenty,

        /// <summary>Anything on the shelf, R10.</summary>
        FixedPriceTen,

        /// <summary>Three for R50.</summary>
        MultibuyThreeForFifty,

        /// <summary>Buy two, get one free.</summary>
        BuyTwoGetOneFree,
    }

    public static TheoryData<Special, decimal, decimal, decimal> RewardArithmetic => new()
    {
        // Special,                        unit price, quantity, discount across the whole line.
        { Special.PercentageOffTen,        50m,        2m,       10.00m },
        { Special.AmountOffTwo,            50m,        3m,        6.00m },

        // R12 each, fixed at R10: three units save R6.
        { Special.FixedPriceTen,           12m,        3m,        6.00m },

        // Already below the fixed price — a promotion never raises a price (business rule 3).
        { Special.FixedPriceTen,            8m,        3m,        0.00m },

        // 3 for R50 at R20 each: seven units make two bundles, each saving R10. The seventh stays at R20.
        { Special.MultibuyThreeForFifty,   20m,        7m,       20.00m },
        { Special.MultibuyThreeForFifty,   20m,        2m,        0.00m },
        { Special.MultibuyThreeForFifty,   20m,        3m,       10.00m },

        // Bundle priced above the shelf price saves nothing rather than costing the customer more.
        { Special.MultibuyThreeForFifty,   10m,        3m,        0.00m },

        // Buy 2 get 1 free: a group is three units, so six units earn two free ones at R15.
        { Special.BuyTwoGetOneFree,        15m,        6m,       30.00m },
        { Special.BuyTwoGetOneFree,        15m,        2m,        0.00m },
        { Special.BuyTwoGetOneFree,        15m,        3m,       15.00m },
        { Special.BuyTwoGetOneFree,        15m,        5m,       15.00m },
    };

    [Theory]
    [MemberData(nameof(RewardArithmetic))]
    public void Each_kind_takes_off_what_it_advertises(
        Special special, decimal unitPrice, decimal quantity, decimal expectedDiscount)
    {
        PromotionOutcome outcome = PromotionEngine.Apply(
            Context(unitPrice, quantity), [Configured(special)]);

        outcome.DiscountAmount.Amount.Should().Be(expectedDiscount);
    }

    [Fact]
    public void A_promotion_that_takes_off_nothing_is_not_reported_as_having_fired()
    {
        // "3 for R50" on a basket of two is the ordinary case, not an error — and a receipt that named
        // a special which saved the customer nothing is a support call.
        PromotionOutcome outcome = PromotionEngine.Apply(
            Context(20m, 2m), [Configured(Special.MultibuyThreeForFifty)]);

        outcome.Applied.Should().BeEmpty();
        outcome.DiscountAmount.IsZero.Should().BeTrue();
    }

    [Fact]
    public void Promotions_stack_against_what_is_left_in_priority_order()
    {
        // R5 off each of two units leaves R90, then 10% of R90 is R9 — not 10% of R100. Order is what
        // Priority buys, and the two sequences give different answers.
        PromotionOutcome outcome = PromotionEngine.Apply(
            Context(50m, 2m),
            [
                Configured(Special.PercentageOffTen, priority: 1),
                Configured(Special.AmountOffFive, priority: 5),
            ]);

        outcome.Applied.Select(applied => applied.Code).Should()
            .ContainInOrder("AMOUNTOFFFIVE", "PERCENTAGEOFFTEN");

        outcome.Applied[0].DiscountAmount.Amount.Should().Be(10.00m);
        outcome.Applied[1].DiscountAmount.Amount.Should().Be(9.00m);
        outcome.DiscountAmount.Amount.Should().Be(19.00m);
    }

    [Fact]
    public void An_exclusive_promotion_that_fires_stops_every_lower_priority_one()
    {
        PromotionOutcome outcome = PromotionEngine.Apply(
            Context(50m, 2m),
            [
                Configured(Special.PercentageOffTen, priority: 1),
                Configured(Special.AmountOffFive, priority: 5, isExclusive: true),
            ]);

        outcome.Applied.Should().ContainSingle();
        outcome.Applied[0].Code.Should().Be("AMOUNTOFFFIVE");
        outcome.DiscountAmount.Amount.Should().Be(10.00m);
    }

    [Fact]
    public void The_same_basket_always_yields_the_same_price_whatever_order_the_candidates_arrive_in()
    {
        // Two terminals reading the same configuration must charge the same price, and neither of them
        // controls the order rows come back in. Ties on priority break on code, which is unique per
        // tenant, so the ordering is total rather than merely stable.
        List<Promotion> candidates =
        [
            Configured(Special.PercentageOffTen, priority: 3),
            Configured(Special.AmountOffFive, priority: 3),
            Configured(Special.FixedPriceTen, priority: 1),
        ];

        decimal expected = PromotionEngine.Apply(Context(50m, 2m), candidates).DiscountAmount.Amount;

        for (int seed = 0; seed < 25; seed++)
        {
            // Seeded rather than Random.Shared, so a failure is reproducible from the test output
            // rather than being a flake somebody re-runs until it passes.
            Random shuffler = new(seed);
            List<Promotion> shuffled = [.. candidates.OrderBy(_ => shuffler.Next())];

            PromotionEngine.Apply(Context(50m, 2m), shuffled).DiscountAmount.Amount
                .Should().Be(expected, "the same basket must price the same however the rows arrive");
        }
    }

    [Fact]
    public void A_stacked_set_that_would_go_below_zero_clamps_and_says_that_it_clamped()
    {
        // Business rule 4. A till that pays a customer to leave is a defect; a promotion that quietly
        // gave less than it advertised is a support call nobody can reproduce, so the clamp is reported.
        PromotionOutcome outcome = PromotionEngine.Apply(
            Context(10m, 1m),
            [
                Configured(Special.AmountOffFive, priority: 9),
                Configured(Special.AmountOffTwenty, priority: 5),
            ]);

        outcome.DiscountAmount.Amount.Should().Be(10.00m);
        outcome.Applied[1].WasClamped.Should().BeTrue();
        outcome.Applied[1].DiscountAmount.Amount.Should().Be(5.00m);
    }

    [Fact]
    public void A_promotion_outside_its_day_or_time_window_does_not_fire()
    {
        Promotion happyHour = Configured(Special.PercentageOffTen);
        happyHour.RestrictToWindow(PromotionDays.Friday, new TimeOnly(17, 0), new TimeOnly(19, 0));

        PromotionEngine.Apply(Context(50m, 1m), [happyHour]).Applied.Should().BeEmpty();

        PromotionEngine.Apply(
            new PromotionContext(ItemId, null, null, 1m, new Money(50m, "ZAR"), Friday, new TimeOnly(18, 0)),
            [happyHour])
            .Applied.Should().ContainSingle();
    }

    [Fact]
    public void A_promotion_with_no_lines_applies_to_everything_and_one_with_lines_only_to_those()
    {
        Promotion clearance = Configured(Special.PercentageOffTen);

        Promotion targeted = Configured(Special.AmountOffFive);
        targeted.AddLine(OtherItemId, null, null);

        PromotionOutcome outcome = PromotionEngine.Apply(Context(50m, 1m), [clearance, targeted]);

        outcome.Applied.Should().ContainSingle();
        outcome.Applied[0].Code.Should().Be("PERCENTAGEOFFTEN");
    }

    [Fact]
    public void A_category_targeted_promotion_matches_the_category_the_caller_supplies()
    {
        Promotion shelf = Configured(Special.PercentageOffTen);
        shelf.AddLine(null, null, "DAIRY");

        PromotionEngine.Apply(
            new PromotionContext(ItemId, null, "dairy", 1m, new Money(50m, "ZAR"), Friday, Midday),
            [shelf])
            .Applied.Should().ContainSingle();

        PromotionEngine.Apply(Context(50m, 1m), [shelf]).Applied.Should().BeEmpty();
    }

    [Fact]
    public void A_promotion_rewarded_in_another_currency_is_skipped_rather_than_thrown()
    {
        // §4.13 is the record of what a currency mismatch costs when it reaches a till as an exception:
        // the terminal bricked and stayed bricked. A misconfigured special should cost the shop the
        // special, not the shift.
        Promotion foreign = Promotion.AmountOff(
            TenantId, null, "USD-OFF", "Dollar special", new Money(5m, "USD"), Friday, null);

        PromotionOutcome outcome = PromotionEngine.Apply(Context(50m, 1m), [foreign]);

        outcome.Applied.Should().BeEmpty();
        outcome.DiscountAmount.Currency.Should().Be("ZAR");
    }

    [Fact]
    public void A_quantity_that_is_not_positive_is_refused()
    {
        Action pricing = () => PromotionEngine.Apply(Context(50m, 0m), []);

        pricing.Should().Throw<SalesRuleException>()
            .Which.Code.Should().Be("SALES_QUANTITY_MUST_BE_POSITIVE");
    }

    private static PromotionContext Context(decimal unitPrice, decimal quantity)
        => new(ItemId, null, null, quantity, new Money(unitPrice, "ZAR"), Friday, Midday);

    /// <summary>
    /// Builds one of the named specials. The code is the enum member, which keeps the ordinal tiebreak
    /// in the ordering test readable — <c>AMOUNTOFFFIVE</c> sorts before <c>PERCENTAGEOFFTEN</c>.
    /// </summary>
    private static Promotion Configured(Special special, int priority = 0, bool isExclusive = false)
    {
        string code = special.ToString().ToUpperInvariant();

        return special switch
        {
            Special.PercentageOffTen => Promotion.PercentageOff(
                TenantId, null, code, "10% off", 10m, Friday, null, priority, isExclusive),

            Special.AmountOffTwo => Promotion.AmountOff(
                TenantId, null, code, "R2 off each", new Money(2m, "ZAR"), Friday, null, priority, isExclusive),

            Special.AmountOffFive => Promotion.AmountOff(
                TenantId, null, code, "R5 off each", new Money(5m, "ZAR"), Friday, null, priority, isExclusive),

            Special.AmountOffTwenty => Promotion.AmountOff(
                TenantId, null, code, "R20 off each", new Money(20m, "ZAR"), Friday, null, priority, isExclusive),

            Special.FixedPriceTen => Promotion.FixedPrice(
                TenantId, null, code, "Anything R10", new Money(10m, "ZAR"), Friday, null, priority, isExclusive),

            Special.MultibuyThreeForFifty => Promotion.MultibuyForAmount(
                TenantId, null, code, "3 for R50", 3m, new Money(50m, "ZAR"), Friday, null, priority, isExclusive),

            _ => Promotion.BuyXGetYFree(
                TenantId, null, code, "Buy 2 get 1 free", 2m, 1m, Friday, null, priority, isExclusive),
        };
    }
}
