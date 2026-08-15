using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Inventory;

/// <summary>
/// A physical place stock is held — a warehouse, a store's back room, a sales floor.
/// </summary>
/// <remarks>
/// <para>
/// Location-level, not bin-level: R8 asks for multi-warehouse support from the data model up, and
/// <c>CLAUDE.md</c> §6 puts bin/zone granularity at Stage 13 (Warehouse Management). This entity is the
/// unit Stage 08's ledger posts against; a location is subdivided into bins only once Stage 13 exists.
/// </para>
/// <para>
/// <see cref="Entity.StoreId"/> is optional: most locations belong to one store (its stock room, its
/// sales floor), but a central distribution warehouse serving several stores is a legitimate tenant-wide
/// location with no single owning store.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.Bidirectional, ConflictPolicy.CloudWins)]
public sealed class StockLocation : Entity
{
    private StockLocation(Guid tenantId, Guid? storeId, string code, string name, StockLocationType type)
        : base(tenantId, storeId)
    {
        Code = code;
        Name = name;
        Type = type;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private StockLocation()
    {
    }

    /// <summary>The location's unique code within the tenant, upper-cased, like <c>Store.Code</c>.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>The location's name.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>What kind of location this is.</summary>
    public StockLocationType Type { get; private set; }

    /// <summary>True while the location may receive or issue stock. Deactivate, not delete (§7 rule 8).</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>Creates a new stock location.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The owning store, or <c>null</c> for a tenant-wide location such as a central warehouse.</param>
    /// <param name="code">The location code, upper-cased.</param>
    /// <param name="name">The location's name.</param>
    /// <param name="type">What kind of location this is.</param>
    public static StockLocation Create(Guid tenantId, Guid? storeId, string code, string name, StockLocationType type)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A stock location must belong to a tenant.", nameof(tenantId));
        }

        return new StockLocation(tenantId, storeId, Require(code, nameof(code)).ToUpperInvariant(), Require(name, nameof(name)), type);
    }

    /// <summary>Retires the location from receiving or issuing stock. History is retained; nothing is deleted.</summary>
    public void Deactivate() => IsActive = false;

    /// <summary>Brings a retired location back into use.</summary>
    public void Activate() => IsActive = true;

    private static string Require(string value, string parameterName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{parameterName} is required.", parameterName)
            : value.Trim();
}

/// <summary>What kind of place a <see cref="StockLocation"/> is.</summary>
public enum StockLocationType
{
    /// <summary>A dedicated storage or distribution warehouse.</summary>
    Warehouse = 0,

    /// <summary>A store's own sales floor or back room.</summary>
    SalesFloor = 1,

    /// <summary>Anything else — a consignment location, a repair bench, an in-transit holding area.</summary>
    Other = 2,
}
