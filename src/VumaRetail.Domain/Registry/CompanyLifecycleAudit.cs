using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Registry;

#pragma warning disable CS1591
/// <summary>Append-only audit record for a registry lifecycle transition.</summary>
public sealed class CompanyLifecycleAudit
{
    private CompanyLifecycleAudit() { }
    private CompanyLifecycleAudit(Guid tenantId, Guid companyId, CompanyLifecycleState fromState,
        CompanyLifecycleState toState, string actor, string reason, DateTimeOffset occurredAt)
    {
        Id = UuidV7.NewGuid(); TenantId = tenantId; CompanyId = companyId; FromState = fromState;
        ToState = toState; Actor = actor; Reason = reason; OccurredAt = occurredAt;
    }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid CompanyId { get; private set; }
    public CompanyLifecycleState FromState { get; private set; }
    public CompanyLifecycleState ToState { get; private set; }
    public string Actor { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; private set; }
    public static CompanyLifecycleAudit Record(Guid tenantId, Guid companyId, CompanyLifecycleState fromState,
        CompanyLifecycleState toState, string actor, string reason, DateTimeOffset occurredAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new(tenantId, companyId, fromState, toState, actor.Trim(), reason.Trim(), occurredAt);
    }
}
