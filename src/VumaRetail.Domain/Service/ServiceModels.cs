#pragma warning disable CS1591, IDE0011
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Service;

/// <summary>Lifecycle of a customer service request.</summary>
public enum ServiceTicketStatus { Open, InProgress, WaitingForCustomer, Resolved, Closed }

/// <summary>A tenant/company-scoped request for service.</summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class ServiceTicket : Entity
{
    private ServiceTicket(Guid tenantId, Guid? storeId, Guid companyId, Guid customerId, string subject,
        DateTimeOffset openedAt) : base(tenantId, storeId)
    {
        AssignCompany(companyId);
        CustomerId = customerId;
        Subject = subject.Trim();
        OpenedAtUtc = openedAt;
        Status = ServiceTicketStatus.Open;
    }

    private ServiceTicket() { }

    public Guid CustomerId { get; private set; }
    public string Subject { get; private set; } = string.Empty;
    public ServiceTicketStatus Status { get; private set; }
    public DateTimeOffset OpenedAtUtc { get; private set; }
    public DateTimeOffset? ClosedAtUtc { get; private set; }

    public static ServiceTicket Open(Guid tenantId, Guid? storeId, Guid companyId, Guid customerId,
        string subject, DateTimeOffset openedAt)
    {
        if (tenantId == Guid.Empty || companyId == Guid.Empty || customerId == Guid.Empty)
            throw new ArgumentException("A service ticket requires tenant, company and customer identities.");
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        if (subject.Trim().Length > 256) throw new ArgumentException("A service subject must be 256 characters or fewer.", nameof(subject));
        return new ServiceTicket(tenantId, storeId, companyId, customerId, subject, openedAt);
    }

    public void Start(DateTimeOffset at) => Move(ServiceTicketStatus.InProgress, at);
    public void WaitForCustomer(DateTimeOffset at) => Move(ServiceTicketStatus.WaitingForCustomer, at);
    public void Resolve(DateTimeOffset at) => Move(ServiceTicketStatus.Resolved, at);

    public void Close(DateTimeOffset at)
    {
        if (Status is not (ServiceTicketStatus.Resolved or ServiceTicketStatus.WaitingForCustomer))
            throw new InvalidOperationException("Only a resolved or waiting service ticket can be closed.");
        Status = ServiceTicketStatus.Closed;
        ClosedAtUtc = at;
    }

    private void Move(ServiceTicketStatus target, DateTimeOffset at)
    {
        if (Status == ServiceTicketStatus.Closed)
            throw new InvalidOperationException("A closed service ticket cannot change state.");
        Status = target;
    }
}

/// <summary>Outcome of a warranty review against a frozen sale and serial snapshot.</summary>
public enum WarrantyClaimStatus { Pending, Approved, Declined }

/// <summary>Warranty claim whose entitlement facts are immutable after submission.</summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class WarrantyClaim : Entity
{
    private WarrantyClaim(Guid tenantId, Guid? storeId, Guid companyId, Guid ticketId, Guid customerId,
        string saleReference, DateOnly saleDate, string serialNumber, DateTimeOffset submittedAt) : base(tenantId, storeId)
    {
        AssignCompany(companyId);
        TicketId = ticketId; CustomerId = customerId; SaleReference = saleReference.Trim();
        SaleDate = saleDate; SerialNumber = serialNumber.Trim(); SubmittedAtUtc = submittedAt;
        Status = WarrantyClaimStatus.Pending;
    }

    private WarrantyClaim() { }

    public Guid TicketId { get; private set; }
    public Guid CustomerId { get; private set; }
    public string SaleReference { get; private set; } = string.Empty;
    public DateOnly SaleDate { get; private set; }
    public string SerialNumber { get; private set; } = string.Empty;
    public WarrantyClaimStatus Status { get; private set; }
    public DateTimeOffset SubmittedAtUtc { get; private set; }
    public DateTimeOffset? DecidedAtUtc { get; private set; }
    public string? DecisionReason { get; private set; }

    public static WarrantyClaim Submit(Guid tenantId, Guid? storeId, Guid companyId, Guid ticketId,
        Guid customerId, string saleReference, DateOnly saleDate, string serialNumber, DateTimeOffset submittedAt)
    {
        if (tenantId == Guid.Empty || companyId == Guid.Empty || ticketId == Guid.Empty || customerId == Guid.Empty)
            throw new ArgumentException("A warranty claim requires tenant, company, ticket and customer identities.");
        ArgumentException.ThrowIfNullOrWhiteSpace(saleReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(serialNumber);
        return new WarrantyClaim(tenantId, storeId, companyId, ticketId, customerId, saleReference, saleDate, serialNumber, submittedAt);
    }

    public void Approve(string soldSerialNumber, DateTimeOffset at)
    {
        EnsurePending();
        if (!string.Equals(SerialNumber, soldSerialNumber?.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The warranty serial does not match the original sale snapshot.");
        Status = WarrantyClaimStatus.Approved; DecidedAtUtc = at;
    }

    public void Decline(string reason, DateTimeOffset at)
    {
        EnsurePending(); ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Status = WarrantyClaimStatus.Declined; DecisionReason = reason.Trim(); DecidedAtUtc = at;
    }

    private void EnsurePending()
    {
        if (Status != WarrantyClaimStatus.Pending) throw new InvalidOperationException("Only a pending warranty claim can be decided.");
    }
}

/// <summary>Append-only custody transition for customer-owned goods.</summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.AppendOnly)]
public sealed class ServiceCustodyEvent : Entity
{
    private ServiceCustodyEvent(Guid tenantId, Guid? storeId, Guid companyId, Guid ticketId, Guid customerId,
        string eventType, string itemReference, DateTimeOffset occurredAt) : base(tenantId, storeId)
    {
        AssignCompany(companyId); TicketId = ticketId; CustomerId = customerId;
        EventType = eventType.Trim(); ItemReference = itemReference.Trim(); OccurredAtUtc = occurredAt;
    }

    private ServiceCustodyEvent() { }
    public Guid TicketId { get; private set; }
    public Guid CustomerId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public string ItemReference { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; private set; }

    public static ServiceCustodyEvent Record(Guid tenantId, Guid? storeId, Guid companyId, Guid ticketId,
        Guid customerId, string eventType, string itemReference, DateTimeOffset occurredAt)
    {
        if (tenantId == Guid.Empty || companyId == Guid.Empty || ticketId == Guid.Empty || customerId == Guid.Empty)
            throw new ArgumentException("A custody event requires tenant, company, ticket and customer identities.");
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemReference);
        return new ServiceCustodyEvent(tenantId, storeId, companyId, ticketId, customerId, eventType, itemReference, occurredAt);
    }
}
