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
    public void Forecast_strategies_honor_their_options_and_empty_series()
    {
        var options = new ForecastOptions(WindowSize: 2, Alpha: 0.5, SeasonLength: 2);

        new MovingAverageForecastStrategy().Forecast([2m, 4m, 8m], options).Should().Be(6m);
        new ExponentialSmoothingForecastStrategy().Forecast([2m, 4m, 8m], options).Should().Be(5.5m);
        new SeasonalNaiveForecastStrategy().Forecast([2m, 4m, 8m], options).Should().Be(4m);
        new MovingAverageForecastStrategy().Forecast([], options).Should().Be(0m);
        new ExponentialSmoothingForecastStrategy().Forecast([], options).Should().Be(0m);
        new SeasonalNaiveForecastStrategy().Forecast([], options).Should().Be(0m);
    }

    [Fact]
    public void Forecast_rejects_invalid_alpha_and_clamps_moving_average_window()
    {
        var strategy = new ExponentialSmoothingForecastStrategy();
        FluentActions.Invoking(() => strategy.Forecast([1m], new ForecastOptions(Alpha: 0)))
            .Should().Throw<PlanningRuleException>();
        FluentActions.Invoking(() => strategy.Forecast([1m], new ForecastOptions(Alpha: 1.1)))
            .Should().Throw<PlanningRuleException>();

        new MovingAverageForecastStrategy().Forecast([3m, 9m], new ForecastOptions(WindowSize: 99))
            .Should().Be(6m);
    }

    [Fact]
    public void Forecast_math_calculates_variation_and_standard_deviation_edge_cases()
    {
        ForecastMath.CoefficientOfVariation([10m, 10m, 10m]).Should().Be(0m);
        ForecastMath.CoefficientOfVariation([0m, 0m]).Should().Be(0m);
        ForecastMath.StandardDeviation([]).Should().Be(0m);
        ForecastMath.StandardDeviation([5m]).Should().Be(0m);
        ForecastMath.StandardDeviation([2m, 4m]).Should().BeApproximately(1.414213562m, 0.000000001m);
        ForecastMath.ZScore(50m).Should().BeApproximately(0d, 0.000000001d);
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
    public void Replenishment_uses_forecast_top_up_and_deterministic_transfer_tiebreaking()
    {
        Guid firstLocation = Guid.NewGuid();
        Guid secondLocation = Guid.NewGuid();
        var input = new ReplenishmentInput(
            Guid.NewGuid(), null, Guid.NewGuid(), "EA", 10m, 2m, 12m, 0m, 20m, 5,
            [new TransferCandidate(Guid.NewGuid(), firstLocation, 20m, At),
             new TransferCandidate(Guid.NewGuid(), secondLocation, 20m, At)], false);

        ProposedSuggestion result = new ReplenishmentEngine().Propose(input).Single();

        result.Reason.Should().Be(SuggestionReason.TransferSurplus);
        result.Source.Should().Be(SuggestionSource.Transfer);
        result.Quantity.Should().Be(10m);
        result.Candidate!.SourceLocationId.Should().Be(firstLocation.CompareTo(secondLocation) < 0 ? firstLocation : secondLocation);
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

    [Fact]
    public void Markdown_planner_requires_all_three_inclusive_signals()
    {
        var planner = new MarkdownPlanner();
        planner.Evaluate(new MarkdownSignals(AbcClass.C, XyzClass.Z, 20m, 90m))
            .Should().Be(new MarkdownVerdict(true, 25m));
        planner.Evaluate(new MarkdownSignals(AbcClass.B, XyzClass.Y, 20m, 90m))
            .Should().Be(new MarkdownVerdict(false, 0m));
        planner.Evaluate(new MarkdownSignals(null, XyzClass.Z, 21m, 120m))
            .Should().Be(new MarkdownVerdict(false, 0m));
    }

    [Fact]
    public void Markdown_plan_rejects_empty_submission_and_supports_cancel_and_complete()
    {
        var empty = MarkdownPlan.Create(Guid.NewGuid(), Guid.NewGuid(), "MDP-EMPTY", "reason",
            new DateOnly(2026, 2, 1), null);
        FluentActions.Invoking(() => empty.Submit(Guid.NewGuid())).Should().Throw<PlanningRuleException>();

        var plan = MarkdownPlan.Create(Guid.NewGuid(), Guid.NewGuid(), "MDP-002", "reason",
            new DateOnly(2026, 2, 1), null);
        plan.AddLine(Guid.NewGuid(), null, 100m, 25m, "zar", "C-Z", 10m, 120m);
        plan.Submit(Guid.NewGuid());
        plan.ApplyApproval(true);
        plan.RecordActivation(Guid.NewGuid());
        plan.Complete();
        plan.Status.Should().Be(MarkdownPlanStatus.Completed);
        FluentActions.Invoking(plan.Cancel).Should().Throw<PlanningRuleException>();
    }

    [Fact]
    public void Planning_domain_validates_sku_budget_and_date_invariants()
    {
        FluentActions.Invoking(() => DemandHistory.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                null, null, new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 11), 1m, "EA", At))
            .Should().Throw<PlanningRuleException>();
        FluentActions.Invoking(() => OpenToBuyBudget.Create(Guid.NewGuid(), Guid.NewGuid(), 2026, 13,
                null, 100m, "zar")).Should().Throw<PlanningRuleException>();
        FluentActions.Invoking(() => OpenToBuyBudget.Create(Guid.NewGuid(), Guid.NewGuid(), 2026, 1,
                null, -1m, "zar")).Should().Throw<PlanningRuleException>();
        FluentActions.Invoking(() => MarkdownPlan.Create(Guid.NewGuid(), Guid.NewGuid(), "MDP-003", "reason",
                new DateOnly(2026, 2, 2), new DateOnly(2026, 2, 1))).Should().Throw<PlanningRuleException>();

        var budget = OpenToBuyBudget.Create(Guid.NewGuid(), Guid.NewGuid(), 2026, 1, " cat ", 100m, " zar ");
        budget.CategoryCode.Should().Be("cat");
        budget.Currency.Should().Be("ZAR");
        budget.Replan(75m);
        budget.PlannedAmount.Should().Be(75m);
    }
}
