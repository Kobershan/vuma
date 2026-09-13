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
    private ServiceTicket(Guid tenantId, Guid? storeId, Guid companyId, Guid operationId, Guid customerId, string subject,
        DateTimeOffset openedAt) : base(tenantId, storeId)
    {
        AssignCompany(companyId); OperationId = operationId;
        CustomerId = customerId;
        Subject = subject.Trim();
        OpenedAtUtc = openedAt;
        Status = ServiceTicketStatus.Open;
    }

    private ServiceTicket() { }

    public Guid CustomerId { get; private set; }
    public Guid OperationId { get; private set; }
    public string Subject { get; private set; } = string.Empty;
    public ServiceTicketStatus Status { get; private set; }
    public DateTimeOffset OpenedAtUtc { get; private set; }
    public DateTimeOffset? ClosedAtUtc { get; private set; }

    public static ServiceTicket Open(Guid tenantId, Guid? storeId, Guid companyId, Guid operationId, Guid customerId,
        string subject, DateTimeOffset openedAt)
    {
        if (tenantId == Guid.Empty || companyId == Guid.Empty || operationId == Guid.Empty || customerId == Guid.Empty)
            throw new ArgumentException("A service ticket requires tenant, company and customer identities.");
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        if (subject.Trim().Length > 256) throw new ArgumentException("A service subject must be 256 characters or fewer.", nameof(subject));
        return new ServiceTicket(tenantId, storeId, companyId, operationId, customerId, subject, openedAt);
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

/// <summary>Lifecycle of a repair performed under a service ticket.</summary>
public enum RepairJobStatus { Planned, InProgress, Completed, Cancelled }

/// <summary>A repair job for customer-owned goods; it does not represent saleable stock.</summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class RepairJob : Entity
{
    private RepairJob(Guid tenantId, Guid? storeId, Guid companyId, Guid ticketId, string itemReference,
        DateTimeOffset openedAt) : base(tenantId, storeId)
    {
        AssignCompany(companyId); TicketId = ticketId; ItemReference = itemReference.Trim();
        OpenedAtUtc = openedAt; Status = RepairJobStatus.Planned;
    }

    private RepairJob() { }
    public Guid TicketId { get; private set; }
    public string ItemReference { get; private set; } = string.Empty;
    public RepairJobStatus Status { get; private set; }
    public DateTimeOffset OpenedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public static RepairJob Open(Guid tenantId, Guid? storeId, Guid companyId, Guid ticketId,
        string itemReference, DateTimeOffset openedAt)
    {
        if (tenantId == Guid.Empty || companyId == Guid.Empty || ticketId == Guid.Empty)
            throw new ArgumentException("A repair job requires tenant, company and ticket identities.");
        ArgumentException.ThrowIfNullOrWhiteSpace(itemReference);
        return new RepairJob(tenantId, storeId, companyId, ticketId, itemReference, openedAt);
    }

    public void Start()
    {
        if (Status != RepairJobStatus.Planned) throw new InvalidOperationException("Only a planned repair can start.");
        Status = RepairJobStatus.InProgress;
    }

    public void Complete(DateTimeOffset at)
    {
        if (Status != RepairJobStatus.InProgress) throw new InvalidOperationException("Only an in-progress repair can complete.");
        Status = RepairJobStatus.Completed; CompletedAtUtc = at;
    }
}

/// <summary>One idempotent consumption of a service part against a repair job.</summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.AppendOnly)]
public sealed class ServicePartUsage : Entity
{
    private ServicePartUsage(Guid tenantId, Guid? storeId, Guid companyId, Guid repairJobId, Guid operationId,
        Guid? itemId, Guid? itemVariantId, decimal quantity, decimal unitCost, string currency,
        DateTimeOffset issuedAt) : base(tenantId, storeId)
    {
        AssignCompany(companyId); RepairJobId = repairJobId; OperationId = operationId;
        ItemId = itemId; ItemVariantId = itemVariantId; Quantity = quantity; UnitCost = unitCost;
        Currency = currency.Trim(); IssuedAtUtc = issuedAt;
    }

    private ServicePartUsage() { }
    public Guid RepairJobId { get; private set; }
    public Guid OperationId { get; private set; }
    public Guid? ItemId { get; private set; }
    public Guid? ItemVariantId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal UnitCost { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public DateTimeOffset IssuedAtUtc { get; private set; }

    public static ServicePartUsage Issue(Guid tenantId, Guid? storeId, Guid companyId, Guid repairJobId,
        Guid operationId, Guid? itemId, Guid? itemVariantId, decimal quantity, decimal unitCost,
        string currency, DateTimeOffset issuedAt)
    {
        if (tenantId == Guid.Empty || companyId == Guid.Empty || repairJobId == Guid.Empty || operationId == Guid.Empty)
            throw new ArgumentException("A part usage requires tenant, company, repair and operation identities.");
        if ((itemId is null) == (itemVariantId is null)) throw new ArgumentException("A part must identify an item or variant.");
        if (quantity <= 0m || unitCost < 0m) throw new ArgumentOutOfRangeException(nameof(quantity));
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        return new ServicePartUsage(tenantId, storeId, companyId, repairJobId, operationId, itemId,
            itemVariantId, quantity, unitCost, currency, issuedAt);
    }

    public decimal TotalCost => Quantity * UnitCost;
}

/// <summary>Configured service SLA duration, expressed in working hours.</summary>
[Replicated(ReplicationScope.CloudToStore, ConflictPolicy.CloudWins)]
public sealed class ServiceSla : Entity
{
    private ServiceSla() { }
    private ServiceSla(Guid tenantId, Guid companyId, string name, decimal responseHours, decimal resolutionHours)
        : base(tenantId)
    { AssignCompany(companyId); Name = name.Trim(); ResponseHours = responseHours; ResolutionHours = resolutionHours; }
    public string Name { get; private set; } = string.Empty;
    public decimal ResponseHours { get; private set; }
    public decimal ResolutionHours { get; private set; }
    public static ServiceSla Create(Guid tenantId, Guid companyId, string name, decimal responseHours, decimal resolutionHours)
    {
        if (tenantId == Guid.Empty || companyId == Guid.Empty) throw new ArgumentException("Tenant and company are required.");
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (responseHours <= 0m || resolutionHours <= 0m) throw new ArgumentOutOfRangeException(nameof(responseHours));
        return new ServiceSla(tenantId, companyId, name, responseHours, resolutionHours);
    }
}
