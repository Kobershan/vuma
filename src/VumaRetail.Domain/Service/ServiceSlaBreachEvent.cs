#pragma warning disable CS1591, IDE0011
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Service;

public enum ServiceSlaBreachType { Response, Resolution }

/// <summary>Append-only, idempotent observation that a service SLA deadline was exceeded.</summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.AppendOnly)]
public sealed class ServiceSlaBreachEvent : Entity
{
    private ServiceSlaBreachEvent(Guid tenantId, Guid? storeId, Guid companyId, Guid ticketId,
        string slaName, ServiceSlaBreachType breachType, DateTimeOffset dueAtUtc, DateTimeOffset observedAtUtc)
        : base(tenantId, storeId)
    {
        AssignCompany(companyId);
        TicketId = ticketId;
        SlaName = slaName.Trim();
        BreachType = breachType;
        DueAtUtc = dueAtUtc.ToUniversalTime();
        ObservedAtUtc = observedAtUtc.ToUniversalTime();
    }

    private ServiceSlaBreachEvent() { }

    public Guid TicketId { get; private set; }
    public string SlaName { get; private set; } = string.Empty;
    public ServiceSlaBreachType BreachType { get; private set; }
    public DateTimeOffset DueAtUtc { get; private set; }
    public DateTimeOffset ObservedAtUtc { get; private set; }

    public static ServiceSlaBreachEvent Record(Guid tenantId, Guid? storeId, Guid companyId, Guid ticketId,
        string slaName, ServiceSlaBreachType breachType, DateTimeOffset dueAtUtc, DateTimeOffset observedAtUtc)
    {
        if (tenantId == Guid.Empty || companyId == Guid.Empty || ticketId == Guid.Empty)
        {
            throw new ArgumentException("An SLA breach requires tenant, company and ticket identities.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(slaName);
        if (observedAtUtc.ToUniversalTime() < dueAtUtc.ToUniversalTime())
        {
            throw new ArgumentException("An SLA breach cannot be observed before its due time.");
        }
        return new ServiceSlaBreachEvent(tenantId, storeId, companyId, ticketId, slaName, breachType,
            dueAtUtc, observedAtUtc);
    }
}
