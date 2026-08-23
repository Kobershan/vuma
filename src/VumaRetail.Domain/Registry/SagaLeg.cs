using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Registry;

/// <summary>
/// One company's share of a <see cref="SagaIntent"/> — idempotent by construction, keyed by
/// <c>(IntentId, LegKey)</c> (ADR-116). Dispatch, retry and acknowledgement are 06d's job; this is the
/// row that records the fact of the leg.
/// </summary>
/// <remarks>Replicated the same shape as <see cref="SagaIntent"/>, for the same dashboards.</remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.AppendOnly)]
public sealed class SagaLeg : Entity
{
    private SagaLeg(Guid tenantId, Guid intentId, string legKey, Guid companyId)
        : base(tenantId)
    {
        IntentId = intentId;
        LegKey = legKey;
        CompanyId = companyId;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private SagaLeg()
    {
    }

    /// <summary>The intent this leg belongs to.</summary>
    public Guid IntentId { get; private set; }

    /// <summary>
    /// The leg's identity within its intent — stable across retries, so a re-dispatch of the same leg
    /// after a restore (ADR-120) is the same key and therefore the same idempotent operation.
    /// </summary>
    public string LegKey { get; private set; } = string.Empty;

    /// <summary>The company database this leg is dispatched to.</summary>
    public Guid CompanyId { get; private set; }

    /// <summary>Where this leg stands.</summary>
    public SagaLegStatus Status { get; private set; } = SagaLegStatus.Pending;

    /// <summary>How many dispatch attempts have been made.</summary>
    public int AttemptCount { get; private set; }

    /// <summary>When the company database acknowledged this leg, or <c>null</c>.</summary>
    public DateTimeOffset? AcknowledgedAt { get; private set; }

    /// <summary>Why the last attempt failed, or <c>null</c>.</summary>
    public string? LastError { get; private set; }

    /// <summary>Records one company's share of an intent.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="intentId">The parent intent.</param>
    /// <param name="legKey">The leg's stable key within its intent.</param>
    /// <param name="companyId">The company database this leg targets.</param>
    public static SagaLeg Create(Guid tenantId, Guid intentId, string legKey, Guid companyId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A leg must belong to a tenant.", nameof(tenantId));
        }

        if (intentId == Guid.Empty)
        {
            throw new ArgumentException("A leg must belong to an intent.", nameof(intentId));
        }

        if (string.IsNullOrWhiteSpace(legKey))
        {
            throw new ArgumentException("A leg must carry a stable key.", nameof(legKey));
        }

        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("A leg must target a company.", nameof(companyId));
        }

        return new SagaLeg(tenantId, intentId, legKey.Trim(), companyId);
    }

    /// <summary>Records a dispatch attempt.</summary>
    public void MarkDispatched()
    {
        Status = SagaLegStatus.Dispatched;
        AttemptCount++;
    }

    /// <summary>Records the company database's acknowledgement.</summary>
    /// <param name="at">When the acknowledgement arrived, UTC.</param>
    public void MarkAcknowledged(DateTimeOffset at)
    {
        Status = SagaLegStatus.Acknowledged;
        AcknowledgedAt = at;
        LastError = null;
    }

    /// <summary>Records a failed attempt. Never terminal — the leg stays retryable (ADR-116).</summary>
    /// <param name="error">Why the attempt failed.</param>
    public void MarkFailed(string error)
    {
        ArgumentNullException.ThrowIfNull(error);

        LastError = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;
    }

    /// <summary>Marks the leg as under compensation.</summary>
    public void MarkCompensating() => Status = SagaLegStatus.Compensating;

    /// <summary>Marks the leg's compensation as acknowledged.</summary>
    public void MarkCompensated() => Status = SagaLegStatus.Compensated;

    /// <summary>Marks the leg as outstanding past its timeout.</summary>
    public void MarkAlarmed() => Status = SagaLegStatus.Alarmed;

    /// <summary>The widest an error message is stored. Matches the column.</summary>
    public const int MaxErrorLength = 1024;
}
