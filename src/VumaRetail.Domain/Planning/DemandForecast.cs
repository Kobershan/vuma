using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Planning;

/// <summary>
/// Forecast snapshot: a versioned, immutable forecast for one SKU at one location for one period.
/// </summary>
/// <remarks>
/// <para>
/// Forecasts are versioned. The first run for an item/location/period creates version <c>v1</c>.
/// Re-running a closed period creates version <c>v(n+1)</c> rather than updating in place — both
/// versions remain queryable. This is the "snapshots, not re-derivations" rule (ADR-112, ADR-113):
/// a forecast reprinted later shows what was actually predicted.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.CloudWins)]
public sealed class DemandForecast : Entity
{
    private DemandForecast()
    {
    }

    private DemandForecast(
        Guid tenantId,
        Guid companyId,
        Guid? itemId,
        Guid? itemVariantId,
        Guid locationId,
        DateOnly forecastPeriod,
        ForecastMethod forecastMethod,
        decimal quantity,
        decimal mape,
        decimal? bias,
        string version,
        DateTimeOffset generatedAt,
        Guid? generatedBy = null)
        : base(tenantId)
    {
        AssignCompany(companyId);
        ItemId = itemId;
        ItemVariantId = itemVariantId;
        LocationId = locationId;
        ForecastPeriod = forecastPeriod;
        ForecastMethod = forecastMethod;
        Quantity = quantity;
        Mape = mape;
        Bias = bias;
        Version = version;
        GeneratedAt = generatedAt;
        GeneratedBy = generatedBy;
    }

    /// <summary>The item this forecast covers, or <c>null</c> when it is a variant instead.</summary>
    public Guid? ItemId { get; private set; }

    /// <summary>The variant this forecast covers, or <c>null</c> when <see cref="ItemId"/> is set.</summary>
    public Guid? ItemVariantId { get; private set; }

    /// <summary>The location this forecast covers.</summary>
    public Guid LocationId { get; private set; }

    /// <summary>The forecast period (the Monday of the week being forecast).</summary>
    public DateOnly ForecastPeriod { get; private set; }

    /// <summary>The forecasting method used.</summary>
    public ForecastMethod ForecastMethod { get; private set; }

    /// <summary>The forecasted quantity.</summary>
    public decimal Quantity { get; private set; }

    /// <summary>The Mean Absolute Percentage Error from back-testing, or 0 when not computable.</summary>
    public decimal Mape { get; private set; }

    /// <summary>The bias (mean signed percentage error), or <c>null</c> when not computable.</summary>
    public decimal? Bias { get; private set; }

    /// <summary>The forecast version (<c>v1</c>, <c>v2</c>, …). Closed-period forecasts are immutable.</summary>
    public string Version { get; private set; } = "v1";

    /// <summary>When the forecast was generated, UTC.</summary>
    public DateTimeOffset GeneratedAt { get; private set; }

    /// <summary>Who or what generated the forecast, or <c>null</c> for the scheduled run.</summary>
    public Guid? GeneratedBy { get; private set; }

    /// <summary>
    /// Creates the first forecast version for an item/location/period.
    /// </summary>
    /// <exception cref="PlanningRuleException">Neither or both of item and variant are set.</exception>
    public static DemandForecast CreateVersion1(
        Guid tenantId,
        Guid companyId,
        Guid? itemId,
        Guid? itemVariantId,
        Guid locationId,
        DateOnly forecastPeriod,
        ForecastMethod forecastMethod,
        decimal quantity,
        decimal mape,
        decimal? bias,
        DateTimeOffset generatedAt,
        Guid? generatedBy = null)
    {
        if ((itemId is null) == (itemVariantId is null))
        {
            throw PlanningRuleException.ExactlyOneSku();
        }

        return new DemandForecast(
            tenantId,
            companyId,
            itemId,
            itemVariantId,
            locationId,
            forecastPeriod,
            forecastMethod,
            quantity,
            mape,
            bias,
            "v1",
            generatedAt,
            generatedBy);
    }

    /// <summary>
    /// Creates the next version of an existing forecast, copying every field except the version stamp
    /// and the generation audit. The existing row is never mutated.
    /// </summary>
    /// <param name="existing">The forecast being superseded.</param>
    /// <param name="quantity">The re-computed quantity.</param>
    /// <param name="mape">The re-computed MAPE.</param>
    /// <param name="bias">The re-computed bias.</param>
    /// <param name="generatedAt">When the re-run happened, UTC.</param>
    /// <param name="generatedBy">Who or what re-ran it.</param>
    /// <exception cref="PlanningRuleException">The existing version stamp is not in <c>vN</c> form.</exception>
    public static DemandForecast CreateNextVersion(
        DemandForecast existing,
        decimal quantity,
        decimal mape,
        decimal? bias,
        DateTimeOffset generatedAt,
        Guid? generatedBy = null)
    {
        ArgumentNullException.ThrowIfNull(existing);

        return new DemandForecast(
            existing.TenantId,
            existing.CompanyId!.Value,
            existing.ItemId,
            existing.ItemVariantId,
            existing.LocationId,
            existing.ForecastPeriod,
            existing.ForecastMethod,
            quantity,
            mape,
            bias,
            NextVersion(existing.Version),
            generatedAt,
            generatedBy);
    }

    private static string NextVersion(string previous)
    {
        if (previous is not ['v', .. string rest] || !int.TryParse(rest, out int number) || number < 1)
        {
            throw PlanningRuleException.BadForecastVersion(previous);
        }

        return $"v{number + 1}";
    }
}
