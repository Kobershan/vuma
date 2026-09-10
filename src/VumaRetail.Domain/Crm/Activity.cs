using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Crm;

/// <summary>
/// One logged interaction with a lead, opportunity or customer (Stage 19). Append-only: once
/// committed, subject/body/type never change. Deleting an interaction record is a compliance
/// violation (POPIA §9 — records stay retrievable for the retention period).
/// </summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.AppendOnly)]
public sealed class Activity : Entity, IImmutableRecord
{
    private Activity()
    {
    }

    /// <summary>Logs an interaction.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="companyId">The owning company.</param>
    /// <param name="type">What kind of interaction.</param>
    /// <param name="subject">Short subject.</param>
    /// <param name="body">Detail, if any.</param>
    /// <param name="happenedAt">When it happened, UTC. From <c>IClock</c>.</param>
    /// <param name="direction">Which way it flowed.</param>
    /// <param name="durationMinutes">Duration, if known.</param>
    /// <param name="leadId">The lead it concerns, if any.</param>
    /// <param name="opportunityId">The opportunity it concerns, if any.</param>
    /// <param name="customerId">The customer it concerns, if any.</param>
    /// <param name="storeId">The owning store, if any.</param>
    public Activity(
        Guid tenantId,
        Guid companyId,
        ActivityType type,
        string subject,
        string? body,
        DateTimeOffset happenedAt,
        ActivityDirection direction = ActivityDirection.Outbound,
        int? durationMinutes = null,
        Guid? leadId = null,
        Guid? opportunityId = null,
        Guid? customerId = null,
        Guid? storeId = null)
        : base(tenantId, storeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        AssignCompany(companyId);
        ActivityType = type;
        Direction = direction;
        Subject = subject.Trim();
        Body = body;
        HappenedAt = happenedAt;
        DurationMinutes = durationMinutes;
        LeadId = leadId;
        OpportunityId = opportunityId;
        CustomerId = customerId;
    }

    /// <summary>What kind of interaction.</summary>
    public ActivityType ActivityType { get; private set; }

    /// <summary>Which way it flowed.</summary>
    public ActivityDirection Direction { get; private set; }

    /// <summary>Short subject. Immutable after logging.</summary>
    public string Subject { get; private set; } = string.Empty;

    /// <summary>Detail. Immutable after logging.</summary>
    public string? Body { get; private set; }

    /// <summary>When it happened, UTC.</summary>
    public DateTimeOffset HappenedAt { get; private set; }

    /// <summary>Duration in minutes, if known.</summary>
    public int? DurationMinutes { get; private set; }

    /// <summary>The lead it concerns, if any. A plain id, never a cross-schema FK.</summary>
    public Guid? LeadId { get; private set; }

    /// <summary>The opportunity it concerns, if any.</summary>
    public Guid? OpportunityId { get; private set; }

    /// <summary>The customer it concerns, if any.</summary>
    public Guid? CustomerId { get; private set; }

    /// <summary>Refused: a logged interaction's body never changes.</summary>
    /// <param name="body">Ignored.</param>
    /// <exception cref="ActivityImmutableException">Always.</exception>
    public void UpdateBody(string? body) => throw new ActivityImmutableException();

    /// <summary>Refused: a logged interaction's subject never changes.</summary>
    /// <param name="subject">Ignored.</param>
    /// <exception cref="ActivityImmutableException">Always.</exception>
    public void UpdateSubject(string subject) => throw new ActivityImmutableException();

    /// <summary>Refused: a logged interaction's type never changes.</summary>
    /// <param name="type">Ignored.</param>
    /// <exception cref="ActivityImmutableException">Always.</exception>
    public void UpdateType(ActivityType type) => throw new ActivityImmutableException();

    /// <summary>Refused: interaction records are retained, never deleted (POPIA §9).</summary>
    /// <exception cref="ActivityImmutableException">Always.</exception>
    public void MarkDeleted() => throw new ActivityImmutableException();
}
