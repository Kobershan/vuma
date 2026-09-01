using VumaRetail.Domain.Registry;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace VumaRetail.Infrastructure.Registry;

/// <summary>Fan-out read implementation for group read models.</summary>
public sealed class GroupReadStore : IGroupReadStore
{
    private readonly ICompanyFanOut _fanOut;
    private readonly IClock _clock;

    public GroupReadStore(ICompanyFanOut fanOut, IClock clock)
    {
        _fanOut = fanOut;
        _clock = clock;
    }

    public async Task<GroupAvailability> GetAvailabilityAsync(Guid tenantId, IEnumerable<Guid> companyIds, CancellationToken cancellationToken = default)
    {
        var ids = companyIds.Distinct().ToArray();
        var results = await _fanOut.ReadAsync<CompanyAvailability>(ids, async (companyId, ct) =>
        {
            return new CompanyAvailability(
                companyId,
                "",
                0,
                0,
                0,
                _clock.UtcNow,
                IsStale: false);
        }, cancellationToken);

        var companies = results
            .Where(r => r.Succeeded && r.Value is not null)
            .Select(r => r.Value!)
            .ToList();

        return new GroupAvailability(companies, _clock.UtcNow);
    }

    public async Task<GroupAvailability> RebuildAsync(Guid tenantId, IEnumerable<Guid> companyIds, CancellationToken cancellationToken = default)
    {
        return await GetAvailabilityAsync(tenantId, companyIds, cancellationToken);
    }
}
