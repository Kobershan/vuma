using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Registry;

/// <summary>
/// Maps a company to a premises — the occupancy relationship.
/// </summary>
public sealed class PremisesOccupancy
{
    private PremisesOccupancy() { }

    private PremisesOccupancy(Guid id, Guid tenantId, Guid premisesId, Guid companyId, Guid storeId, DateTimeOffset occupiesFrom)
    {
        Id = id;
        TenantId = tenantId;
        PremisesId = premisesId;
        CompanyId = companyId;
        StoreId = storeId;
        OccupiesFrom = occupiesFrom;
    }

    /// <summary>The occupancy record identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>The tenant that owns this occupancy.</summary>
    public Guid TenantId { get; private set; }

    /// <summary>The premises being occupied.</summary>
    public Guid PremisesId { get; private set; }

    /// <summary>The company occupying the premises.</summary>
    public Guid CompanyId { get; private set; }

    /// <summary>The store row inside that company's database.</summary>
    public Guid StoreId { get; private set; }

    /// <summary>When occupancy began.</summary>
    public DateTimeOffset OccupiesFrom { get; private set; }

    /// <summary>When occupancy ends, or null if still occupying.</summary>
    public DateTimeOffset? OccupiesTo { get; private set; }

    /// <summary>Creates a new occupancy for a premises.</summary>
    public static PremisesOccupancy Create(Guid tenantId, Guid premisesId, Guid companyId, Guid storeId, DateTimeOffset occupiesFrom)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A tenant is required.", nameof(tenantId));
        }
        if (premisesId == Guid.Empty)
        {
            throw new ArgumentException("A premises is required.", nameof(premisesId));
        }
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("A company is required.", nameof(companyId));
        }
        if (storeId == Guid.Empty)
        {
            throw new ArgumentException("A store is required.", nameof(storeId));
        }
        return new PremisesOccupancy(UuidV7.NewGuid(), tenantId, premisesId, companyId, storeId, occupiesFrom);
    }

    /// <summary>Ends occupancy at the given time.</summary>
    public void EndOccupancy(DateTimeOffset occupiedTo)
    {
        OccupiesTo = occupiedTo;
    }
}
