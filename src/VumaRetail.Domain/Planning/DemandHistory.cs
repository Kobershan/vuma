using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Planning;

/// <summary>
/// Read model: demand history aggregated from sale issues into fixed periods.
/// </summary>
/// <remarks>
/// <para>
/// A read model, not an aggregate root. It summarises <see cref="Inventory.StockLedgerEntry"/> rows
/// whose movement is <see cref="Inventory.StockMovementType.SaleIssue"/>, grouped by item/variant ×
/// location × period. Rebuilt from the authoritative stock ledger on a nightly schedule — it never
/// becomes a second source of truth (ADR-012).
/// </para>
/// <para>
/// Period grain is weekly, Monday to Sunday. Each row is one SKU at one location for one week.
/// Periods with no sales inside an otherwise active series are stored with a zero quantity so the
/// forecasting mathematics never shifts time.
/// </para>
/// <para>
/// The rollup is idempotent: the natural key (tenant, company, location, SKU, period start) carries
/// a unique index, and rebuilding upserts rather than inserts.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.AppendOnly)]
public sealed class DemandHistory : Entity
{
    private DemandHistory()
    {
    }

    private DemandHistory(
        Guid tenantId,
        Guid companyId,
        Guid locationId,
        Guid? itemId,
        Guid? itemVariantId,
        DateOnly periodStart,
        DateOnly periodEnd,
        decimal totalQuantity,
        string uom,
        DateTimeOffset generatedAt)
        : base(tenantId)
    {
        AssignCompany(companyId);
        LocationId = locationId;
        ItemId = itemId;
        ItemVariantId = itemVariantId;
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
        TotalQuantity = totalQuantity;
        Uom = uom;
        GeneratedAt = generatedAt;
    }

    /// <summary>The location this demand history row covers.</summary>
    public Guid LocationId { get; private set; }

    /// <summary>The item this row covers, or <c>null</c> when it is a variant instead.</summary>
    public Guid? ItemId { get; private set; }

    /// <summary>The variant this row covers, or <c>null</c> when <see cref="ItemId"/> is set.</summary>
    public Guid? ItemVariantId { get; private set; }

    /// <summary>The inclusive start of the aggregation period (a Monday).</summary>
    public DateOnly PeriodStart { get; private set; }

    /// <summary>The inclusive end of the aggregation period (a Sunday).</summary>
    public DateOnly PeriodEnd { get; private set; }

    /// <summary>The total quantity sold in this period. Zero for gap periods.</summary>
    public decimal TotalQuantity { get; private set; }

    /// <summary>The unit of measure.</summary>
    public string Uom { get; private set; } = string.Empty;

    /// <summary>When this row was generated from the ledger.</summary>
    public DateTimeOffset GeneratedAt { get; private set; }

    /// <summary>Refreshes the aggregated total on a rebuild. The row's identity never changes.</summary>
    /// <param name="totalQuantity">The re-aggregated total.</param>
    /// <param name="generatedAt">When the rebuild ran, UTC.</param>
    public void Refresh(decimal totalQuantity, DateTimeOffset generatedAt)
    {
        TotalQuantity = totalQuantity;
        GeneratedAt = generatedAt;
    }

    /// <summary>
    /// Creates a demand history row.
    /// </summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="companyId">The owning company.</param>
    /// <param name="locationId">The location the demand is aggregated for.</param>
    /// <param name="itemId">The item, or <c>null</c> when <paramref name="itemVariantId"/> is set.</param>
    /// <param name="itemVariantId">The variant, or <c>null</c> when <paramref name="itemId"/> is set.</param>
    /// <param name="periodStart">Inclusive start of the aggregation period.</param>
    /// <param name="periodEnd">Inclusive end of the aggregation period.</param>
    /// <param name="totalQuantity">The aggregated quantity (zero for gap periods).</param>
    /// <param name="uom">The unit of measure.</param>
    /// <param name="generatedAt">When this row was generated, UTC.</param>
    /// <exception cref="PlanningRuleException">Neither or both of item and variant are set.</exception>
    public static DemandHistory Create(
        Guid tenantId,
        Guid companyId,
        Guid locationId,
        Guid? itemId,
        Guid? itemVariantId,
        DateOnly periodStart,
        DateOnly periodEnd,
        decimal totalQuantity,
        string uom,
        DateTimeOffset generatedAt)
    {
        if ((itemId is null) == (itemVariantId is null))
        {
            throw PlanningRuleException.ExactlyOneSku();
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(uom);

        return new DemandHistory(
            tenantId,
            companyId,
            locationId,
            itemId,
            itemVariantId,
            periodStart,
            periodEnd,
            totalQuantity,
            uom.Trim(),
            generatedAt);
    }
}
