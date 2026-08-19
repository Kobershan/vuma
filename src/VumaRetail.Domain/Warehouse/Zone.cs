using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Warehouse;

/// <summary>
/// A named subdivision of a Stage 08 <see cref="Inventory.StockLocation"/> — a receiving dock, a
/// storage aisle, a pick face, a pack bench, a shipping dock.
/// </summary>
/// <remarks>
/// <see cref="LocationId"/> is a plain <see cref="Guid"/>, never a cross-schema foreign key
/// (<c>CONVENTIONS.md</c> §2) — the application layer validates it against Stage 08's
/// <c>IStockLocationRepository</c>.
/// </remarks>
[Replicated(ReplicationScope.Bidirectional, ConflictPolicy.CloudWins)]
public sealed class Zone : Entity
{
    private Zone(Guid tenantId, Guid? storeId, Guid locationId, string code, string name, ZoneType type)
        : base(tenantId, storeId)
    {
        LocationId = locationId;
        Code = code;
        Name = name;
        Type = type;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private Zone()
    {
    }

    /// <summary>The Stage 08 location this zone subdivides.</summary>
    public Guid LocationId { get; private set; }

    /// <summary>The zone's code, unique within its location, upper-cased.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>The zone's name.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>What kind of work this zone is used for.</summary>
    public ZoneType Type { get; private set; }

    /// <summary>True while the zone may hold active bins.</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>Creates a new zone.</summary>
    public static Zone Create(Guid tenantId, Guid? storeId, Guid locationId, string code, string name, ZoneType type)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A zone must belong to a tenant.", nameof(tenantId));
        }

        if (locationId == Guid.Empty)
        {
            throw new ArgumentException("A zone must belong to a location.", nameof(locationId));
        }

        return new Zone(tenantId, storeId, locationId, Require(code).ToUpperInvariant(), Require(name), type);
    }

    /// <summary>Retires the zone. History is retained; nothing is deleted.</summary>
    public void Deactivate() => IsActive = false;

    /// <summary>Brings a retired zone back into use.</summary>
    public void Activate() => IsActive = true;

    private static string Require(string value)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A required value was blank.")
            : value.Trim();
}
