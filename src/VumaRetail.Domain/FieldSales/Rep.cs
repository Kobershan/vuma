using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.FieldSales;

/// <summary>
/// A sales rep: a user with a territory, the companies they may sell for, and a visibility
/// profile (Stage 14b, FIELD_SALES.md §1).
/// </summary>
/// <remarks>
/// A rep proposes; management commits. The territory decides what a rep may see and quote and is
/// enforced server-side — a rep who guesses a customer id gets a refusal, not a document.
/// </remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class Rep : Entity
{
    private Rep(Guid tenantId, Guid? storeId)
        : base(tenantId, storeId)
    {
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private Rep()
    {
    }

    /// <summary>The registry user behind the rep. Bare id, never a foreign key.</summary>
    public Guid RegistryUserId { get; private set; }

    /// <summary>Display name, snapshotted from the user directory at creation.</summary>
    public string DisplayName { get; private set; } = string.Empty;

    /// <summary>Companies this rep may sell for.</summary>
    public List<Guid> CompanyIds { get; private set; } = [];

    /// <summary>Assigned customers. Empty with no geography means nowhere.</summary>
    public List<Guid> CustomerIds { get; private set; } = [];

    /// <summary>Optional geography territory (province/city/suburb — the 13b hierarchy).</summary>
    public string? TerritoryProvince { get; private set; }

    /// <summary>Optional geography territory.</summary>
    public string? TerritoryCity { get; private set; }

    /// <summary>Optional geography territory.</summary>
    public string? TerritorySuburb { get; private set; }

    /// <summary>Whether availability and documents show cost and margin to this rep.</summary>
    public bool SeeCost { get; private set; }

    /// <summary>False once deactivated. History keeps pointing at the row.</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>Registers a rep for a user.</summary>
    public static Rep Register(
        Guid tenantId,
        Guid? storeId,
        Guid registryUserId,
        string displayName,
        IEnumerable<Guid> companyIds,
        bool seeCost = false)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A rep must belong to a tenant.", nameof(tenantId));
        }

        if (registryUserId == Guid.Empty)
        {
            throw new ArgumentException("A rep must name its registry user.", nameof(registryUserId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        var rep = new Rep(tenantId, storeId)
        {
            RegistryUserId = registryUserId,
            DisplayName = displayName.Trim(),
            SeeCost = seeCost,
        };

        foreach (Guid companyId in companyIds ?? [])
        {
            rep.GrantCompany(companyId);
        }


        return rep;
    }

    /// <summary>Allows selling for one more company.</summary>
    public void GrantCompany(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("A company is required.", nameof(companyId));
        }

        if (!CompanyIds.Contains(companyId))
        {
            CompanyIds.Add(companyId);
        }
    }

    /// <summary>Assigns customers and optional geography.</summary>
    public void AssignTerritory(
        IEnumerable<Guid> customerIds,
        string? province = null,
        string? city = null,
        string? suburb = null)
    {
        CustomerIds = [];
        foreach (Guid customerId in customerIds ?? [])
        {
            if (customerId != Guid.Empty && !CustomerIds.Contains(customerId))
            {
                CustomerIds.Add(customerId);
            }
        }

        TerritoryProvince = string.IsNullOrWhiteSpace(province) ? null : province.Trim();
        TerritoryCity = string.IsNullOrWhiteSpace(city) ? null : city.Trim();
        TerritorySuburb = string.IsNullOrWhiteSpace(suburb) ? null : suburb.Trim();
    }

    /// <summary>Changes the visibility profile.</summary>
    public void SetVisibility(bool seeCost) => SeeCost = seeCost;

    /// <summary>Deactivates the rep. History keeps pointing at the row.</summary>
    public void Deactivate() => IsActive = false;

    /// <summary>Whether this rep may sell for the company.</summary>
    public bool MaySellFor(Guid companyId) => IsActive && CompanyIds.Contains(companyId);

    /// <summary>Whether this rep may see and quote the customer.</summary>
    public bool MayQuote(Guid customerId) => IsActive && CustomerIds.Contains(customerId);
}
