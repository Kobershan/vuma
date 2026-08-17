using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Warehouse;

/// <summary>
/// A single storage position within a <see cref="Zone"/> — the smallest unit stock is tracked at once
/// Stage 13 exists. The bin-level equivalent of a Stage 08 <see cref="Inventory.StockLocation"/>.
/// </summary>
/// <remarks>
/// <see cref="LocationId"/> is carried directly (denormalised from <see cref="ZoneId"/>'s own location)
/// so a bin-scoped query never needs to join through its zone to find its location — the same shape
/// <c>StockLedgerEntry.LocationId</c> uses.
/// </remarks>
[Replicated(ReplicationScope.Bidirectional, ConflictPolicy.CloudWins)]
public sealed class Bin : Entity
{
    private Bin(
        Guid tenantId,
        Guid? storeId,
        Guid locationId,
        Guid zoneId,
        string code,
        string name,
        BinType type,
        Quantity? capacity)
        : base(tenantId, storeId)
    {
        LocationId = locationId;
        ZoneId = zoneId;
        Code = code;
        Name = name;
        Type = type;
        CapacityValue = capacity?.Value;
        CapacityUnitOfMeasure = capacity?.UnitOfMeasure;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private Bin()
    {
    }

    /// <summary>The Stage 08 location this bin belongs to.</summary>
    public Guid LocationId { get; private set; }

    /// <summary>The zone this bin belongs to.</summary>
    public Guid ZoneId { get; private set; }

    /// <summary>The bin's code, unique within its location, upper-cased.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>The bin's name or description.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>What kind of storage unit this bin is.</summary>
    public BinType Type { get; private set; }

    /// <summary>
    /// The capacity's numeric value, or <c>null</c> if none is recorded. Mapped directly as a plain
    /// nullable column; prefer <see cref="Capacity"/> — this pairs with
    /// <see cref="CapacityUnitOfMeasure"/> only because EF Core 9 complex properties do not support a
    /// two-hop member expression on a nullable value-type complex property (ADR-067, the same reason
    /// <c>Domain.Finance.JournalLine.DebitAmount</c> is a plain column).
    /// </summary>
    public decimal? CapacityValue { get; private set; }

    /// <summary>The capacity's unit of measure, paired with <see cref="CapacityValue"/>.</summary>
    public string? CapacityUnitOfMeasure { get; private set; }

    /// <summary>
    /// An informational capacity, or <c>null</c> if none is recorded. Not enforced (ADR-091) — see the
    /// stage document's business rule 9.
    /// </summary>
    public Quantity? Capacity => CapacityValue is { } value ? new Quantity(value, CapacityUnitOfMeasure!) : null;

    /// <summary>True while the bin may receive or issue stock.</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>Creates a new bin.</summary>
    public static Bin Create(
        Guid tenantId,
        Guid? storeId,
        Guid locationId,
        Guid zoneId,
        string code,
        string name,
        BinType type,
        Quantity? capacity = null)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A bin must belong to a tenant.", nameof(tenantId));
        }

        if (locationId == Guid.Empty)
        {
            throw new ArgumentException("A bin must belong to a location.", nameof(locationId));
        }

        if (zoneId == Guid.Empty)
        {
            throw new ArgumentException("A bin must belong to a zone.", nameof(zoneId));
        }

        if (capacity is { IsNegative: true } or { IsZero: true })
        {
            throw WarehouseRuleException.QuantityMustBePositive();
        }

        return new Bin(tenantId, storeId, locationId, zoneId, Require(code).ToUpperInvariant(), Require(name), type, capacity);
    }

    /// <summary>Retires the bin. History is retained; nothing is deleted.</summary>
    public void Deactivate() => IsActive = false;

    /// <summary>Brings a retired bin back into use.</summary>
    public void Activate() => IsActive = true;

    private static string Require(string value)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A required value was blank.")
            : value.Trim();
}
