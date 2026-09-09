using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Planning;

/// <summary>
/// One safety-stock calculation: the inputs snapshotted, the result, and whether it is low-confidence.
/// </summary>
/// <remarks>
/// Audit, not state: the replenishment engine reads the latest calculation per SKU/location, and
/// every calculation carries the inputs it used so a later reader can see exactly what was assumed.
/// </remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.CloudWins)]
public sealed class SafetyStockCalculation : Entity
{
    private SafetyStockCalculation()
    {
    }

    private SafetyStockCalculation(
        Guid tenantId,
        Guid companyId,
        Guid locationId,
        Guid? itemId,
        Guid? itemVariantId,
        decimal leadTimeDemandMean,
        decimal demandVariance,
        decimal serviceLevelPercent,
        int historyWeeks,
        int leadTimeDays,
        decimal safetyStock,
        decimal reorderPoint,
        bool lowConfidence,
        string method,
        DateTimeOffset calculatedAt)
        : base(tenantId)
    {
        AssignCompany(companyId);
        LocationId = locationId;
        ItemId = itemId;
        ItemVariantId = itemVariantId;
        LeadTimeDemandMean = leadTimeDemandMean;
        DemandVariance = demandVariance;
        ServiceLevelPercent = serviceLevelPercent;
        HistoryWeeks = historyWeeks;
        LeadTimeDays = leadTimeDays;
        SafetyStock = safetyStock;
        ReorderPoint = reorderPoint;
        LowConfidence = lowConfidence;
        Method = method;
        CalculatedAt = calculatedAt;
    }

    /// <summary>The location calculated for.</summary>
    public Guid LocationId { get; private set; }

    /// <summary>The item, or <c>null</c> for a variant.</summary>
    public Guid? ItemId { get; private set; }

    /// <summary>The variant, or <c>null</c> for an item.</summary>
    public Guid? ItemVariantId { get; private set; }

    /// <summary>Mean demand over the lead time, in units.</summary>
    public decimal LeadTimeDemandMean { get; private set; }

    /// <summary>Variance of weekly demand used by the calculation.</summary>
    public decimal DemandVariance { get; private set; }

    /// <summary>The service-level target the z-score was converted from.</summary>
    public decimal ServiceLevelPercent { get; private set; }

    /// <summary>How many weeks of history fed the calculation.</summary>
    public int HistoryWeeks { get; private set; }

    /// <summary>The lead time in days the calculation covered.</summary>
    public int LeadTimeDays { get; private set; }

    /// <summary>The resulting safety stock, in units.</summary>
    public decimal SafetyStock { get; private set; }

    /// <summary>The resulting reorder point, in units.</summary>
    public decimal ReorderPoint { get; private set; }

    /// <summary>True when the minimum-history fallback was used.</summary>
    public bool LowConfidence { get; private set; }

    /// <summary><c>variance</c> or <c>fallback</c>.</summary>
    public string Method { get; private set; } = string.Empty;

    /// <summary>When it was calculated, UTC.</summary>
    public DateTimeOffset CalculatedAt { get; private set; }

    /// <summary>Records a safety-stock calculation with its inputs snapshotted.</summary>
    /// <exception cref="PlanningRuleException">Neither or both of item and variant are set.</exception>
    public static SafetyStockCalculation Create(
        Guid tenantId,
        Guid companyId,
        Guid locationId,
        Guid? itemId,
        Guid? itemVariantId,
        decimal leadTimeDemandMean,
        decimal demandVariance,
        decimal serviceLevelPercent,
        int historyWeeks,
        int leadTimeDays,
        decimal safetyStock,
        decimal reorderPoint,
        bool lowConfidence,
        string method,
        DateTimeOffset calculatedAt)
    {
        if ((itemId is null) == (itemVariantId is null))
        {
            throw PlanningRuleException.ExactlyOneSku();
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(method);

        return new SafetyStockCalculation(
            tenantId,
            companyId,
            locationId,
            itemId,
            itemVariantId,
            leadTimeDemandMean,
            demandVariance,
            serviceLevelPercent,
            historyWeeks,
            leadTimeDays,
            safetyStock,
            reorderPoint,
            lowConfidence,
            method.Trim(),
            calculatedAt);
    }
}
