using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Registry;

/// <summary>
/// Maps a company to a premises — the occupancy relationship.
/// </summary>
public sealed class PremisesOccupancy
{
    private PremisesOccupancy() { }

    private PremisesOccupancy(Guid id, Guid premisesId, Guid companyId, Guid storeId)
    {
        Id = id;
        PremisesId = premisesId;
        CompanyId = companyId;
        StoreId = storeId;
        OccupiesFrom = DateTimeOffset.UtcNow;
    }

    /// <summary>The occupancy record identifier.</summary>
    public Guid Id { get; private set; }

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
    public static PremisesOccupancy Create(Guid premisesId, Guid companyId, Guid storeId)
    {
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
        return new PremisesOccupancy(UuidV7.NewGuid(), premisesId, companyId, storeId);
    }

    /// <summary>Ends occupancy at the given time.</summary>
    public void EndOccupancy(DateTimeOffset occupiedTo)
    {
        OccupiesTo = occupiedTo;
    }
}
