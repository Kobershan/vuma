using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Planning;

/// <summary>
/// The replenishment parameters for one SKU at one location: how it is forecast, how much buffer
/// it carries, and when it reorders.
/// </summary>
/// <remarks>
/// Upserted by the planner, never recalculated on read. The forecast run and the replenishment
/// engine read these rows; they never derive them.
/// </remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.CloudWins)]
public sealed class ReplenishmentParameter : Entity
{
    private ReplenishmentParameter()
    {
    }

    private ReplenishmentParameter(
        Guid tenantId,
        Guid companyId,
        Guid locationId,
        Guid? itemId,
        Guid? itemVariantId,
        ForecastMethod forecastMethod,
        decimal serviceLevelPercent,
        int leadTimeDays,
        int reviewPeriodDays,
        string uom)
        : base(tenantId)
    {
        AssignCompany(companyId);
        LocationId = locationId;
        ItemId = itemId;
        ItemVariantId = itemVariantId;
        ForecastMethod = forecastMethod;
        ServiceLevelPercent = serviceLevelPercent;
        LeadTimeDays = leadTimeDays;
        ReviewPeriodDays = reviewPeriodDays;
        Uom = uom;
    }

    /// <summary>The location these parameters apply to.</summary>
    public Guid LocationId { get; private set; }

    /// <summary>The item, or <c>null</c> when <see cref="ItemVariantId"/> is set.</summary>
    public Guid? ItemId { get; private set; }

    /// <summary>The variant, or <c>null</c> when <see cref="ItemId"/> is set.</summary>
    public Guid? ItemVariantId { get; private set; }

    /// <summary>Which forecasting method the forecast run uses for this SKU.</summary>
    public ForecastMethod ForecastMethod { get; private set; }

    /// <summary>The service-level target, 0–100. Converted to a z-score at calculation time, never stored as one.</summary>
    public decimal ServiceLevelPercent { get; private set; }

    /// <summary>Supplier lead time in days.</summary>
    public int LeadTimeDays { get; private set; }

    /// <summary>Review period in days — how often the replenishment run looks at this SKU.</summary>
    public int ReviewPeriodDays { get; private set; }

    /// <summary>The unit of measure quantities are expressed in.</summary>
    public string Uom { get; private set; } = string.Empty;

    /// <summary>Updates the parameters. History is not kept — the calculations that used them snapshot their inputs.</summary>
    public void Update(
        ForecastMethod forecastMethod,
        decimal serviceLevelPercent,
        int leadTimeDays,
        int reviewPeriodDays,
        string uom)
    {
        Validate(serviceLevelPercent, leadTimeDays, reviewPeriodDays);
        ArgumentException.ThrowIfNullOrWhiteSpace(uom);

        ForecastMethod = forecastMethod;
        ServiceLevelPercent = serviceLevelPercent;
        LeadTimeDays = leadTimeDays;
        ReviewPeriodDays = reviewPeriodDays;
        Uom = uom.Trim();
    }

    /// <summary>Creates or validates parameters for one SKU at one location.</summary>
    /// <exception cref="PlanningRuleException">Neither or both of item and variant are set, or an input is out of range.</exception>
    public static ReplenishmentParameter Create(
        Guid tenantId,
        Guid companyId,
        Guid locationId,
        Guid? itemId,
        Guid? itemVariantId,
        ForecastMethod forecastMethod,
        decimal serviceLevelPercent,
        int leadTimeDays,
        int reviewPeriodDays,
        string uom)
    {
        if ((itemId is null) == (itemVariantId is null))
        {
            throw PlanningRuleException.ExactlyOneSku();
        }

        Validate(serviceLevelPercent, leadTimeDays, reviewPeriodDays);
        ArgumentException.ThrowIfNullOrWhiteSpace(uom);

        return new ReplenishmentParameter(
            tenantId,
            companyId,
            locationId,
            itemId,
            itemVariantId,
            forecastMethod,
            serviceLevelPercent,
            leadTimeDays,
            reviewPeriodDays,
            uom.Trim());
    }

    private static void Validate(decimal serviceLevelPercent, int leadTimeDays, int reviewPeriodDays)
    {
        if (serviceLevelPercent <= 0m || serviceLevelPercent >= 100m)
        {
            throw PlanningRuleException.BadInput("Service level must be between 0 and 100 (exclusive).");
        }

        if (leadTimeDays < 0)
        {
            throw PlanningRuleException.BadInput("Lead time cannot be negative.");
        }

        if (reviewPeriodDays < 1)
        {
            throw PlanningRuleException.BadInput("Review period must be at least one day.");
        }
    }
}
