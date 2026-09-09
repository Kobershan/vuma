using VumaRetail.Application.Planning.Forecasting;
using VumaRetail.Domain.Planning;

namespace VumaRetail.Application.Planning;

/// <summary>Inputs to a safety-stock calculation.</summary>
/// <param name="WeeklyDemand">Weekly demand history, oldest first. Gaps are explicit zeros.</param>
/// <param name="ServiceLevelPercent">The service-level target, 0–100 exclusive.</param>
/// <param name="LeadTimeDays">Supplier lead time in days.</param>
/// <param name="ReviewPeriodDays">Review period in days.</param>
/// <param name="ForecastHorizonDays">How far the forecast covers, in days.</param>
/// <param name="FallbackBufferPercent">Buffer for the minimum-history fallback, e.g. 0.25m.</param>
public sealed record SafetyStockInputs(
    IReadOnlyList<decimal> WeeklyDemand,
    decimal ServiceLevelPercent,
    int LeadTimeDays,
    int ReviewPeriodDays,
    int ForecastHorizonDays,
    decimal FallbackBufferPercent = 0.25m);

/// <summary>The outcome of a safety-stock calculation.</summary>
/// <param name="SafetyStock">Buffer stock in units.</param>
/// <param name="ReorderPoint">Lead-time demand plus safety stock, in units.</param>
/// <param name="LeadTimeDemandMean">Mean demand over the lead time.</param>
/// <param name="DemandVariance">Weekly demand variance used.</param>
/// <param name="HistoryWeeks">Weeks of history used.</param>
/// <param name="LowConfidence">True when the minimum-history fallback was used.</param>
/// <param name="Method"><c>variance</c> or <c>fallback</c>.</param>
public sealed record SafetyStockOutcome(
    decimal SafetyStock,
    decimal ReorderPoint,
    decimal LeadTimeDemandMean,
    decimal DemandVariance,
    int HistoryWeeks,
    bool LowConfidence,
    string Method);

/// <summary>Pure safety-stock calculator — no repository, no I/O.</summary>
public interface ISafetyStockCalculator
{
    /// <summary>Calculates safety stock and the reorder point.</summary>
    SafetyStockOutcome Calculate(SafetyStockInputs inputs);
}

/// <summary>
/// Variance-based safety stock with a minimum-history fallback (ADR-149).
/// </summary>
/// <remarks>
/// Full history (≥ 8 weeks): SS = z × σ<sub>weekly</sub> × √(lead-time days / 7),
/// ROP = mean daily demand × lead-time days + SS. Below 8 weeks: SS = lead-time days × mean
/// daily demand × buffer, flagged <c>LowConfidence</c>. No NaN, no infinities, no divide-by-zero:
/// zero variance yields zero buffer, and an empty series yields zeros flagged low-confidence.
/// </remarks>
public sealed class SafetyStockCalculator : ISafetyStockCalculator
{
    /// <summary>Weeks of history required for the variance method.</summary>
    public const int MinimumHistoryWeeks = 8;

    /// <inheritdoc />
    public SafetyStockOutcome Calculate(SafetyStockInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        if (inputs.ForecastHorizonDays < inputs.LeadTimeDays + inputs.ReviewPeriodDays)
        {
            throw PlanningRuleException.HorizonTooShort(inputs.ForecastHorizonDays, inputs.LeadTimeDays + inputs.ReviewPeriodDays);
        }

        IReadOnlyList<decimal> series = inputs.WeeklyDemand;
        int weeks = series.Count;
        decimal meanDaily = weeks == 0 ? 0m : series.Sum() / (weeks * 7m);
        decimal leadTimeMean = meanDaily * inputs.LeadTimeDays;

        if (weeks < MinimumHistoryWeeks)
        {
            decimal fallback = inputs.LeadTimeDays * meanDaily * inputs.FallbackBufferPercent;

            return new SafetyStockOutcome(
                fallback < 0m ? 0m : fallback,
                leadTimeMean + (fallback < 0m ? 0m : fallback),
                leadTimeMean,
                0m,
                weeks,
                true,
                "fallback");
        }

        decimal variance = SampleVariance(series);
        double z = ForecastMath.ZScore(inputs.ServiceLevelPercent);
        double leadTimeWeeks = inputs.LeadTimeDays / 7d;
        decimal safety = (decimal)z * Sqrt(variance) * Sqrt((decimal)leadTimeWeeks);

        if (safety < 0m)
        {
            safety = 0m;
        }

        return new SafetyStockOutcome(
            safety,
            leadTimeMean + safety,
            leadTimeMean,
            variance,
            weeks,
            false,
            "variance");
    }

    private static decimal SampleVariance(IReadOnlyList<decimal> series)
    {
        if (series.Count < 2)
        {
            return 0m;
        }

        decimal mean = series.Average();
        double sum = series.Sum(value => Math.Pow((double)(value - mean), 2));

        return (decimal)(sum / (series.Count - 1));
    }

    private static decimal Sqrt(decimal value)
        => value <= 0m ? 0m : (decimal)Math.Sqrt((double)value);
}

