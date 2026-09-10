using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Crm;

namespace VumaRetail.Application.Crm;

/// <summary>Evaluates consent state off the stored rows (Stage 19).</summary>
/// <param name="consents">Consent persistence.</param>
public sealed class ConsentService(IConsentRepository consents) : IConsentService
{
    /// <inheritdoc />
    public async Task<ConsentState> GetStateAsync(Guid customerId, ConsentType type, CancellationToken cancellationToken = default)
    {
        Consent? row = await consents
            .FindAsync(customerId, type, cancellationToken)
            .ConfigureAwait(false);

        return row?.State ?? ConsentState.NotAsked;
    }

    /// <inheritdoc />
    public async Task<bool> IsValidAsync(Guid customerId, ConsentType type, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        Consent? row = await consents
            .FindAsync(customerId, type, cancellationToken)
            .ConfigureAwait(false);

        return row?.IsValid(at) ?? false;
    }
}

/// <summary>Evaluates segment membership: static rows, plus the v1 dynamic predicates (Stage 19).</summary>
/// <param name="segments">Segment persistence.</param>
/// <param name="members">Membership persistence.</param>
public sealed class SegmentService(ISegmentRepository segments, ISegmentMemberRepository members)
    : ISegmentService
{
    /// <inheritdoc />
    public async Task<bool> IsMemberAsync(
        Guid segmentId, MemberType memberType, Guid memberId, CancellationToken cancellationToken = default)
    {
        Segment? segment = await segments
            .FindAsync(segmentId, cancellationToken)
            .ConfigureAwait(false);

        if (segment is null || !segment.IsActive || segment.Kind != SegmentKind.Static)
        {
            return false;
        }

        return await members
            .ExistsAsync(segmentId, memberType, memberId, cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>Materialises the 360° view live on every read (Stage 19).</summary>
/// <param name="leads">Lead persistence.</param>
/// <param name="opportunities">Opportunity persistence.</param>
/// <param name="activities">Activity persistence.</param>
/// <param name="segments">Segment persistence.</param>
/// <param name="members">Membership persistence.</param>
/// <param name="consents">Consent persistence.</param>
/// <param name="clock">The only source of time.</param>
public sealed class Customer360ViewService(
    ILeadRepository leads,
    IOpportunityRepository opportunities,
    IActivityRepository activities,
    ISegmentRepository segments,
    ISegmentMemberRepository members,
    IConsentRepository consents,
    IClock clock) : ICustomer360ViewService
{
    /// <inheritdoc />
    public async Task<Customer360View> GetViewAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        int leadCount = await leads
            .CountForCustomerAsync(customerId, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<Opportunity> deals = await opportunities
            .ListForCustomerAsync(customerId, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<Opportunity> open = [.. deals.Where(deal =>
            deal.Stage is not OpportunityStage.Won and not OpportunityStage.Lost)];

        // One currency per view: the majority currency wins, so a mixed-currency book still
        // reports a meaningful total rather than a silent rands-plus-dollars sum.
        string currency = open
            .GroupBy(deal => deal.ExpectedValue.Currency)
            .OrderByDescending(group => group.Count())
            .Select(group => group.Key)
            .FirstOrDefault() ?? "ZAR";

        decimal openValue = open
            .Where(deal => deal.ExpectedValue.Currency == currency)
            .Sum(deal => deal.ExpectedValue.Amount);

        int activityCount = await activities
            .CountForCustomerAsync(customerId, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<SegmentMember> memberships = await members
            .ListSegmentsForMemberAsync(MemberType.Customer, customerId, cancellationToken)
            .ConfigureAwait(false);

        var segmentNames = new List<string>();
        foreach (SegmentMember membership in memberships)
        {
            Segment? segment = await segments
                .FindAsync(membership.SegmentId, cancellationToken)
                .ConfigureAwait(false);

            if (segment is not null && segment.IsActive)
            {
                segmentNames.Add(segment.Name);
            }
        }

        IReadOnlyList<Consent> consentRows = await consents
            .ListForCustomerAsync(customerId, cancellationToken)
            .ConfigureAwait(false);

        return new Customer360View(
            customerId,
            leadCount,
            open.Count,
            openValue,
            currency,
            activityCount,
            segmentNames,
            [.. consentRows.Select(row => new ConsentStateEntry(row.Type, row.State))],
            clock.UtcNow);
    }
}
