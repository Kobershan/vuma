// Registry records are persistence aggregates; their public surface is intentionally simple.
#pragma warning disable CS1591
#pragma warning disable IDE0011
namespace VumaRetail.Domain.Registry;

public sealed class CompanyGroup
{
    private CompanyGroup() { }
    private CompanyGroup(Guid tenantId, string name) { Id = Guid.NewGuid(); TenantId = tenantId; Name = name.Trim(); }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public List<CompanyGroupMember> Members { get; private set; } = [];
    public static CompanyGroup Create(Guid tenantId, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A name is required.", nameof(name));
        return new(tenantId, name);
    }
}

public sealed class CompanyGroupMember
{
    private CompanyGroupMember() { }
    public CompanyGroupMember(Guid groupId, Guid companyId) { GroupId = groupId; CompanyId = companyId; }
    public Guid GroupId { get; private set; }
    public Guid CompanyId { get; private set; }
}

public enum SagaIntentState { Pending, InProgress, Completed, Compensated, TimedOut }
public enum SagaLegState { Pending, Dispatched, Acknowledged, Failed, Compensated, TimedOut }

public sealed class SagaIntent
{
    private SagaIntent() { }
    private SagaIntent(Guid tenantId, string type, string idempotencyKey, DateTimeOffset createdAt)
    { Id = Guid.NewGuid(); TenantId = tenantId; Type = type.Trim(); IdempotencyKey = idempotencyKey.Trim(); CreatedAt = createdAt; }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Type { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string Payload { get; private set; } = "{}";
    public SagaIntentState State { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }
    public string? Owner { get; private set; }
    public List<SagaLeg> Legs { get; private set; } = [];
    public static SagaIntent Create(Guid tenantId, string type, string idempotencyKey, DateTimeOffset createdAt, string payload = "{}")
    {
        if (string.IsNullOrWhiteSpace(type)) throw new ArgumentException("A type is required.", nameof(type));
        if (string.IsNullOrWhiteSpace(idempotencyKey)) throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));
        var x = new SagaIntent(tenantId, type, idempotencyKey, createdAt); x.Payload = payload; return x;
    }
}

public sealed class SagaLeg
{
    private SagaLeg() { }
    public SagaLeg(Guid intentId, Guid companyId, Guid legId) { IntentId = intentId; CompanyId = companyId; LegId = legId; State = SagaLegState.Pending; }
    public Guid IntentId { get; private set; }
    public Guid LegId { get; private set; }
    public Guid CompanyId { get; private set; }
    public SagaLegState State { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset? AcknowledgedAt { get; private set; }
    public DateTimeOffset? TimedOutAt { get; private set; }
    public string? LastError { get; private set; }
    public void MarkDispatched() { State = SagaLegState.Dispatched; Attempts++; }
    public void Acknowledge(DateTimeOffset acknowledgedAt) { State = SagaLegState.Acknowledged; AcknowledgedAt = acknowledgedAt; }
    public void Fail(string error) { State = SagaLegState.Failed; LastError = error; }
}

public sealed class RegistryOutboxMessage
{
    private RegistryOutboxMessage() { }
    public RegistryOutboxMessage(Guid tenantId, string type, string payload, DateTimeOffset createdAt) { Id = Guid.NewGuid(); TenantId = tenantId; Type = type; Payload = payload; CreatedAt = createdAt; }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Type { get; private set; } = string.Empty;
    public string Payload { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DispatchedAt { get; private set; }
    public int Attempts { get; private set; }
    public void MarkAttempt() => Attempts++;
    public void MarkDispatched(DateTimeOffset dispatchedAt) => DispatchedAt = dispatchedAt;
}
