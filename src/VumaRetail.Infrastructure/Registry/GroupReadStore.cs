using VumaRetail.Domain.Registry;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VumaRetail.Application.Inventory;

namespace VumaRetail.Infrastructure.Registry;

/// <summary>Reads the durable registry projection for the legacy group-read port.</summary>
public sealed class GroupReadStore : IGroupReadStore
{
    private readonly VumaRegistryDbContext _registry;
    private readonly IClock _clock;
    private readonly GroupAvailabilityOptions _options;

    public GroupReadStore(VumaRegistryDbContext registry, IClock clock, IOptions<GroupAvailabilityOptions> options)
    {
        _registry = registry;
        _clock = clock;
        _options = options.Value;
    }

    public async Task<GroupAvailability> GetAvailabilityAsync(Guid tenantId, IEnumerable<Guid> companyIds, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("A tenant is required.", nameof(tenantId));
        Guid[] ids = companyIds.Distinct().Where(id => id != Guid.Empty).ToArray();
        DateTimeOffset now = _clock.UtcNow;
        if (ids.Length == 0) return new GroupAvailability([], now);

        Dictionary<Guid, string> companies = await _registry.Companies.AsNoTracking()
            .Where(company => company.TenantId == tenantId && ids.Contains(company.Id))
            .ToDictionaryAsync(company => company.Id, company => company.Code, cancellationToken);
        List<GroupAvailabilityRow> rows = await _registry.GroupAvailabilityRows.AsNoTracking()
            .Where(row => row.TenantId == tenantId && ids.Contains(row.CompanyId))
            .ToListAsync(cancellationToken);

        List<CompanyAvailability> result = [];
        foreach (Guid companyId in ids.OrderBy(id => companies.GetValueOrDefault(id, id.ToString("N"))))
        {
            List<GroupAvailabilityRow> contributed = rows.Where(row => row.CompanyId == companyId).ToList();
            DateTimeOffset asAt = contributed.Count == 0 ? DateTimeOffset.MinValue : contributed.Min(row => row.AsAt);
            result.Add(new CompanyAvailability(
                companyId,
                companies.GetValueOrDefault(companyId, companyId.ToString("N")),
                contributed.Sum(row => row.Available),
                contributed.Sum(row => row.Reserved),
                contributed.Sum(row => row.OnHand),
                asAt,
                IsStale: contributed.Count == 0 || now - asAt > _options.StaleAfter));
        }

        return new GroupAvailability(result, now);
    }

    public async Task<GroupAvailability> RebuildAsync(Guid tenantId, IEnumerable<Guid> companyIds, CancellationToken cancellationToken = default)
    {
        // Rebuilding is performed by GroupAvailabilityRelay, which needs company database
        // contexts and is tested independently. This query returns the same durable projection
        // it rebuilds, without substituting a fan-out of fake zeroes.
        return await GetAvailabilityAsync(tenantId, companyIds, cancellationToken);
    }
}
