using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Crm;
using VumaRetail.Domain.Crm;
using VumaRetail.Infrastructure.Persistence;

namespace VumaRetail.Infrastructure.Persistence.Repositories;

/// <summary>EF Core implementation of the CRM repositories (Stage 19).</summary>
public sealed class LeadRepository(VumaRetailDbContext context) : ILeadRepository
{
    /// <inheritdoc />
    public Task<Lead?> FindAsync(Guid leadId, CancellationToken cancellationToken = default)
        => context.CrmLeads.FirstOrDefaultAsync(lead => lead.Id == leadId, cancellationToken);

    /// <inheritdoc />
    public Task<Lead?> FindByEmailAsync(string email, Guid? storeId, CancellationToken cancellationToken = default)
        => context.CrmLeads.FirstOrDefaultAsync(
            lead => lead.Email == email && lead.StoreId == storeId && lead.Status != LeadStatus.Converted,
            cancellationToken);

    /// <inheritdoc />
    public async Task<(IReadOnlyList<Lead> Items, bool HasMore)> ListPageAsync(
        Guid? companyId, LeadStatus? status, Guid? afterId, int limit,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset? afterAt = null;

        if (afterId.HasValue)
        {
            DateTimeOffset? cursor = await context.CrmLeads
                .Where(lead => lead.Id == afterId.Value)
                .Select(lead => (DateTimeOffset?)lead.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (cursor is null)
            {
                return ([], false);
            }

            afterAt = cursor;
        }

        List<Lead> items = await context.CrmLeads
            .Where(lead => (companyId == null || lead.CompanyId == companyId)
                && (status == null || lead.Status == status)
                && (afterAt == null
                    || lead.CreatedAt < afterAt
                    || (lead.CreatedAt == afterAt && lead.Id.CompareTo(afterId!.Value) < 0)))
            .OrderByDescending(lead => lead.CreatedAt)
            .ThenByDescending(lead => lead.Id)
            .Take(limit + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        bool hasMore = items.Count > limit;
        return ([.. items.Take(limit)], hasMore);
    }

    /// <inheritdoc />
    public Task<int> CountForCustomerAsync(Guid customerId, CancellationToken cancellationToken = default)
        => context.CrmLeads.CountAsync(lead => lead.CustomerId == customerId, cancellationToken);

    /// <inheritdoc />
    public void Add(Lead lead) => context.CrmLeads.Add(lead);
}

/// <summary>EF Core implementation of the opportunity store (Stage 19).</summary>
public sealed class OpportunityRepository(VumaRetailDbContext context) : IOpportunityRepository
{
    /// <inheritdoc />
    public Task<Opportunity?> FindAsync(Guid opportunityId, CancellationToken cancellationToken = default)
        => context.CrmOpportunities.FirstOrDefaultAsync(
            opportunity => opportunity.Id == opportunityId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Opportunity>> ListForLeadAsync(Guid leadId, CancellationToken cancellationToken = default)
        => await context.CrmOpportunities.AsNoTracking()
            .Where(opportunity => opportunity.LeadId == leadId)
            .OrderByDescending(opportunity => opportunity.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Opportunity>> ListForCustomerAsync(Guid customerId, CancellationToken cancellationToken = default)
        => await context.CrmOpportunities.AsNoTracking()
            .Where(opportunity => opportunity.CustomerId == customerId)
            .OrderByDescending(opportunity => opportunity.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<(IReadOnlyList<Opportunity> Items, bool HasMore)> ListPageAsync(
        Guid? companyId, OpportunityStage? stage, Guid? afterId, int limit,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset? afterAt = null;

        if (afterId.HasValue)
        {
            DateTimeOffset? cursor = await context.CrmOpportunities
                .Where(opportunity => opportunity.Id == afterId.Value)
                .Select(opportunity => (DateTimeOffset?)opportunity.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (cursor is null)
            {
                return ([], false);
            }

            afterAt = cursor;
        }

        List<Opportunity> items = await context.CrmOpportunities
            .Where(opportunity => (companyId == null || opportunity.CompanyId == companyId)
                && (stage == null || opportunity.Stage == stage)
                && (afterAt == null
                    || opportunity.CreatedAt < afterAt
                    || (opportunity.CreatedAt == afterAt && opportunity.Id.CompareTo(afterId!.Value) < 0)))
            .OrderByDescending(opportunity => opportunity.CreatedAt)
            .ThenByDescending(opportunity => opportunity.Id)
            .Take(limit + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        bool hasMore = items.Count > limit;
        return ([.. items.Take(limit)], hasMore);
    }

    /// <inheritdoc />
    public void Add(Opportunity opportunity) => context.CrmOpportunities.Add(opportunity);
}

/// <summary>EF Core implementation of the append-only activity store (Stage 19).</summary>
public sealed class ActivityRepository(VumaRetailDbContext context) : IActivityRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Activity>> ListForLeadAsync(Guid leadId, CancellationToken cancellationToken = default)
        => await context.CrmActivities.AsNoTracking()
            .Where(activity => activity.LeadId == leadId)
            .OrderBy(activity => activity.HappenedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Activity>> ListForOpportunityAsync(Guid opportunityId, CancellationToken cancellationToken = default)
        => await context.CrmActivities.AsNoTracking()
            .Where(activity => activity.OpportunityId == opportunityId)
            .OrderBy(activity => activity.HappenedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Activity>> ListForCustomerAsync(Guid customerId, int limit, CancellationToken cancellationToken = default)
        => await context.CrmActivities.AsNoTracking()
            .Where(activity => activity.CustomerId == customerId)
            .OrderByDescending(activity => activity.HappenedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public Task<int> CountForCustomerAsync(Guid customerId, CancellationToken cancellationToken = default)
        => context.CrmActivities.CountAsync(activity => activity.CustomerId == customerId, cancellationToken);

    /// <inheritdoc />
    public void Add(Activity activity) => context.CrmActivities.Add(activity);
}

/// <summary>EF Core implementation of the segment store (Stage 19).</summary>
public sealed class SegmentRepository(VumaRetailDbContext context) : ISegmentRepository
{
    /// <inheritdoc />
    public Task<Segment?> FindAsync(Guid segmentId, CancellationToken cancellationToken = default)
        => context.CrmSegments.FirstOrDefaultAsync(segment => segment.Id == segmentId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Segment>> ListAsync(Guid? companyId, CancellationToken cancellationToken = default)
        => await context.CrmSegments.AsNoTracking()
            .Where(segment => companyId == null || segment.CompanyId == companyId)
            .OrderBy(segment => segment.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(Segment segment) => context.CrmSegments.Add(segment);
}

/// <summary>EF Core implementation of the static membership store (Stage 19).</summary>
public sealed class SegmentMemberRepository(VumaRetailDbContext context) : ISegmentMemberRepository
{
    /// <inheritdoc />
    public Task<bool> ExistsAsync(
        Guid segmentId, MemberType memberType, Guid memberId, CancellationToken cancellationToken = default)
        => context.CrmSegmentMembers.AnyAsync(
            member => member.SegmentId == segmentId
                && member.MemberType == memberType
                && member.MemberId == memberId,
            cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<SegmentMember>> ListMembersAsync(Guid segmentId, CancellationToken cancellationToken = default)
        => await context.CrmSegmentMembers.AsNoTracking()
            .Where(member => member.SegmentId == segmentId)
            .OrderBy(member => member.AddedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<SegmentMember>> ListSegmentsForMemberAsync(
        MemberType memberType, Guid memberId, CancellationToken cancellationToken = default)
        => await context.CrmSegmentMembers.AsNoTracking()
            .Where(member => member.MemberType == memberType && member.MemberId == memberId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(SegmentMember member) => context.CrmSegmentMembers.Add(member);
}

/// <summary>EF Core implementation of the consent store (Stage 19).</summary>
public sealed class ConsentRepository(VumaRetailDbContext context) : IConsentRepository
{
    /// <inheritdoc />
    public Task<Consent?> FindAsync(Guid customerId, ConsentType type, CancellationToken cancellationToken = default)
        => context.CrmConsents.FirstOrDefaultAsync(
            consent => consent.CustomerId == customerId && consent.Type == type, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Consent>> ListForCustomerAsync(Guid customerId, CancellationToken cancellationToken = default)
        => await context.CrmConsents.AsNoTracking()
            .Where(consent => consent.CustomerId == customerId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(Consent consent) => context.CrmConsents.Add(consent);
}
