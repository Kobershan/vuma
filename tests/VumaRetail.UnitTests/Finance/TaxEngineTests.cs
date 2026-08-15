using NSubstitute;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Domain.Finance;
using VumaRetail.Finance.Tax;
using static VumaRetail.UnitTests.Finance.FinanceTestContext;

namespace VumaRetail.UnitTests.Finance;

/// <summary>
/// Tax is a rules engine, never a constant (CLAUDE.md §9).
/// </summary>
/// <remarks>
/// The point these tests have to prove is not that 15% arithmetic is right — it is that nothing in
/// the engine knows 15%, or VAT, or South Africa. So the second-rule tests deliberately use a rate
/// and a treatment the `en-ZA` default does not have: if any of it were hard-coded, those are the
/// cases that would fail while the 15% ones passed.
/// </remarks>
public sealed class TaxEngineTests
{
    private readonly ITaxRuleRepository _rules = Substitute.For<ITaxRuleRepository>();

    private TaxEngine Engine => new(_rules);

    private void Effective(TaxRule rule)
        => _rules.FindEffectiveAsync(rule.Code, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(rule);

    [Fact]
    public async Task An_inclusive_rule_extracts_the_tax_from_the_stated_amount()
    {
        // The seeded en-ZA default: 15%, inclusive. R115 stated means R100 net and R15 tax.
        Effective(TaxRule.Define(TenantId, "STANDARD", "Standard rate", 0.15m, TaxTreatment.Inclusive, new DateOnly(2026, 1, 1)));

        TaxCalculation result = await Engine.CalculateAsync("STANDARD", Rand(115m), Today);

        result.NetAmount.Should().Be(Rand(100m));
        result.TaxAmount.Should().Be(Rand(15m));
        result.GrossAmount.Should().Be(Rand(115m));
        result.Rate.Should().Be(0.15m);
    }

    [Fact]
    public async Task An_exclusive_rule_adds_the_tax_on_top_of_the_stated_amount()
    {
        Effective(TaxRule.Define(TenantId, "EXPORT", "Exclusive rate", 0.15m, TaxTreatment.Exclusive, new DateOnly(2026, 1, 1)));

        TaxCalculation result = await Engine.CalculateAsync("EXPORT", Rand(100m), Today);

        result.NetAmount.Should().Be(Rand(100m));
        result.TaxAmount.Should().Be(Rand(15m));
        result.GrossAmount.Should().Be(Rand(115m));
    }

    [Fact]
    public async Task Whether_an_amount_is_inclusive_is_the_rules_decision_not_the_callers()
    {
        // Same code, same stated amount, opposite treatment — and the caller passes nothing to say
        // which. A caller that could choose would be a second place tax policy lives.
        Effective(TaxRule.Define(TenantId, "SAME", "Inclusive", 0.15m, TaxTreatment.Inclusive, new DateOnly(2026, 1, 1)));
        TaxCalculation inclusive = await Engine.CalculateAsync("SAME", Rand(115m), Today);

        Effective(TaxRule.Define(TenantId, "SAME", "Exclusive", 0.15m, TaxTreatment.Exclusive, new DateOnly(2026, 1, 1)));
        TaxCalculation exclusive = await Engine.CalculateAsync("SAME", Rand(115m), Today);

        inclusive.GrossAmount.Should().Be(Rand(115m));
        exclusive.GrossAmount.Should().Be(Rand(132.25m));
    }

    [Fact]
    public async Task A_second_jurisdictions_rate_is_a_data_row_not_a_code_change()
    {
        // 20%, exclusive — neither the rate nor the treatment of the en-ZA default.
        Effective(TaxRule.Define(TenantId, "UK-VAT", "UK standard", 0.20m, TaxTreatment.Exclusive, new DateOnly(2026, 1, 1)));

        TaxCalculation result = await Engine.CalculateAsync("UK-VAT", Rand(100m), Today);

        result.TaxAmount.Should().Be(Rand(20m));
        result.GrossAmount.Should().Be(Rand(120m));
        result.Rate.Should().Be(0.20m);
    }

    [Fact]
    public async Task A_zero_rated_rule_calculates_no_tax_without_being_a_special_case()
    {
        Effective(TaxRule.Define(TenantId, "ZERO", "Zero rated", 0m, TaxTreatment.Exclusive, new DateOnly(2026, 1, 1)));

        TaxCalculation result = await Engine.CalculateAsync("ZERO", Rand(100m), Today);

        result.TaxAmount.Should().Be(Rand(0m));
        result.GrossAmount.Should().Be(Rand(100m));
    }

    [Fact]
    public async Task The_rule_effective_on_the_documents_date_is_the_one_applied()
    {
        // A rate change is a new rule with its own effective dates, so a historical invoice always
        // recalculates to what it actually charged rather than to today's rate.
        TaxRule oldRate = TaxRule.Define(
            TenantId, "STANDARD", "14% until 2026-03-31", 0.14m, TaxTreatment.Inclusive,
            new DateOnly(2025, 1, 1), new DateOnly(2026, 3, 31));
        TaxRule newRate = TaxRule.Define(
            TenantId, "STANDARD", "15% from 2026-04-01", 0.15m, TaxTreatment.Inclusive, new DateOnly(2026, 4, 1));

        _rules.FindEffectiveAsync("STANDARD", new DateOnly(2026, 2, 1), Arg.Any<CancellationToken>()).Returns(oldRate);
        _rules.FindEffectiveAsync("STANDARD", new DateOnly(2026, 6, 15), Arg.Any<CancellationToken>()).Returns(newRate);

        TaxCalculation before = await Engine.CalculateAsync("STANDARD", Rand(114m), new DateOnly(2026, 2, 1));
        TaxCalculation after = await Engine.CalculateAsync("STANDARD", Rand(115m), new DateOnly(2026, 6, 15));

        before.Rate.Should().Be(0.14m);
        before.TaxAmount.Should().Be(Rand(14m));
        after.Rate.Should().Be(0.15m);
        after.TaxAmount.Should().Be(Rand(15m));
    }

    [Fact]
    public async Task A_code_with_no_effective_rule_is_refused_rather_than_defaulted_to_zero()
    {
        // Silently returning zero tax would be the worst possible failure here: it under-charges,
        // under-declares, and looks like a successful calculation.
        _rules.FindEffectiveAsync("MISSING", Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns((TaxRule?)null);

        Func<Task> calculating = () => Engine.CalculateAsync("MISSING", Rand(100m), Today);

        await calculating.Should().ThrowAsync<TaxRuleNotFoundException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_rule_outside_a_sensible_range_is_refused_when_it_is_defined(int daysEndBeforeStart)
    {
        // A rule that ends before it starts can never match anything, so it is refused at definition
        // rather than becoming a code that silently never resolves.
        DateOnly start = new(2026, 6, 1);
        DateOnly end = start.AddDays(daysEndBeforeStart - 1);

        Action defining = () => TaxRule.Define(
            TenantId, "BAD", "Ends before it starts", 0.15m, TaxTreatment.Inclusive, start, end);

        defining.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_negative_rate_is_refused()
    {
        Action defining = () => TaxRule.Define(
            TenantId, "NEGATIVE", "Impossible", -0.15m, TaxTreatment.Inclusive, new DateOnly(2026, 1, 1));

        defining.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void A_rule_is_effective_only_inside_its_own_dates_and_only_while_active()
    {
        TaxRule rule = TaxRule.Define(
            TenantId, "STANDARD", "Standard", 0.15m, TaxTreatment.Inclusive,
            new DateOnly(2026, 4, 1), new DateOnly(2026, 6, 30));

        rule.IsEffectiveOn(new DateOnly(2026, 3, 31)).Should().BeFalse();
        rule.IsEffectiveOn(new DateOnly(2026, 4, 1)).Should().BeTrue();
        rule.IsEffectiveOn(new DateOnly(2026, 6, 30)).Should().BeTrue();
        rule.IsEffectiveOn(new DateOnly(2026, 7, 1)).Should().BeFalse();

        rule.Deactivate();

        rule.IsEffectiveOn(new DateOnly(2026, 5, 1)).Should().BeFalse();
    }
}
