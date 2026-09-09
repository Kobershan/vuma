using VumaRetail.Domain.Planning;

namespace VumaRetail.Application.Planning.Forecasting;

/// <summary>Options for a forecast computation.</summary>
/// <param name="HorizonPeriods">How many periods ahead the forecast covers.</param>
/// <param name="WindowSize">Moving-average window, in periods.</param>
/// <param name="Alpha">Exponential-smoothing factor, 0–1 exclusive of 0.</param>
/// <param name="SeasonLength">Seasonal-naive cycle length, in periods (52 weekly).</param>
public sealed record ForecastOptions(
    int HorizonPeriods = 4,
    int WindowSize = 4,
    double Alpha = 0.3,
    int SeasonLength = 52);

/// <summary>The result of a forecast computation.</summary>
/// <param name="Quantity">The forecasted quantity per period over the horizon.</param>
/// <param name="Mape">Mean absolute percentage error from in-sample back-testing. Zero when not computable.</param>
/// <param name="Bias">Mean signed percentage error, or <c>null</c> when not computable.</param>
/// <param name="LowConfidence">True when the input was too thin to trust (empty, one point, insufficient seasons).</param>
/// <param name="Method">The method that produced it.</param>
public sealed record ForecastResult(
    decimal Quantity,
    decimal Mape,
    decimal? Bias,
    bool LowConfidence,
    ForecastMethod Method);

/// <summary>
/// Pure, deterministic, synchronous forecast engine — no repository, no database, no I/O.
/// </summary>
public interface IForecastEngine
{
    /// <summary>Computes a forecast for weekly demand history, oldest first.</summary>
    /// <param name="weeklySeries">Weekly quantities, oldest first. Gaps are explicit zeros.</param>
    /// <param name="method">Which strategy to use.</param>
    /// <param name="options">Tuning, or <c>null</c> for defaults.</param>
    ForecastResult Compute(IReadOnlyList<decimal> weeklySeries, ForecastMethod method, ForecastOptions? options = null);
}

/// <summary>One forecasting strategy behind the engine seam.</summary>
public interface IForecastStrategy
{
    /// <summary>The method this strategy implements.</summary>
    ForecastMethod Method { get; }

    /// <summary>Forecasts the next-period quantity from the series.</summary>
    decimal Forecast(IReadOnlyList<decimal> weeklySeries, ForecastOptions options);
}

/// <summary>Mean of the last N weekly periods.</summary>
public sealed class MovingAverageForecastStrategy : IForecastStrategy
{
    /// <inheritdoc />
    public ForecastMethod Method => ForecastMethod.MovingAverage;

    /// <inheritdoc />
    public decimal Forecast(IReadOnlyList<decimal> weeklySeries, ForecastOptions options)
    {
        ArgumentNullException.ThrowIfNull(weeklySeries);
        ArgumentNullException.ThrowIfNull(options);

        if (weeklySeries.Count == 0)
        {
            return 0m;
        }

        int window = Math.Min(Math.Max(options.WindowSize, 1), weeklySeries.Count);
        decimal sum = 0m;

        for (int i = weeklySeries.Count - window; i < weeklySeries.Count; i++)
        {
            sum += weeklySeries[i];
        }

        return sum / window;
    }
}

/// <summary>Single exponential smoothing: S<sub>t</sub> = α·Y<sub>t</sub> + (1−α)·S<sub>t−1</sub>, seeded at Y<sub>1</sub>.</summary>
public sealed class ExponentialSmoothingForecastStrategy : IForecastStrategy
{
    /// <inheritdoc />
    public ForecastMethod Method => ForecastMethod.ExponentialSmoothing;

    /// <inheritdoc />
    public decimal Forecast(IReadOnlyList<decimal> weeklySeries, ForecastOptions options)
    {
        ArgumentNullException.ThrowIfNull(weeklySeries);
        ArgumentNullException.ThrowIfNull(options);

        if (weeklySeries.Count == 0)
        {
            return 0m;
        }

        if (options.Alpha <= 0d || options.Alpha > 1d)
        {
            throw PlanningRuleException.BadInput("Alpha must be in (0, 1].");
        }

        decimal alpha = (decimal)options.Alpha;
        decimal smoothed = weeklySeries[0];

        for (int i = 1; i < weeklySeries.Count; i++)
        {
            smoothed = (alpha * weeklySeries[i]) + ((1m - alpha) * smoothed);
        }

        return smoothed;
    }
}

/// <summary>Seasonal naive: the forecast is the observation one full cycle back.</summary>
public sealed class SeasonalNaiveForecastStrategy : IForecastStrategy
{
    /// <inheritdoc />
    public ForecastMethod Method => ForecastMethod.SeasonalNaive;

    /// <inheritdoc />
    public decimal Forecast(IReadOnlyList<decimal> weeklySeries, ForecastOptions options)
    {
        ArgumentNullException.ThrowIfNull(weeklySeries);
        ArgumentNullException.ThrowIfNull(options);

        if (weeklySeries.Count == 0)
        {
            return 0m;
        }

        int back = weeklySeries.Count - options.SeasonLength;

        return back >= 0 ? weeklySeries[back] : weeklySeries[^1];
    }
}

/// <summary>Shared forecast mathematics: MAPE, bias, variation.</summary>
public static class ForecastMath
{
    /// <summary>
    /// Mean absolute percentage error of fitted values against actuals. Periods with an actual of
    /// zero are skipped (there is no percentage error of nothing); when every actual is zero the
    /// MAPE is zero rather than NaN.
    /// </summary>
    public static decimal Mape(IReadOnlyList<decimal> actuals, IReadOnlyList<decimal> fitted)
    {
        ArgumentNullException.ThrowIfNull(actuals);
        ArgumentNullException.ThrowIfNull(fitted);

        int count = Math.Min(actuals.Count, fitted.Count);
        decimal sum = 0m;
        int used = 0;

        for (int i = 0; i < count; i++)
        {
            if (actuals[i] == 0m)
            {
                continue;
            }

            sum += Math.Abs((actuals[i] - fitted[i]) / actuals[i]);
            used++;
        }

        return used == 0 ? 0m : sum / used;
    }

    /// <summary>
    /// Mean signed percentage error (bias). Positive means the fit ran high.
    /// <c>null</c> when no period has a non-zero actual.
    /// </summary>
    public static decimal? Bias(IReadOnlyList<decimal> actuals, IReadOnlyList<decimal> fitted)
    {
        ArgumentNullException.ThrowIfNull(actuals);
        ArgumentNullException.ThrowIfNull(fitted);

        int count = Math.Min(actuals.Count, fitted.Count);
        decimal sum = 0m;
        int used = 0;

        for (int i = 0; i < count; i++)
        {
            if (actuals[i] == 0m)
            {
                continue;
            }

            sum += (fitted[i] - actuals[i]) / actuals[i];
            used++;
        }

        return used == 0 ? null : sum / used;
    }

    /// <summary>Coefficient of variation (σ/μ) of a series. Zero when the mean is zero.</summary>
    public static decimal CoefficientOfVariation(IReadOnlyList<decimal> series)
    {
        ArgumentNullException.ThrowIfNull(series);

        if (series.Count == 0)
        {
            return 0m;
        }

        decimal mean = series.Average();

        if (mean == 0m)
        {
            return 0m;
        }

        double variance = series.Sum(value => Math.Pow((double)(value - mean), 2)) / series.Count;

        return (decimal)Math.Sqrt(variance) / mean;
    }

    /// <summary>Sample standard deviation of a series. Zero for fewer than two points.</summary>
    public static decimal StandardDeviation(IReadOnlyList<decimal> series)
    {
        ArgumentNullException.ThrowIfNull(series);

        if (series.Count < 2)
        {
            return 0m;
        }

        decimal mean = series.Average();
        double variance = series.Sum(value => Math.Pow((double)(value - mean), 2)) / (series.Count - 1);

        return (decimal)Math.Sqrt(variance);
    }

    /// <summary>
    /// Converts a service-level percentage to a z-score (Acklam's approximation of the normal
    /// inverse CDF, good to ~1e-9 — far past what a buffer quantity can express).
    /// </summary>
    /// <param name="serviceLevelPercent">The service level, 0–100 exclusive.</param>
    public static double ZScore(decimal serviceLevelPercent)
    {
        if (serviceLevelPercent <= 0m || serviceLevelPercent >= 100m)
        {
            throw PlanningRuleException.BadInput("Service level must be between 0 and 100 (exclusive).");
        }

        double p = (double)serviceLevelPercent / 100d;

        // Acklam's approximation.
        double[] a = [-3.969683028665376e+01, 2.209460984245205e+02, -2.759285104469687e+02, 1.383577518672690e+02, -3.066479806614716e+01, 2.506628277459239e+00];
        double[] b = [-5.447609879822406e+01, 1.615858368580409e+02, -1.556989798598866e+02, 6.680131188771972e+01, -1.328068155288572e+01];
        double[] c = [-7.784894002430293e-03, -3.223964580411365e-01, -2.400758277161838e+00, -2.549732539343734e+00, 4.374664141464968e+00, 2.938163982698783e+00];
        double[] d = [7.784695709041462e-03, 3.224671290700398e-01, 2.445134137142996e+00, 3.754408661907416e+00];

        double q;
        double result;

        if (p < 0.02425d)
        {
            q = Math.Sqrt(-2d * Math.Log(p));
            result = (((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) /
                     ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1d);
        }
        else if (p <= 0.97575d)
        {
            q = p - 0.5d;
            double r = q * q;
            result = (((((a[0] * r + a[1]) * r + a[2]) * r + a[3]) * r + a[4]) * r + a[5]) * q /
                     (((((b[0] * r + b[1]) * r + b[2]) * r + b[3]) * r + b[4]) * r + 1d);
        }
        else
        {
            q = Math.Sqrt(-2d * Math.Log(1d - p));
            result = -(((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) /
                      ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1d);
        }

        return result;
    }
}

/// <summary>Dispatches to the strategy for the requested method and back-tests it in-sample.</summary>
public sealed class ForecastEngine(IEnumerable<IForecastStrategy> strategies) : IForecastEngine
{
    private readonly IReadOnlyDictionary<ForecastMethod, IForecastStrategy> _strategies =
        strategies.ToDictionary(strategy => strategy.Method);

    /// <inheritdoc />
    public ForecastResult Compute(IReadOnlyList<decimal> weeklySeries, ForecastMethod method, ForecastOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(weeklySeries);

        ForecastOptions effective = options ?? new ForecastOptions();

        if (!_strategies.TryGetValue(method, out IForecastStrategy? strategy))
        {
            throw PlanningRuleException.BadInput($"Unknown forecast method '{method}'.");
        }

        if (weeklySeries.Count == 0)
        {
            return new ForecastResult(0m, 0m, null, true, method);
        }

        if (weeklySeries.Count == 1)
        {
            return new ForecastResult(weeklySeries[0], 0m, null, true, method);
        }

        if (method is ForecastMethod.SeasonalNaive && weeklySeries.Count < 2 * effective.SeasonLength)
        {
            // Insufficient seasons: fall back to the last observation, flagged low-confidence.
            return new ForecastResult(weeklySeries[^1], 0m, null, true, method);
        }

        decimal quantity = strategy.Forecast(weeklySeries, effective);

        // In-sample one-step-ahead back-test for MAPE/bias.
        var fitted = new List<decimal>(weeklySeries.Count - 1);

        for (int i = 1; i < weeklySeries.Count; i++)
        {
            fitted.Add(strategy.Forecast(weeklySeries.Take(i).ToList(), effective));
        }

        decimal mape = ForecastMath.Mape(weeklySeries.Skip(1).ToList(), fitted);
        decimal? bias = ForecastMath.Bias(weeklySeries.Skip(1).ToList(), fitted);

        return new ForecastResult(quantity < 0m ? 0m : quantity, mape, bias, false, method);
    }
}
