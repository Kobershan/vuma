using VumaRetail.Application.Planning;
using VumaRetail.Application.Planning.Forecasting;
using VumaRetail.Domain.Planning;

namespace VumaRetail.UnitTests.Planning;

public sealed class PlanningCoreTests
{
    private static readonly DateTimeOffset At = new(2026, 1, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Forecast_engine_computes_moving_average_and_backtest_metrics()
    {
        var engine = new ForecastEngine([
            new MovingAverageForecastStrategy(),
            new ExponentialSmoothingForecastStrategy(),
            new SeasonalNaiveForecastStrategy()]);

        ForecastResult result = engine.Compute([10m, 20m, 30m, 40m], ForecastMethod.MovingAverage,
            new ForecastOptions(HorizonPeriods: 1, WindowSize: 2));

        result.Quantity.Should().Be(35m);
        result.Method.Should().Be(ForecastMethod.MovingAverage);
        result.LowConfidence.Should().BeFalse();
        result.Mape.Should().BeGreaterThan(0m);
    }

    [Fact]
    public void Forecast_engine_flags_thin_and_insufficient_seasonal_history()
    {
        var engine = new ForecastEngine([
            new MovingAverageForecastStrategy(),
            new ExponentialSmoothingForecastStrategy(),
            new SeasonalNaiveForecastStrategy()]);

        engine.Compute([], ForecastMethod.MovingAverage).Should().BeEquivalentTo(
            new ForecastResult(0m, 0m, null, true, ForecastMethod.MovingAverage));
        engine.Compute([12m], ForecastMethod.MovingAverage).LowConfidence.Should().BeTrue();
        engine.Compute(Enumerable.Repeat(12m, 52).ToArray(), ForecastMethod.SeasonalNaive).LowConfidence.Should().BeTrue();
    }

    [Fact]
    public void Forecast_math_skips_zero_actuals_and_rejects_invalid_service_levels()
    {
        ForecastMath.Mape([0m, 10m], [100m, 12m]).Should().Be(0.2m);
        ForecastMath.Bias([0m, 10m], [100m, 12m]).Should().Be(0.2m);
        ForecastMath.Bias([0m], [100m]).Should().BeNull();
        FluentActions.Invoking(() => ForecastMath.ZScore(100m)).Should().Throw<PlanningRuleException>();
    }

    [Fact]
    public void Safety_stock_uses_fallback_until_eight_weeks_and_rejects_short_horizon()
    {
        var calculator = new SafetyStockCalculator();
        SafetyStockOutcome fallback = calculator.Calculate(new SafetyStockInputs(
            [70m, 70m, 70m], 95m, LeadTimeDays: 7, ReviewPeriodDays: 7, ForecastHorizonDays: 14));

        fallback.LowConfidence.Should().BeTrue();
        fallback.Method.Should().Be("fallback");
        fallback.SafetyStock.Should().Be(17.5m);

        FluentActions.Invoking(() => calculator.Calculate(new SafetyStockInputs(
            [70m] , 95m, 7, 7, 13))).Should().Throw<PlanningRuleException>();
    }

    [Fact]
    public void Safety_stock_uses_variance_method_with_full_history()
    {
        SafetyStockOutcome outcome = new SafetyStockCalculator().Calculate(new SafetyStockInputs(
            [60m, 70m, 80m, 70m, 60m, 70m, 80m, 70m], 95m, 7, 7, 14));

        outcome.LowConfidence.Should().BeFalse();
        outcome.Method.Should().Be("variance");
        outcome.DemandVariance.Should().BeGreaterThan(0m);
        outcome.SafetyStock.Should().BeGreaterThan(0m);
    }

    [Fact]
    public void Replenishment_prefers_a_fresh_transfer_that_covers_the_need()
    {
        Guid sourceLocation = Guid.NewGuid();
        ReplenishmentInput input = new(
            Guid.NewGuid(), null, Guid.NewGuid(), "EA", 100m, 20m, 60m, 0m, 80m, 7,
            [new TransferCandidate(Guid.NewGuid(), sourceLocation, 100m, At)], false);

        ProposedSuggestion suggestion = new ReplenishmentEngine().Propose(input).Single();

        suggestion.Source.Should().Be(SuggestionSource.Transfer);
        suggestion.Reason.Should().Be(SuggestionReason.TransferSurplus);
        suggestion.Quantity.Should().Be(60m);
        suggestion.Candidate!.SourceLocationId.Should().Be(sourceLocation);
    }

    [Fact]
    public void Replenishment_falls_back_to_procurement_and_returns_nothing_when_covered()
    {
        var engine = new ReplenishmentEngine();
        ProposedSuggestion procurement = engine.Propose(new ReplenishmentInput(
            Guid.NewGuid(), null, Guid.NewGuid(), "EA", 100m, 20m, 60m, 0m, 80m, 7, [], false)).Single();
        engine.Propose(new ReplenishmentInput(
            Guid.NewGuid(), null, Guid.NewGuid(), "EA", 100m, 20m, 120m, 0m, 80m, 7, [], false))
            .Should().BeEmpty();

        procurement.Source.Should().Be(SuggestionSource.Procurement);
        procurement.Quantity.Should().Be(60m);
    }

    [Fact]
    public void Replenishment_suggestion_is_exactly_once_and_preserves_recommendation_on_amendment()
    {
        var suggestion = ReplenishmentSuggestion.Raise(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, 10m, "EA",
            SuggestionReason.BelowReorderPoint, SuggestionSource.Procurement, null, null, "run-1", false,
            At, At.AddDays(14));
        Guid documentId = Guid.NewGuid();

        suggestion.AmendAndAccept(8m, documentId);

        suggestion.Status.Should().Be(SuggestionStatus.Accepted);
        suggestion.SuggestedQuantity.Should().Be(10m);
        suggestion.AcceptedQuantity.Should().Be(8m);
        suggestion.DownstreamDocumentId.Should().Be(documentId);
        FluentActions.Invoking(() => suggestion.Accept(Guid.NewGuid())).Should().Throw<PlanningRuleException>();
    }

    [Fact]
    public void Markdown_plan_requires_approval_before_activation_and_supports_amendment()
    {
        var plan = MarkdownPlan.Create(Guid.NewGuid(), Guid.NewGuid(), "MDP-001", "slow mover",
            new DateOnly(2026, 2, 1), null);
        plan.AddLine(Guid.NewGuid(), null, 100m, 20m, "zar", "C-Z", 10m, 120m);

        FluentActions.Invoking(() => plan.RecordActivation(Guid.NewGuid())).Should().Throw<PlanningRuleException>();
        plan.Submit(Guid.NewGuid());
        plan.ApplyApproval(true);
        plan.RecordActivation(Guid.NewGuid());

        MarkdownPlan amendment = MarkdownPlan.CreateAmendment(plan, "MDP-002");
        plan.Status.Should().Be(MarkdownPlanStatus.Amended);
        amendment.Version.Should().Be(2);
        amendment.Status.Should().Be(MarkdownPlanStatus.Draft);
    }
}
