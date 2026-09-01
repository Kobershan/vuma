#pragma warning disable CS1591
using VumaRetail.Domain.Registry;

namespace VumaRetail.Application.Abstractions.Registry;

/// <summary>Read models that span companies, carrying AsAt per contributor.</summary>
public interface IGroupReadStore
{
    /// <summary>Group-wide availability with per-company contributions and staleness disclosure.</summary>
    Task<GroupAvailability> GetAvailabilityAsync(Guid tenantId, IEnumerable<Guid> companyIds, CancellationToken cancellationToken = default);
    
    /// <summary>Rebuild path: regenerates from company outboxes, returns the rebuilt projection.</summary>
    Task<GroupAvailability> RebuildAsync(Guid tenantId, IEnumerable<Guid> companyIds, CancellationToken cancellationToken = default);
}

/// <summary>Group availability read model. Never used as the basis for a commit.</summary>
public sealed record GroupAvailability(IReadOnlyList<CompanyAvailability> Companies, DateTimeOffset AsAt)
{
    /// <summary>Total available across all companies.</summary>
    public decimal TotalAvailable => Companies.Sum(c => c.Available);
    /// <summary>Whether any contributor is stale.</summary>
    public bool HasStaleContributors => Companies.Any(c => c.IsStale);
    /// <summary>Codes of stale contributors.</summary>
    public IReadOnlyList<string> StaleContributorCodes => Companies.Where(c => c.IsStale).Select(c => c.CompanyCode).ToList();
}

/// <summary>Availability for one company in a group.</summary>
public sealed record CompanyAvailability(Guid CompanyId, string CompanyCode, decimal Available, decimal Reserved, decimal OnHand, DateTimeOffset AsAt, bool IsStale);
