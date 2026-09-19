using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Marketing;
using VumaRetail.Domain.Marketing;

namespace VumaRetail.Infrastructure.Persistence.Repositories;

public sealed class JourneyDefinitionRepository(VumaRetailDbContext db) : IJourneyDefinitionRepository
{
    public Task<JourneyDefinition?> FindAsync(Guid id, CancellationToken cancellationToken = default) => db.JourneyDefinitions.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    public void Add(JourneyDefinition journey) => db.JourneyDefinitions.Add(journey);
}
public sealed class JourneyEnrollmentRepository(VumaRetailDbContext db) : IJourneyEnrollmentRepository
{
    public Task<JourneyEnrollment?> FindByIdempotencyKeyAsync(string key, CancellationToken cancellationToken = default) => db.JourneyEnrollments.FirstOrDefaultAsync(x => x.IdempotencyKey == key, cancellationToken);
    public void Add(JourneyEnrollment enrollment) => db.JourneyEnrollments.Add(enrollment);
}
public sealed class AttributionEventRepository(VumaRetailDbContext db) : IAttributionEventRepository
{
    public void Add(AttributionEvent attributionEvent) => db.AttributionEvents.Add(attributionEvent);
    public async Task<IReadOnlyList<AttributionEvent>> ListAsync(Guid companyId, Guid? campaignId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        IQueryable<AttributionEvent> query = db.AttributionEvents.Where(x => x.CompanyId == companyId && x.OccurredAt >= from && x.OccurredAt <= to);
        if (campaignId is { } id)
        {
            query = query.Where(x => x.CampaignId == id);
        }

        return await query.OrderByDescending(x => x.OccurredAt).Take(5000).ToListAsync(cancellationToken).ConfigureAwait(false);
    }
}
