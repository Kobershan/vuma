using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Planning;

/// <summary>
/// One ABC/XYZ classification snapshot for a SKU at a location.
/// </summary>
/// <remarks>
/// <para>
/// Classification is a scheduled snapshot: reading it never recalculates it, and existing
/// snapshots never change when new sales arrive. A new run writes new rows.
/// </para>
/// <para>
/// ABC ranks by share of demand quantity over the classification window — a quantity proxy for
/// consumption value, used because cost joins live in Stage 08 valuation and this snapshot must
/// stay rebuildable from demand history alone. XYZ ranks by variability (coefficient of variation
/// of weekly demand): X steady, Y fluctuating, Z erratic.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.CloudWins)]
public sealed class AbcXyzClassification : Entity
{
    private AbcXyzClassification()
    {
    }

    private AbcXyzClassification(
        Guid tenantId,
        Guid companyId,
        Guid locationId,
        Guid? itemId,
        Guid? itemVariantId,
        AbcClass abc,
        XyzClass xyz,
        decimal demandShare,
        decimal coefficientOfVariation,
        DateOnly periodStart,
        DateOnly periodEnd,
        DateTimeOffset generatedAt)
        : base(tenantId)
    {
        AssignCompany(companyId);
        LocationId = locationId;
        ItemId = itemId;
        ItemVariantId = itemVariantId;
        Abc = abc;
        Xyz = xyz;
        DemandShare = demandShare;
        CoefficientOfVariation = coefficientOfVariation;
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
        GeneratedAt = generatedAt;
    }

    /// <summary>The location classified.</summary>
    public Guid LocationId { get; private set; }

    /// <summary>The item, or <c>null</c> for a variant.</summary>
    public Guid? ItemId { get; private set; }

    /// <summary>The variant, or <c>null</c> for an item.</summary>
    public Guid? ItemVariantId { get; private set; }

    /// <summary>The ABC class.</summary>
    public AbcClass Abc { get; private set; }

    /// <summary>The XYZ class.</summary>
    public XyzClass Xyz { get; private set; }

    /// <summary>This SKU's share of total demand quantity in the window, 0–1.</summary>
    public decimal DemandShare { get; private set; }

    /// <summary>Coefficient of variation of weekly demand in the window.</summary>
    public decimal CoefficientOfVariation { get; private set; }

    /// <summary>First day of the classification window.</summary>
    public DateOnly PeriodStart { get; private set; }

    /// <summary>Last day of the classification window.</summary>
    public DateOnly PeriodEnd { get; private set; }

    /// <summary>When the snapshot was taken, UTC.</summary>
    public DateTimeOffset GeneratedAt { get; private set; }

    /// <summary>Creates a classification snapshot row.</summary>
    /// <exception cref="PlanningRuleException">Neither or both of item and variant are set.</exception>
    public static AbcXyzClassification Create(
        Guid tenantId,
        Guid companyId,
        Guid locationId,
        Guid? itemId,
        Guid? itemVariantId,
        AbcClass abc,
        XyzClass xyz,
        decimal demandShare,
        decimal coefficientOfVariation,
        DateOnly periodStart,
        DateOnly periodEnd,
        DateTimeOffset generatedAt)
    {
        if ((itemId is null) == (itemVariantId is null))
        {
            throw PlanningRuleException.ExactlyOneSku();
        }

        return new AbcXyzClassification(
            tenantId,
            companyId,
            locationId,
            itemId,
            itemVariantId,
            abc,
            xyz,
            demandShare,
            coefficientOfVariation,
            periodStart,
            periodEnd,
            generatedAt);
    }
}
