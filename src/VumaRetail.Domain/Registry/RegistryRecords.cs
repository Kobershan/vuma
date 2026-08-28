using VumaRetail.Domain.Primitives;
using System.Text.Json;
using System.Text.Json.Nodes;

// Registry records are persistence aggregates; their public surface is intentionally simple.
#pragma warning disable CS1591
#pragma warning disable IDE0011
namespace VumaRetail.Domain.Registry;

public sealed class CompanyGroup
{
    private CompanyGroup() { }
    private CompanyGroup(Guid tenantId, string name) { Id = UuidV7.NewGuid(); TenantId = tenantId; Name = name.Trim(); }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public List<CompanyGroupMember> Members { get; private set; } = [];
    public static CompanyGroup Create(Guid tenantId, string name)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("A tenant is required.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A name is required.", nameof(name));
        return new(tenantId, name);
    }
    public void AddMember(Guid companyId)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("A company is required.", nameof(companyId));
        if (Members.Any(x => x.CompanyId == companyId)) throw new InvalidOperationException("The company is already in this group.");
        Members.Add(new CompanyGroupMember(Id, companyId, TenantId));
    }
}

public sealed class CompanyGroupMember
{
    private CompanyGroupMember() { }
    public CompanyGroupMember(Guid groupId, Guid companyId, Guid tenantId = default) { GroupId = groupId; CompanyId = companyId; TenantId = tenantId; }
    public Guid TenantId { get; private set; }
    public Guid GroupId { get; private set; }
    public Guid CompanyId { get; private set; }
}

public enum SagaIntentState { Pending, InProgress, Completed, Compensated, TimedOut }
public enum SagaLegState { Pending, Dispatched, Acknowledged, Failed, Compensated, TimedOut }

public sealed class SagaIntent
{
    private SagaIntent() { }
    private SagaIntent(Guid tenantId, string type, string idempotencyKey, DateTimeOffset createdAt)
    { Id = UuidV7.NewGuid(); TenantId = tenantId; Type = type.Trim(); IdempotencyKey = idempotencyKey.Trim(); CreatedAt = createdAt; State = SagaIntentState.Pending; InitiatedBy = "system:legacy"; OperationStamp = HlcStamp.MinValue.ToString(); }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Type { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string Payload { get; private set; } = "{}";
    public SagaIntentState State { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }
    public string? Owner { get; private set; }
    public Guid? AuthorizedOperatorId { get; private set; }
    public string InitiatedBy { get; private set; } = string.Empty;
    public string OperationStamp { get; private set; } = string.Empty;
    public List<SagaLeg> Legs { get; private set; } = [];
    public static SagaIntent Create(Guid tenantId, string type, string idempotencyKey, DateTimeOffset createdAt, string payload = "{}")
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("A tenant is required.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(type)) throw new ArgumentException("A type is required.", nameof(type));
        if (string.IsNullOrWhiteSpace(idempotencyKey)) throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));
        if (string.IsNullOrWhiteSpace(payload)) throw new ArgumentException("A payload is required.", nameof(payload));
        var x = new SagaIntent(tenantId, type, idempotencyKey, createdAt); x.Payload = RegistryPayload.Sanitise(payload); return x;
    }
    public void Authorize(Guid operatorId, string initiatedBy, string operationStamp)
    {
        if (operatorId == Guid.Empty) throw new ArgumentException("An operator is required.", nameof(operatorId));
        AuthorizedOperatorId = operatorId;
        InitiatedBy = Require(initiatedBy, nameof(initiatedBy));
        OperationStamp = ParseStamp(operationStamp, nameof(operationStamp));
    }
    public void AddLeg(Guid companyId, Guid? legId = null)
    {
        RequireState(SagaIntentState.Pending);
        if (companyId == Guid.Empty) throw new ArgumentException("A company is required.", nameof(companyId));
        Guid id = legId ?? UuidV7.NewGuid();
        if (id == Guid.Empty) throw new ArgumentException("A leg is required.", nameof(legId));
        if (Legs.Any(x => x.LegId == id)) throw new InvalidOperationException("The leg is already registered.");
        Legs.Add(new SagaLeg(Id, companyId, id, TenantId));
    }
    public void Start(string owner)
    {
        RequireState(SagaIntentState.Pending);
        if (AuthorizedOperatorId is null) throw new InvalidOperationException("The saga intent must be authorized.");
        if (Legs.Count == 0) throw new InvalidOperationException("A saga intent must have at least one leg.");
        Owner = Require(owner, nameof(owner));
        State = SagaIntentState.InProgress;
    }
    public void Complete()
    {
        RequireState(SagaIntentState.InProgress);
        if (Legs.Any(x => x.State != SagaLegState.Acknowledged)) throw new InvalidOperationException("All saga legs must be acknowledged.");
        State = SagaIntentState.Completed;
    }
    public void Compensate() { if (State is not (SagaIntentState.InProgress or SagaIntentState.TimedOut)) throw new InvalidOperationException("Only an in-flight or timed-out intent can be compensated."); foreach (var leg in Legs.Where(x => x.State is not (SagaLegState.Acknowledged or SagaLegState.Compensated))) leg.Compensate(); State = SagaIntentState.Compensated; }
    public void Timeout(DateTimeOffset expiresAt) { if (State is not SagaIntentState.InProgress) throw new InvalidOperationException("Only an in-flight intent can time out."); ExpiresAt = expiresAt; State = SagaIntentState.TimedOut; }
    private void RequireState(SagaIntentState expected) { if (State != expected) throw new InvalidOperationException($"Saga intent must be {expected}."); }
    private static string Require(string value, string parameterName) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A value is required.", parameterName) : value.Trim();
    private static string ParseStamp(string value, string parameterName) { string stamp = Require(value, parameterName); try { HlcStamp.Parse(stamp); return stamp; } catch (MalformedHlcStampException) { throw new ArgumentException("A valid HLC stamp is required.", parameterName); } }
}

public sealed class SagaLeg
{
    private SagaLeg() { }
    public SagaLeg(Guid intentId, Guid companyId, Guid legId, Guid tenantId = default) { IntentId = intentId; CompanyId = companyId; TenantId = tenantId; LegId = legId; State = SagaLegState.Pending; OperationStamp = HlcStamp.MinValue.ToString(); }
    public Guid IntentId { get; private set; }
    public Guid LegId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid CompanyId { get; private set; }
    public SagaLegState State { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset? AcknowledgedAt { get; private set; }
    public DateTimeOffset? TimedOutAt { get; private set; }
    public string? LastError { get; private set; }
    public DateTimeOffset? LastAttemptAt { get; private set; }
    public string OperationStamp { get; private set; } = string.Empty;
    public void MarkDispatched(DateTimeOffset? attemptedAt = null, string? operationStamp = null) { if (State is SagaLegState.Acknowledged or SagaLegState.Compensated) throw new InvalidOperationException("A completed leg cannot be dispatched."); State = SagaLegState.Dispatched; Attempts++; LastAttemptAt = attemptedAt; if (operationStamp is not null) OperationStamp = ParseStamp(operationStamp); }
    public void Acknowledge(DateTimeOffset acknowledgedAt) { if (State == SagaLegState.Acknowledged) return; if (State is SagaLegState.Pending or SagaLegState.Compensated or SagaLegState.TimedOut) throw new InvalidOperationException("This leg cannot be acknowledged."); State = SagaLegState.Acknowledged; AcknowledgedAt = acknowledgedAt; LastError = null; }
    public void Fail(string error) { if (State is SagaLegState.Acknowledged or SagaLegState.Compensated) throw new InvalidOperationException("A completed leg cannot fail."); State = SagaLegState.Failed; LastError = string.IsNullOrWhiteSpace(error) ? throw new ArgumentException("An error is required.", nameof(error)) : error.Trim(); }
    public void Compensate()
    {
        // A pending leg has not reached a company, so compensation is a durable no-op that
        // prevents a timed-out intent from being redriven after the coordinator closes it.
        if (State is SagaLegState.Pending or SagaLegState.Dispatched or SagaLegState.Failed or SagaLegState.TimedOut)
            State = SagaLegState.Compensated;
        else
            throw new InvalidOperationException("A completed leg cannot be compensated.");
    }
    public void Timeout(DateTimeOffset timedOutAt) { if (State is SagaLegState.Acknowledged or SagaLegState.Compensated) throw new InvalidOperationException("A completed leg cannot time out."); State = SagaLegState.TimedOut; TimedOutAt = timedOutAt; }
    private static string ParseStamp(string value) { try { HlcStamp.Parse(value); return value; } catch (MalformedHlcStampException) { throw new ArgumentException("A valid HLC stamp is required.", nameof(value)); } }
}

public sealed class RegistryOutboxMessage
{
    private RegistryOutboxMessage() { }
    public RegistryOutboxMessage(Guid tenantId, string type, string payload, DateTimeOffset createdAt, string? idempotencyKey = null, string? operationStamp = null)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("A tenant is required.", nameof(tenantId));
        Type = Require(type, nameof(type));
        Payload = Require(payload, nameof(payload));
        Id = UuidV7.NewGuid(); TenantId = tenantId; IdempotencyKey = idempotencyKey is null ? Id.ToString() : Require(idempotencyKey, nameof(idempotencyKey)); CreatedAt = createdAt; Payload = RegistryPayload.Sanitise(payload); OperationStamp = operationStamp is null ? HlcStamp.MinValue.ToString() : ParseStamp(operationStamp);
    }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Type { get; private set; } = string.Empty;
    public string Payload { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DispatchedAt { get; private set; }
    public int Attempts { get; private set; }
    public string OperationStamp { get; private set; } = string.Empty;
    public void MarkAttempt() => Attempts++;
    public void MarkDispatched(DateTimeOffset dispatchedAt) => DispatchedAt = dispatchedAt;
    private static string Require(string value, string parameterName) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A value is required.", parameterName) : value.Trim();
    private static string ParseStamp(string value) { try { HlcStamp.Parse(value); return value; } catch (MalformedHlcStampException) { throw new ArgumentException("A valid HLC stamp is required.", nameof(value)); } }
}

internal static class RegistryPayload
{
    private static readonly HashSet<string> SensitiveNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "accessToken", "apiKey", "authorization", "clientSecret", "connectionString", "password", "refreshToken", "secret", "token"
    };

    public static string Sanitise(string payload)
    {
        try
        {
            JsonNode? node = JsonNode.Parse(payload.Trim()) ?? throw new JsonException("A JSON value is required.");
            Redact(node);
            return node.ToJsonString();
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Payload must be valid JSON and secrets are redacted.", nameof(payload), exception);
        }
    }

    private static void Redact(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            foreach (KeyValuePair<string, JsonNode?> property in obj.ToList())
            {
                if (SensitiveNames.Contains(property.Key))
                    obj[property.Key] = "[REDACTED]";
                else if (property.Value is not null)
                    Redact(property.Value);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (JsonNode? child in array)
                if (child is not null) Redact(child);
        }
    }
}
