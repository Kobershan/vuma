using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Crm;

namespace VumaRetail.Application.Crm.Queries;

/// <summary>One lead, as read back out.</summary>
public sealed record LeadEntry(
    Guid Id,
    Guid? CompanyId,
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    string? Company,
    LeadSource Source,
    LeadStatus Status,
    Guid? AssignedTo,
    Guid? CustomerId,
    DateTimeOffset? ConvertedAt);

/// <summary>Reads one lead by id.</summary>
/// <param name="LeadId">The lead.</param>
public sealed record GetLeadQuery(Guid LeadId) : IQuery<LeadEntry>;

/// <summary>Reads the lead.</summary>
/// <param name="leads">Lead persistence.</param>
public sealed class GetLeadQueryHandler(ILeadRepository leads)
    : IQueryHandler<GetLeadQuery, LeadEntry>
{
    /// <inheritdoc />
    public async Task<LeadEntry> HandleAsync(GetLeadQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        Lead lead = await leads
            .FindAsync(query.LeadId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new LeadNotFoundException();

        return ToEntry(lead);
    }

    internal static LeadEntry ToEntry(Lead lead) => new(
        lead.Id, lead.CompanyId, lead.FirstName, lead.LastName, lead.Email, lead.Phone,
        lead.Company, lead.Source, lead.Status, lead.AssignedTo, lead.CustomerId, lead.ConvertedAt);
}

/// <summary>Pages leads, newest first.</summary>
/// <param name="CompanyId">Restricts to a company, or <c>null</c> for all.</param>
/// <param name="Status">Restricts to a status, or <c>null</c> for all.</param>
/// <param name="Limit">Page size.</param>
/// <param name="After">Keyset cursor: the last id of the previous page, if any.</param>
public sealed record ListLeadsQuery(Guid? CompanyId, LeadStatus? Status, int? Limit, string? After)
    : IQuery<PageResult<LeadEntry>>;

/// <summary>Reads the page.</summary>
/// <param name="leads">Lead persistence.</param>
public sealed class ListLeadsQueryHandler(ILeadRepository leads)
    : IQueryHandler<ListLeadsQuery, PageResult<LeadEntry>>
{
    /// <inheritdoc />
    public async Task<PageResult<LeadEntry>> HandleAsync(ListLeadsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        int limit = Paging.Clamp(query.Limit);
        Guid? afterId = Guid.TryParse(query.After, out Guid parsed) ? parsed : null;

        (IReadOnlyList<Lead> items, bool hasMore) = await leads
            .ListPageAsync(query.CompanyId, query.Status, afterId, limit, cancellationToken)
            .ConfigureAwait(false);

        string? cursor = hasMore && items.Count > 0 ? items[^1].Id.ToString("N") : null;
        return new PageResult<LeadEntry>([.. items.Select(GetLeadQueryHandler.ToEntry)], cursor, hasMore);
    }
}

/// <summary>One opportunity, as read back out.</summary>
public sealed record OpportunityEntry(
    Guid Id,
    Guid? CompanyId,
    string Title,
    string? Description,
    decimal ExpectedAmount,
    string Currency,
    OpportunityStage Stage,
    byte Probability,
    DateOnly? CloseDate,
    string? LossReason,
    Guid? LeadId,
    Guid? CustomerId,
    Guid? AssignedTo);

/// <summary>Reads one opportunity by id.</summary>
/// <param name="OpportunityId">The opportunity.</param>
public sealed record GetOpportunityQuery(Guid OpportunityId) : IQuery<OpportunityEntry>;

/// <summary>Reads the opportunity.</summary>
/// <param name="opportunities">Opportunity persistence.</param>
public sealed class GetOpportunityQueryHandler(IOpportunityRepository opportunities)
    : IQueryHandler<GetOpportunityQuery, OpportunityEntry>
{
    /// <inheritdoc />
    public async Task<OpportunityEntry> HandleAsync(GetOpportunityQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        Opportunity opportunity = await opportunities
            .FindAsync(query.OpportunityId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new OpportunityNotFoundException();

        return ToEntry(opportunity);
    }

    internal static OpportunityEntry ToEntry(Opportunity opportunity) => new(
        opportunity.Id, opportunity.CompanyId, opportunity.Title, opportunity.Description,
        opportunity.ExpectedValue.Amount, opportunity.ExpectedValue.Currency, opportunity.Stage,
        opportunity.Probability, opportunity.CloseDate, opportunity.LossReason,
        opportunity.LeadId, opportunity.CustomerId, opportunity.AssignedTo);
}

/// <summary>Pages opportunities, newest first.</summary>
/// <param name="CompanyId">Restricts to a company, or <c>null</c> for all.</param>
/// <param name="Stage">Restricts to a stage, or <c>null</c> for all.</param>
/// <param name="Limit">Page size.</param>
/// <param name="After">Keyset cursor: the last id of the previous page, if any.</param>
public sealed record ListOpportunitiesQuery(Guid? CompanyId, OpportunityStage? Stage, int? Limit, string? After)
    : IQuery<PageResult<OpportunityEntry>>;

/// <summary>Reads the page.</summary>
/// <param name="opportunities">Opportunity persistence.</param>
public sealed class ListOpportunitiesQueryHandler(IOpportunityRepository opportunities)
    : IQueryHandler<ListOpportunitiesQuery, PageResult<OpportunityEntry>>
{
    /// <inheritdoc />
    public async Task<PageResult<OpportunityEntry>> HandleAsync(
        ListOpportunitiesQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        int limit = Paging.Clamp(query.Limit);
        Guid? afterId = Guid.TryParse(query.After, out Guid parsed) ? parsed : null;

        (IReadOnlyList<Opportunity> items, bool hasMore) = await opportunities
            .ListPageAsync(query.CompanyId, query.Stage, afterId, limit, cancellationToken)
            .ConfigureAwait(false);

        string? cursor = hasMore && items.Count > 0 ? items[^1].Id.ToString("N") : null;
        return new PageResult<OpportunityEntry>([.. items.Select(GetOpportunityQueryHandler.ToEntry)], cursor, hasMore);
    }
}

/// <summary>One activity, as read back out.</summary>
public sealed record ActivityEntry(
    Guid Id,
    ActivityType Type,
    ActivityDirection Direction,
    string Subject,
    string? Body,
    DateTimeOffset HappenedAt,
    int? DurationMinutes,
    Guid? LeadId,
    Guid? OpportunityId,
    Guid? CustomerId);

/// <summary>Lists activities for a lead, opportunity or customer.</summary>
/// <param name="LeadId">The lead, if listing for one.</param>
/// <param name="OpportunityId">The opportunity, if listing for one.</param>
/// <param name="CustomerId">The customer, if listing for one.</param>
/// <param name="Limit">Maximum rows for a customer listing.</param>
public sealed record ListActivitiesQuery(Guid? LeadId, Guid? OpportunityId, Guid? CustomerId, int? Limit)
    : IQuery<IReadOnlyList<ActivityEntry>>;

/// <summary>Reads the activities.</summary>
/// <param name="activities">Activity persistence.</param>
public sealed class ListActivitiesQueryHandler(IActivityRepository activities)
    : IQueryHandler<ListActivitiesQuery, IReadOnlyList<ActivityEntry>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ActivityEntry>> HandleAsync(
        ListActivitiesQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<Activity> rows = query switch
        {
            { LeadId: not null } => await activities
                .ListForLeadAsync(query.LeadId.Value, cancellationToken).ConfigureAwait(false),
            { OpportunityId: not null } => await activities
                .ListForOpportunityAsync(query.OpportunityId.Value, cancellationToken).ConfigureAwait(false),
            { CustomerId: not null } => await activities
                .ListForCustomerAsync(query.CustomerId.Value, Paging.Clamp(query.Limit), cancellationToken)
                .ConfigureAwait(false),
            _ => [],
        };

        return [.. rows.Select(row => new ActivityEntry(
            row.Id, row.ActivityType, row.Direction, row.Subject, row.Body, row.HappenedAt,
            row.DurationMinutes, row.LeadId, row.OpportunityId, row.CustomerId))];
    }
}

/// <summary>One segment, as read back out.</summary>
public sealed record SegmentEntry(
    Guid Id,
    string Name,
    string? Description,
    SegmentKind Kind,
    string? QueryExpression,
    bool IsActive);

/// <summary>Lists segments for a company.</summary>
/// <param name="CompanyId">The company, or <c>null</c> for all.</param>
public sealed record ListSegmentsQuery(Guid? CompanyId) : IQuery<IReadOnlyList<SegmentEntry>>;

/// <summary>Reads the segments.</summary>
/// <param name="segments">Segment persistence.</param>
public sealed class ListSegmentsQueryHandler(ISegmentRepository segments)
    : IQueryHandler<ListSegmentsQuery, IReadOnlyList<SegmentEntry>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<SegmentEntry>> HandleAsync(
        ListSegmentsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<Segment> rows = await segments
            .ListAsync(query.CompanyId, cancellationToken)
            .ConfigureAwait(false);

        return [.. rows.Select(row => new SegmentEntry(
            row.Id, row.Name, row.Description, row.Kind, row.QueryExpression, row.IsActive))];
    }
}

/// <summary>Lists the member ids of a static segment.</summary>
/// <param name="SegmentId">The segment.</param>
public sealed record GetSegmentMembersQuery(Guid SegmentId)
    : IQuery<IReadOnlyList<SegmentMemberEntry>>;

/// <summary>One static membership, as read back out.</summary>
/// <param name="MemberType">What kind of member.</param>
/// <param name="MemberId">The member.</param>
/// <param name="AddedAt">When added, UTC.</param>
public sealed record SegmentMemberEntry(MemberType MemberType, Guid MemberId, DateTimeOffset AddedAt);

/// <summary>Reads the membership.</summary>
/// <param name="segments">Segment persistence.</param>
/// <param name="members">Membership persistence.</param>
public sealed class GetSegmentMembersQueryHandler(
    ISegmentRepository segments,
    ISegmentMemberRepository members) : IQueryHandler<GetSegmentMembersQuery, IReadOnlyList<SegmentMemberEntry>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<SegmentMemberEntry>> HandleAsync(
        GetSegmentMembersQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        Segment segment = await segments
            .FindAsync(query.SegmentId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new SegmentNotFoundException();

        segment.RefuseMemberWrite();

        IReadOnlyList<SegmentMember> rows = await members
            .ListMembersAsync(query.SegmentId, cancellationToken)
            .ConfigureAwait(false);

        return [.. rows.Select(row => new SegmentMemberEntry(row.MemberType, row.MemberId, row.AddedAt))];
    }
}

/// <summary>One consent row, as read back out.</summary>
public sealed record ConsentEntry(
    Guid CustomerId,
    ConsentType Type,
    ConsentState State,
    DateTimeOffset? GrantedAt,
    DateTimeOffset? WithdrawnAt,
    DateTimeOffset? ExpiresAt);

/// <summary>Reads every consent row for a customer.</summary>
/// <param name="CustomerId">The customer.</param>
public sealed record GetConsentStateQuery(Guid CustomerId) : IQuery<IReadOnlyList<ConsentEntry>>;

/// <summary>Reads the consent state.</summary>
/// <param name="consents">Consent persistence.</param>
public sealed class GetConsentStateQueryHandler(IConsentRepository consents)
    : IQueryHandler<GetConsentStateQuery, IReadOnlyList<ConsentEntry>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ConsentEntry>> HandleAsync(
        GetConsentStateQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<Consent> rows = await consents
            .ListForCustomerAsync(query.CustomerId, cancellationToken)
            .ConfigureAwait(false);

        return [.. rows.Select(row => new ConsentEntry(
            row.CustomerId, row.Type, row.State, row.GrantedAt, row.WithdrawnAt, row.ExpiresAt))];
    }
}

/// <summary>Reads the 360° view for a customer.</summary>
/// <param name="CustomerId">The customer.</param>
public sealed record GetCustomer360ViewQuery(Guid CustomerId) : IQuery<Customer360View>;

/// <summary>Materialises the 360° view live from leads, opportunities, activities, segments and consent.</summary>
/// <param name="view">The view service.</param>
public sealed class GetCustomer360ViewQueryHandler(ICustomer360ViewService view)
    : IQueryHandler<GetCustomer360ViewQuery, Customer360View>
{
    /// <inheritdoc />
    public Task<Customer360View> HandleAsync(GetCustomer360ViewQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return view.GetViewAsync(query.CustomerId, cancellationToken);
    }
}
