using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Crm;

namespace VumaRetail.Application.Crm;

/// <summary>Persists leads (Stage 19).</summary>
public interface ILeadRepository
{
    /// <summary>Finds a lead by id in the ambient tenant.</summary>
    /// <param name="leadId">The lead id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<Lead?> FindAsync(Guid leadId, CancellationToken cancellationToken = default);

    /// <summary>Finds a live lead by email in a store, for the natural-key check.</summary>
    /// <param name="email">Lower-cased email.</param>
    /// <param name="storeId">The store, or <c>null</c> for tenant-wide.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<Lead?> FindByEmailAsync(string email, Guid? storeId, CancellationToken cancellationToken = default);

    /// <summary>Pages leads, newest first.</summary>
    /// <param name="companyId">Restricts to a company, or <c>null</c> for all.</param>
    /// <param name="status">Restricts to a status, or <c>null</c> for all.</param>
    /// <param name="afterId">Keyset cursor: the last id of the previous page.</param>
    /// <param name="limit">Page size, already clamped.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<(IReadOnlyList<Lead> Items, bool HasMore)> ListPageAsync(
        Guid? companyId, LeadStatus? status, Guid? afterId, int limit,
        CancellationToken cancellationToken = default);

    /// <summary>Stages a lead for insert. The pipeline commits.</summary>
    /// <param name="lead">The lead.</param>
    void Add(Lead lead);

    /// <summary>Counts leads linked to a converted customer.</summary>
    /// <param name="customerId">The customer.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<int> CountForCustomerAsync(Guid customerId, CancellationToken cancellationToken = default);
}

/// <summary>Persists opportunities (Stage 19).</summary>
public interface IOpportunityRepository
{
    /// <summary>Finds an opportunity by id in the ambient tenant.</summary>
    /// <param name="opportunityId">The opportunity id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<Opportunity?> FindAsync(Guid opportunityId, CancellationToken cancellationToken = default);

    /// <summary>Lists open opportunities for a lead.</summary>
    /// <param name="leadId">The lead id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<Opportunity>> ListForLeadAsync(Guid leadId, CancellationToken cancellationToken = default);

    /// <summary>Lists opportunities for a customer.</summary>
    /// <param name="customerId">The customer id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<Opportunity>> ListForCustomerAsync(Guid customerId, CancellationToken cancellationToken = default);

    /// <summary>Pages opportunities, newest first.</summary>
    /// <param name="companyId">Restricts to a company, or <c>null</c> for all.</param>
    /// <param name="stage">Restricts to a stage, or <c>null</c> for all.</param>
    /// <param name="afterId">Keyset cursor: the last id of the previous page.</param>
    /// <param name="limit">Page size, already clamped.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<(IReadOnlyList<Opportunity> Items, bool HasMore)> ListPageAsync(
        Guid? companyId, OpportunityStage? stage, Guid? afterId, int limit,
        CancellationToken cancellationToken = default);

    /// <summary>Stages an opportunity for insert. The pipeline commits.</summary>
    /// <param name="opportunity">The opportunity.</param>
    void Add(Opportunity opportunity);
}

/// <summary>Persists activities (Stage 19). Append-only reads; no update path.</summary>
public interface IActivityRepository
{
    /// <summary>Lists activities for a lead, oldest first.</summary>
    /// <param name="leadId">The lead id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<Activity>> ListForLeadAsync(Guid leadId, CancellationToken cancellationToken = default);

    /// <summary>Lists activities for an opportunity, oldest first.</summary>
    /// <param name="opportunityId">The opportunity id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<Activity>> ListForOpportunityAsync(Guid opportunityId, CancellationToken cancellationToken = default);

    /// <summary>Lists activities for a customer, newest first, capped.</summary>
    /// <param name="customerId">The customer id.</param>
    /// <param name="limit">Maximum rows.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<Activity>> ListForCustomerAsync(Guid customerId, int limit, CancellationToken cancellationToken = default);

    /// <summary>Counts activities for a customer.</summary>
    /// <param name="customerId">The customer id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<int> CountForCustomerAsync(Guid customerId, CancellationToken cancellationToken = default);

    /// <summary>Stages an activity for insert. The pipeline commits.</summary>
    /// <param name="activity">The activity.</param>
    void Add(Activity activity);
}

/// <summary>Persists segments (Stage 19).</summary>
public interface ISegmentRepository
{
    /// <summary>Finds a segment by id in the ambient tenant.</summary>
    /// <param name="segmentId">The segment id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<Segment?> FindAsync(Guid segmentId, CancellationToken cancellationToken = default);

    /// <summary>Lists all active segments for a company.</summary>
    /// <param name="companyId">The company, or <c>null</c> for all.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<Segment>> ListAsync(Guid? companyId, CancellationToken cancellationToken = default);

    /// <summary>Stages a segment for insert. The pipeline commits.</summary>
    /// <param name="segment">The segment.</param>
    void Add(Segment segment);
}

/// <summary>Persists static segment memberships (Stage 19).</summary>
public interface ISegmentMemberRepository
{
    /// <summary>Whether an explicit membership row exists.</summary>
    /// <param name="segmentId">The segment.</param>
    /// <param name="memberType">What kind of member.</param>
    /// <param name="memberId">The member.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<bool> ExistsAsync(Guid segmentId, MemberType memberType, Guid memberId, CancellationToken cancellationToken = default);

    /// <summary>Lists member ids of a static segment.</summary>
    /// <param name="segmentId">The segment.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<SegmentMember>> ListMembersAsync(Guid segmentId, CancellationToken cancellationToken = default);

    /// <summary>Lists the segments a member explicitly belongs to.</summary>
    /// <param name="memberType">What kind of member.</param>
    /// <param name="memberId">The member.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<SegmentMember>> ListSegmentsForMemberAsync(MemberType memberType, Guid memberId, CancellationToken cancellationToken = default);

    /// <summary>Stages a membership for insert. The pipeline commits.</summary>
    /// <param name="member">The membership.</param>
    void Add(SegmentMember member);
}

/// <summary>Persists consent records (Stage 19).</summary>
public interface IConsentRepository
{
    /// <summary>Finds the consent row for one purpose and customer.</summary>
    /// <param name="customerId">The customer.</param>
    /// <param name="type">The purpose.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<Consent?> FindAsync(Guid customerId, ConsentType type, CancellationToken cancellationToken = default);

    /// <summary>Lists every consent row for a customer.</summary>
    /// <param name="customerId">The customer.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<Consent>> ListForCustomerAsync(Guid customerId, CancellationToken cancellationToken = default);

    /// <summary>Stages a consent row for insert. The pipeline commits.</summary>
    /// <param name="consent">The consent.</param>
    void Add(Consent consent);
}

/// <summary>
/// The consent contract Stage 20 (and 22) consume: may we contact this customer for this
/// purpose, right now (Stage 19).
/// </summary>
public interface IConsentService
{
    /// <summary>Reads the consent state for one purpose and customer.</summary>
    /// <param name="customerId">The customer.</param>
    /// <param name="type">The purpose.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<ConsentState> GetStateAsync(Guid customerId, ConsentType type, CancellationToken cancellationToken = default);

    /// <summary>Whether contacting is permitted right now.</summary>
    /// <param name="customerId">The customer.</param>
    /// <param name="type">The purpose.</param>
    /// <param name="at">The instant to evaluate at, UTC.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<bool> IsValidAsync(Guid customerId, ConsentType type, DateTimeOffset at, CancellationToken cancellationToken = default);
}

/// <summary>The segment-membership contract Stage 20 (and 22) consume (Stage 19).</summary>
public interface ISegmentService
{
    /// <summary>Whether a member belongs to a segment right now.</summary>
    /// <param name="segmentId">The segment.</param>
    /// <param name="memberType">What kind of member.</param>
    /// <param name="memberId">The member.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<bool> IsMemberAsync(Guid segmentId, MemberType memberType, Guid memberId, CancellationToken cancellationToken = default);
}

/// <summary>One consent state in the 360° view (Stage 19).</summary>
/// <param name="Type">The purpose.</param>
/// <param name="State">Current state.</param>
public sealed record ConsentStateEntry(ConsentType Type, ConsentState State);

/// <summary>The 360° customer view: identity counts, segments and consent, read live (Stage 19).</summary>
/// <param name="CustomerId">The customer.</param>
/// <param name="LeadCount">Leads linked to this customer.</param>
/// <param name="OpenOpportunityCount">Non-closed opportunities.</param>
/// <param name="OpenOpportunityValue">Sum of open expected values.</param>
/// <param name="OpportunityCurrency">Currency of the summed value.</param>
/// <param name="ActivityCount">Logged interactions.</param>
/// <param name="SegmentNames">Static segments the customer belongs to.</param>
/// <param name="Consents">Consent state per purpose.</param>
/// <param name="AsAt">When the view was read, UTC.</param>
public sealed record Customer360View(
    Guid CustomerId,
    int LeadCount,
    int OpenOpportunityCount,
    decimal OpenOpportunityValue,
    string OpportunityCurrency,
    int ActivityCount,
    IReadOnlyList<string> SegmentNames,
    IReadOnlyList<ConsentStateEntry> Consents,
    DateTimeOffset AsAt);

/// <summary>The 360° view contract Stages 10, 20 and 22 consume (Stage 19).</summary>
public interface ICustomer360ViewService
{
    /// <summary>Reads the 360° view for a customer.</summary>
    /// <param name="customerId">The customer.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<Customer360View> GetViewAsync(Guid customerId, CancellationToken cancellationToken = default);
}
