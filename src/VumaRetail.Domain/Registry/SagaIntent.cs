using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Registry;

/// <summary>
/// A cross-database operation, recorded in full before anything happens (ADR-116). The row exists here;
/// what raises one, dispatches its legs, and drives it to <see cref="SagaIntentStatus.Settled"/> is 06d's
/// <c>ISagaCoordinator</c> — nothing in this stage constructs one outside a test.
/// </summary>
/// <remarks>
/// <para>
/// <c>Kind</c> and <c>Payload</c> never change once raised — that is what "immutable intent" means. Only
/// <see cref="Status"/> moves, which is why this is a plain mutable entity rather than
/// <c>IImmutableRecord</c>: that guard would also block the status transitions the coordinator needs to
/// make.
/// </para>
/// <para>
/// Replicated <see cref="ReplicationScope.StoreToCloud"/> with <see cref="ConflictPolicy.AppendOnly"/> —
/// the in-flight and reconciliation dashboards (`docs/MULTI_COMPANY.md` §9) are cloud-side, and an
/// intent's status only ever moves forward, the same ledger-like shape as the sync outbox.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.AppendOnly)]
public sealed class SagaIntent : Entity
{
    private SagaIntent(Guid tenantId, string kind, string payload)
        : base(tenantId)
    {
        Kind = kind;
        Payload = payload;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private SagaIntent()
    {
    }

    /// <summary>What kind of cross-database operation this is, for example <c>"GroupReceiptAllocation"</c>.</summary>
    public string Kind { get; private set; } = string.Empty;

    /// <summary>The full, immutable description of what is to happen, as JSON.</summary>
    public string Payload { get; private set; } = "{}";

    /// <summary>Where the intent stands.</summary>
    public SagaIntentStatus Status { get; private set; } = SagaIntentStatus.Raised;

    /// <summary>When the intent moved out of <see cref="SagaIntentStatus.Raised"/>, or <c>null</c>.</summary>
    public DateTimeOffset? StartedAt { get; private set; }

    /// <summary>When the intent reached a terminal status, or <c>null</c> while still in flight.</summary>
    public DateTimeOffset? SettledAt { get; private set; }

    /// <summary>Records the immutable intent. Legs are raised separately, each dispatched to one company.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="kind">What kind of operation this is.</param>
    /// <param name="payload">The full description, as JSON.</param>
    public static SagaIntent Raise(Guid tenantId, string kind, string payload)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("An intent must belong to a tenant.", nameof(tenantId));
        }

        if (string.IsNullOrWhiteSpace(kind))
        {
            throw new ArgumentException("An intent must name its kind.", nameof(kind));
        }

        return new SagaIntent(tenantId, kind.Trim(), payload ?? "{}");
    }

    /// <summary>Moves the intent to a new status. Terminal statuses stamp <see cref="SettledAt"/>.</summary>
    /// <param name="status">The new status.</param>
    /// <param name="at">When the transition happened, UTC.</param>
    public void MoveTo(SagaIntentStatus status, DateTimeOffset at)
    {
        if (Status is SagaIntentStatus.Raised && status is not SagaIntentStatus.Raised)
        {
            StartedAt = at;
        }

        Status = status;

        if (status is SagaIntentStatus.Settled or SagaIntentStatus.Compensated)
        {
            SettledAt = at;
        }
    }
}
