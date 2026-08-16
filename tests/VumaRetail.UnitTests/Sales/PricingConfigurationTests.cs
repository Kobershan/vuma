using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Sales;

namespace VumaRetail.UnitTests.Sales;

/// <summary>
/// The guards on the two configuration aggregates, and the override log they exist to keep honest.
/// </summary>
public sealed class PricingConfigurationTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid StoreId = UuidV7.NewGuid();
    private static readonly Guid ItemId = UuidV7.NewGuid();
    private static readonly Guid VariantId = UuidV7.NewGuid();
    private static readonly Guid OperatorId = UuidV7.NewGuid();
    private static readonly DateOnly Today = new(2026, 8, 16);
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void A_price_list_upper_cases_its_code_and_currency_the_way_a_store_code_does()
    {
        PriceList list = List();

        list.Code.Should().Be("RETAIL");
        list.Currency.Should().Be("ZAR");
        list.IsActive.Should().BeTrue();
    }

    [Fact]
    public void An_effective_window_cannot_end_before_it_begins()
    {
        Action inverted = () => PriceList.Create(
            TenantId, null, "RETAIL", "Shelf prices", "ZAR", PriceListKind.Retail,
            pricesIncludeTax: true, priority: 0, Today, Today.AddDays(-1));

        inverted.Should().Throw<SalesRuleException>()
            .Which.Code.Should().Be("SALES_EFFECTIVE_WINDOW_INVERTED");
    }

    [Fact]
    public void A_price_list_line_is_priced_in_its_list_currency_and_nothing_else()
    {
        // Converting needs an explicit rate and a rate date, which is Finance's job. A price list that
        // silently accepted dollars would be adding rands to dollars one row later.
        PriceList list = List();

        Action foreign = () => list.AddLine(ItemId, null, new Money(9.99m, "USD"), 1m);

        foreign.Should().Throw<SalesRuleException>()
            .Which.Code.Should().Be("SALES_CURRENCY_MISMATCH");
    }

    [Fact]
    public void One_item_cannot_be_priced_twice_at_the_same_quantity_break()
    {
        // Two prices for one break is not a price, and the resolver would have to pick one arbitrarily.
        PriceList list = List();
        list.AddLine(ItemId, null, new Money(12.99m, "ZAR"), 1m);

        Action duplicate = () => list.AddLine(ItemId, null, new Money(11.50m, "ZAR"), 1m);

        duplicate.Should().Throw<SalesConflictException>()
            .Which.Code.Should().Be("SALES_DUPLICATE_PRICE_LIST_LINE");
    }

    [Fact]
    public void A_priced_row_names_exactly_one_of_an_item_or_a_variant()
    {
        PriceList list = List();

        Action neither = () => list.AddLine(null, null, new Money(12.99m, "ZAR"), 1m);
        Action both = () => list.AddLine(ItemId, VariantId, new Money(12.99m, "ZAR"), 1m);

        neither.Should().Throw<SalesRuleException>()
            .Which.Code.Should().Be("SALES_EXACTLY_ONE_ITEM_OR_VARIANT");

        both.Should().Throw<SalesRuleException>()
            .Which.Code.Should().Be("SALES_EXACTLY_ONE_ITEM_OR_VARIANT");
    }

    [Fact]
    public void A_deactivated_list_is_not_effective_and_comes_back_when_it_is_reactivated()
    {
        // Deactivate, never delete (§7 rule 8): a completed sale has to stay explicable next year.
        PriceList list = List();

        list.Deactivate();
        list.IsEffectiveOn(Today).Should().BeFalse();

        list.Activate();
        list.IsEffectiveOn(Today).Should().BeTrue();
    }

    [Fact]
    public void A_promotion_cannot_be_created_with_parameters_its_kind_does_not_use()
    {
        // The flat nullable columns are only honest because a kind can only be built with what it
        // needs. A MultibuyForAmount with no bundle quantity is a promotion that can never fire.
        Action noQuantity = () => Promotion.MultibuyForAmount(
            TenantId, null, "BAD", "Broken", 0m, new Money(50m, "ZAR"), Today, null);

        noQuantity.Should().Throw<SalesRuleException>()
            .Which.Code.Should().Be("SALES_PROMOTION_PARAMETER_MISMATCH");
    }

    [Fact]
    public void A_percentage_outside_zero_to_one_hundred_is_refused()
    {
        Action tooMuch = () => Promotion.PercentageOff(
            TenantId, null, "BAD", "Broken", 101m, Today, null);

        tooMuch.Should().Throw<SalesRuleException>()
            .Which.Code.Should().Be("SALES_PERCENTAGE_OUT_OF_RANGE");
    }

    [Fact]
    public void A_time_window_that_wraps_past_midnight_runs_through_the_small_hours()
    {
        // A late-night garage forecourt is a real shop, and 22:00–02:00 must not read as an empty set.
        Promotion nightShift = Promotion.PercentageOff(TenantId, null, "NIGHT", "Night special", 10m, Today, null);
        nightShift.RestrictToWindow(null, new TimeOnly(22, 0), new TimeOnly(2, 0));

        nightShift.IsLiveAt(Today, new TimeOnly(23, 30)).Should().BeTrue();
        nightShift.IsLiveAt(Today, new TimeOnly(1, 0)).Should().BeTrue();
        nightShift.IsLiveAt(Today, new TimeOnly(12, 0)).Should().BeFalse();
    }

    [Fact]
    public void A_promotions_reward_parameters_are_not_amendable_but_its_ranking_and_window_are()
    {
        // Changing "3 for R50" into "20% off" on the same row would rewrite what every already-priced
        // basket was told it was getting. Amend moves when it runs and how it ranks, and nothing else.
        Promotion promotion = Promotion.MultibuyForAmount(
            TenantId, null, "THREE-FIFTY", "3 for R50", 3m, new Money(50m, "ZAR"), Today, null);

        promotion.Amend("3 for R50 — extended", 7, isExclusive: true, Today, Today.AddDays(14));

        promotion.Name.Should().Be("3 for R50 — extended");
        promotion.Priority.Should().Be(7);
        promotion.IsExclusive.Should().BeTrue();
        promotion.EffectiveTo.Should().Be(Today.AddDays(14));
        promotion.RequiredQuantity.Should().Be(3m);
        promotion.Reward!.Value.Amount.Should().Be(50m);
    }

    [Fact]
    public void An_override_records_both_prices_and_what_the_gap_cost_across_the_quantity()
    {
        PriceOverrideLog entry = PriceOverrideLog.Record(
            TenantId, StoreId, null, null, ItemId, null, OperatorId,
            new Quantity(3m, "EA"), new Money(12.99m, "ZAR"), new Money(10m, "ZAR"),
            "Damaged packaging", Now);

        entry.Variance.Amount.Should().Be(-8.97m);
        entry.Reason.Should().Be("Damaged packaging");
        entry.OperatorUserId.Should().Be(OperatorId);
    }

    [Fact]
    public void An_override_in_two_currencies_is_refused()
    {
        Action mismatched = () => PriceOverrideLog.Record(
            TenantId, StoreId, null, null, ItemId, null, OperatorId,
            new Quantity(1m, "EA"), new Money(12.99m, "ZAR"), new Money(1m, "USD"),
            "Price match", Now);

        mismatched.Should().Throw<SalesRuleException>()
            .Which.Code.Should().Be("SALES_CURRENCY_MISMATCH");
    }

    [Fact]
    public void An_override_with_no_reason_is_refused()
    {
        // The reason is the entire value of the row. An override nobody can investigate is a warning
        // that was written to a table instead of to a log.
        Action noReason = () => PriceOverrideLog.Record(
            TenantId, StoreId, null, null, ItemId, null, OperatorId,
            new Quantity(1m, "EA"), new Money(12.99m, "ZAR"), new Money(10m, "ZAR"), "  ", Now);

        noReason.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task A_tax_inclusive_list_and_a_tax_exclusive_one_reach_the_same_gross()
    {
        // The reason PricesIncludeTax lives on the list rather than on every caller. A shelf price of
        // R115 authored inclusive and a wholesale price of R100 authored exclusive are the same money
        // under a 15% rule — and neither the till nor the resolver has to know which it is holding,
        // because the matched tax rule decides the split (CLAUDE.md §9).
        StubTaxCalculator tax = new(rate: 0.15m);

        TaxCalculation inclusive = await tax.CalculateAsync(
            "STANDARD", new Money(115m, "ZAR"), Today, statedIsInclusive: true);

        TaxCalculation exclusive = await tax.CalculateAsync(
            "STANDARD", new Money(100m, "ZAR"), Today, statedIsInclusive: false);

        inclusive.GrossAmount.Amount.Should().Be(exclusive.GrossAmount.Amount).And.Be(115m);
        inclusive.NetAmount.Amount.Should().Be(exclusive.NetAmount.Amount).And.Be(100m);
        inclusive.TaxAmount.Amount.Should().Be(exclusive.TaxAmount.Amount).And.Be(15m);
    }

    private static PriceList List()
        => PriceList.Create(
            TenantId, null, " retail ", "Shelf prices", "zar", PriceListKind.Retail,
            pricesIncludeTax: true, priority: 0, Today.AddDays(-30), null);

    /// <summary>
    /// A fixed-rate stand-in for Stage 07's tax engine, exercising only the inclusive/exclusive split.
    /// </summary>
    /// <remarks>
    /// The real engine resolves the rule and decides which side of the line the stated amount sits on;
    /// this one is told, because the point of the assertion above is the arithmetic identity rather
    /// than the rule lookup, which Stage 07's own tests already cover against a real database.
    /// </remarks>
    private sealed class StubTaxCalculator(decimal rate)
    {
        public Task<TaxCalculation> CalculateAsync(
            string taxCode, Money statedAmount, DateOnly asOf, bool statedIsInclusive)
        {
            Money net = statedIsInclusive
                ? (statedAmount / (1m + rate)).RoundToCurrencyScale()
                : statedAmount;

            Money gross = statedIsInclusive
                ? statedAmount
                : (statedAmount * (1m + rate)).RoundToCurrencyScale();

            return Task.FromResult(new TaxCalculation(taxCode, net, gross - net, gross, rate));
        }
    }
}
