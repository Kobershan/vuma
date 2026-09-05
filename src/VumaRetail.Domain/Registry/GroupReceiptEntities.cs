using VumaRetail.Domain.Primitives;

#pragma warning disable CS1591
#pragma warning disable IDE0011
namespace VumaRetail.Domain.Registry;

// ========== Stage 07c: Group Receipts & Payments ==========

public enum GroupReceiptStatus { Draft, PartiallyAllocated, Allocated, Reversed }
public enum GroupReceiptAllocationLegState { Pending, Applied, Compensated }
public enum GroupPaymentRunStatus { Draft, PartiallyAllocated, Allocated, Reversed }
public enum GroupPaymentAllocationLegState { Pending, Applied, Compensated }
public enum InterCompanyClearingIntentState { Pending, Settled, PartiallySettled, Compensated }
public enum InterCompanyClearingLegState { Pending, Acknowledged, Failed, Compensated }

/// <summary>
/// A single payment captured once and allocated across several companies' databases.
/// Lives in the registry, posts nothing (ADR-104). Immutable once fully allocated;
/// a correction is a reversal (§7 rule 7).
/// </summary>
public sealed class GroupReceipt
{
    private readonly List<GroupReceiptAllocation> _allocations = [];

    private GroupReceipt() { }

    private GroupReceipt(Guid tenantId, Guid capturingCompanyId, Guid bankAccountId,
        Money amount, string tenderType, string reference, DateTimeOffset capturedAt)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("A tenant is required.", nameof(tenantId));
        if (capturingCompanyId == Guid.Empty) throw new ArgumentException("A capturing company is required.", nameof(capturingCompanyId));
        if (bankAccountId == Guid.Empty) throw new ArgumentException("A bank account is required.", nameof(bankAccountId));
        Id = UuidV7.NewGuid();
        TenantId = tenantId;
        CapturingCompanyId = capturingCompanyId;
        BankAccountId = bankAccountId;
        Amount = amount;
        TenderType = tenderType.Trim();
        Reference = reference.Trim();
        CapturedAt = capturedAt;
        Status = GroupReceiptStatus.Draft;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid CapturingCompanyId { get; private set; }
    public Guid BankAccountId { get; private set; }
    public Money Amount { get; private set; }
    public string TenderType { get; private set; } = string.Empty;
    public string Reference { get; private set; } = string.Empty;
    public DateTimeOffset CapturedAt { get; private set; }
    public GroupReceiptStatus Status { get; private set; }
    public IReadOnlyList<GroupReceiptAllocation> Allocations => _allocations;

    /// <summary>
    /// Creates a group receipt. Posts nothing. The capturing company owns the receiving bank account.
    /// </summary>
    public static GroupReceipt Capture(
        Guid tenantId, Guid capturingCompanyId, Guid bankAccountId,
        Money amount, string tenderType, string reference, DateTimeOffset capturedAt)
    {
        if (amount.Amount <= 0) throw new ArgumentException("Amount must be positive.", nameof(amount));
        ArgumentException.ThrowIfNullOrWhiteSpace(tenderType);
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        return new GroupReceipt(tenantId, capturingCompanyId, bankAccountId, amount, tenderType, reference, capturedAt);
    }

    /// <summary>
    /// Allocates part of the receipt to one company. Σ allocations ≤ captured amount.
    /// </summary>
    public GroupReceiptAllocation Allocate(
        Guid companyId, Guid? customerPartnerId, Money amount,
        IReadOnlyList<Guid>? targetInvoiceIds = null)
    {
        if (Status is GroupReceiptStatus.Reversed or GroupReceiptStatus.Allocated)
            throw new InvalidOperationException("Cannot allocate to a reversed or fully allocated receipt.");
        if (companyId == Guid.Empty) throw new ArgumentException("A company is required.", nameof(companyId));
        if (amount.Amount <= 0) throw new ArgumentException("Allocation amount must be positive.", nameof(amount));
        if (amount.Currency != Amount.Currency)
            throw new ArgumentException($"Allocation currency {amount.Currency} differs from receipt currency {Amount.Currency}.");

        Money totalAllocated = _allocations
            .Where(a => a.LegState is not GroupReceiptAllocationLegState.Compensated)
            .Aggregate(Money.Zero(Amount.Currency), (sum, a) => sum + a.Amount);

        if (totalAllocated + amount > Amount)
            throw new InvalidOperationException(
                $"Allocations total {totalAllocated + amount} would exceed receipt amount {Amount}.");

        GroupReceiptAllocation allocation = new()
        {
            Id = UuidV7.NewGuid(),
            GroupReceiptId = Id,
            TenantId = TenantId,
            CompanyId = companyId,
            CustomerPartnerId = customerPartnerId,
            Amount = amount,
            LegState = GroupReceiptAllocationLegState.Pending,
            TargetInvoiceIds = targetInvoiceIds?.ToList() ?? [],
        };

        _allocations.Add(allocation);
        UpdateStatus();
        return allocation;
    }

    /// <summary>
    /// Marks a specific allocation as applied (company acknowledged).
    /// </summary>
    public void AcknowledgeAllocation(Guid allocationId)
    {
        GroupReceiptAllocation allocation = _allocations.FirstOrDefault(a => a.Id == allocationId)
            ?? throw new InvalidOperationException($"Allocation {allocationId} not found.");
        allocation.MarkApplied();
        UpdateStatus();
    }

    /// <summary>
    /// Reverses the entire receipt. Dispatches reversing legs to every company involved.
    /// Never edits a posted journal (§7 rule 6).
    /// </summary>
    public void Reverse()
    {
        if (Status == GroupReceiptStatus.Reversed)
            throw new InvalidOperationException("Receipt is already reversed.");
        foreach (GroupReceiptAllocation allocation in _allocations
            .Where(a => a.LegState is GroupReceiptAllocationLegState.Pending or GroupReceiptAllocationLegState.Applied))
        {
            allocation.Compensate();
        }
        Status = GroupReceiptStatus.Reversed;
    }

    private void UpdateStatus()
    {
        var active = _allocations.Where(a => a.LegState is not GroupReceiptAllocationLegState.Compensated).ToList();
        Money totalAllocated = active.Aggregate(Money.Zero(Amount.Currency), (sum, a) => sum + a.Amount);

        Status = totalAllocated >= Amount
            ? GroupReceiptStatus.Allocated
            : totalAllocated.Amount > 0
                ? GroupReceiptStatus.PartiallyAllocated
                : GroupReceiptStatus.Draft;
    }
}

/// <summary>
/// One allocation slice of a <see cref="GroupReceipt"/>, targeting one company's database.
/// Each allocation is dispatched through ISagaCoordinator as an idempotent leg keyed by
/// (group_receipt_id, allocation_id) (ADR-104, ADR-116).
/// </summary>
public sealed class GroupReceiptAllocation
{
    private GroupReceiptAllocation() { }

    public Guid Id { get; set; }
    public Guid GroupReceiptId { get; set; }
    public Guid TenantId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid? CustomerPartnerId { get; set; }
    public Money Amount { get; set; }
    public GroupReceiptAllocationLegState LegState { get; set; }
    public List<Guid> TargetInvoiceIds { get; set; } = [];
    public DateTimeOffset? AppliedAt { get; set; }
    public DateTimeOffset? CompensatedAt { get; set; }
    public string? ErrorMessage { get; set; }

    internal void MarkApplied()
    {
        if (LegState is not GroupReceiptAllocationLegState.Pending)
            throw new InvalidOperationException("Only pending allocations can be applied.");
        LegState = GroupReceiptAllocationLegState.Applied;
        AppliedAt = DateTimeOffset.UtcNow;
    }

    internal void Compensate()
    {
        if (LegState is GroupReceiptAllocationLegState.Compensated) return;
        if (LegState is not GroupReceiptAllocationLegState.Pending and not GroupReceiptAllocationLegState.Applied)
            throw new InvalidOperationException("Only pending or applied allocations can be compensated.");
        LegState = GroupReceiptAllocationLegState.Compensated;
        CompensatedAt = DateTimeOffset.UtcNow;
    }
}

// ========== Stage 07c: Group Payment Runs ==========

/// <summary>
/// A supplier payment captured once and allocated across several companies that owe the supplier.
/// Same shape as <see cref="GroupReceipt"/> for the outbound direction (ADR-104).
/// </summary>
public sealed class GroupPaymentRun
{
    private readonly List<GroupPaymentAllocation> _allocations = [];

    private GroupPaymentRun() { }

    private GroupPaymentRun(Guid tenantId, Guid capturingCompanyId, Guid bankAccountId,
        Money amount, string tenderType, string reference, DateTimeOffset paidAt)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("A tenant is required.", nameof(tenantId));
        if (capturingCompanyId == Guid.Empty) throw new ArgumentException("A capturing company is required.", nameof(capturingCompanyId));
        if (bankAccountId == Guid.Empty) throw new ArgumentException("A bank account is required.", nameof(bankAccountId));
        Id = UuidV7.NewGuid();
        TenantId = tenantId;
        CapturingCompanyId = capturingCompanyId;
        BankAccountId = bankAccountId;
        Amount = amount;
        TenderType = tenderType.Trim();
        Reference = reference.Trim();
        PaidAt = paidAt;
        Status = GroupPaymentRunStatus.Draft;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid CapturingCompanyId { get; private set; }
    public Guid BankAccountId { get; private set; }
    public Money Amount { get; private set; }
    public string TenderType { get; private set; } = string.Empty;
    public string Reference { get; private set; } = string.Empty;
    public DateTimeOffset PaidAt { get; private set; }
    public GroupPaymentRunStatus Status { get; private set; }
    public IReadOnlyList<GroupPaymentAllocation> Allocations => _allocations;

    public static GroupPaymentRun Capture(
        Guid tenantId, Guid capturingCompanyId, Guid bankAccountId,
        Money amount, string tenderType, string reference, DateTimeOffset paidAt)
    {
        if (amount.Amount <= 0) throw new ArgumentException("Amount must be positive.", nameof(amount));
        ArgumentException.ThrowIfNullOrWhiteSpace(tenderType);
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        return new GroupPaymentRun(tenantId, capturingCompanyId, bankAccountId, amount, tenderType, reference, paidAt);
    }

    public GroupPaymentAllocation Allocate(
        Guid companyId, Guid? supplierPartnerId, Money amount,
        IReadOnlyList<Guid>? targetInvoiceIds = null)
    {
        if (Status is GroupPaymentRunStatus.Reversed or GroupPaymentRunStatus.Allocated)
            throw new InvalidOperationException("Cannot allocate to a reversed or fully allocated payment run.");
        if (companyId == Guid.Empty) throw new ArgumentException("A company is required.", nameof(companyId));
        if (amount.Amount <= 0) throw new ArgumentException("Allocation amount must be positive.", nameof(amount));
        if (amount.Currency != Amount.Currency)
            throw new ArgumentException($"Allocation currency {amount.Currency} differs from payment currency {Amount.Currency}.");

        Money totalAllocated = _allocations
            .Where(a => a.LegState is not GroupPaymentAllocationLegState.Compensated)
            .Aggregate(Money.Zero(Amount.Currency), (sum, a) => sum + a.Amount);

        if (totalAllocated + amount > Amount)
            throw new InvalidOperationException(
                $"Allocations total {totalAllocated + amount} would exceed payment amount {Amount}.");

        GroupPaymentAllocation allocation = new()
        {
            Id = UuidV7.NewGuid(),
            GroupPaymentRunId = Id,
            TenantId = TenantId,
            CompanyId = companyId,
            SupplierPartnerId = supplierPartnerId,
            Amount = amount,
            LegState = GroupPaymentAllocationLegState.Pending,
            TargetInvoiceIds = targetInvoiceIds?.ToList() ?? [],
        };

        _allocations.Add(allocation);
        UpdateStatus();
        return allocation;
    }

    public void AcknowledgeAllocation(Guid allocationId)
    {
        GroupPaymentAllocation allocation = _allocations.FirstOrDefault(a => a.Id == allocationId)
            ?? throw new InvalidOperationException($"Allocation {allocationId} not found.");
        allocation.MarkApplied();
        UpdateStatus();
    }

    public void Reverse()
    {
        if (Status == GroupPaymentRunStatus.Reversed)
            throw new InvalidOperationException("Payment run is already reversed.");
        foreach (GroupPaymentAllocation allocation in _allocations
            .Where(a => a.LegState is GroupPaymentAllocationLegState.Pending or GroupPaymentAllocationLegState.Applied))
        {
            allocation.Compensate();
        }
        Status = GroupPaymentRunStatus.Reversed;
    }

    private void UpdateStatus()
    {
        var active = _allocations.Where(a => a.LegState is not GroupPaymentAllocationLegState.Compensated).ToList();
        Money totalAllocated = active.Aggregate(Money.Zero(Amount.Currency), (sum, a) => sum + a.Amount);

        Status = totalAllocated >= Amount
            ? GroupPaymentRunStatus.Allocated
            : totalAllocated.Amount > 0
                ? GroupPaymentRunStatus.PartiallyAllocated
                : GroupPaymentRunStatus.Draft;
    }
}

/// <summary>One allocation slice of a <see cref="GroupPaymentRun"/>, targeting one company's AP.</summary>
public sealed class GroupPaymentAllocation
{
    private GroupPaymentAllocation() { }

    public Guid Id { get; set; }
    public Guid GroupPaymentRunId { get; set; }
    public Guid TenantId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid? SupplierPartnerId { get; set; }
    public Money Amount { get; set; }
    public GroupPaymentAllocationLegState LegState { get; set; }
    public List<Guid> TargetInvoiceIds { get; set; } = [];
    public DateTimeOffset? AppliedAt { get; set; }
    public DateTimeOffset? CompensatedAt { get; set; }
    public string? ErrorMessage { get; set; }

    internal void MarkApplied()
    {
        if (LegState is not GroupPaymentAllocationLegState.Pending)
            throw new InvalidOperationException("Only pending allocations can be applied.");
        LegState = GroupPaymentAllocationLegState.Applied;
        AppliedAt = DateTimeOffset.UtcNow;
    }

    internal void Compensate()
    {
        if (LegState is GroupPaymentAllocationLegState.Compensated) return;
        if (LegState is not GroupPaymentAllocationLegState.Pending and not GroupPaymentAllocationLegState.Applied)
            throw new InvalidOperationException("Only pending or applied allocations can be compensated.");
        LegState = GroupPaymentAllocationLegState.Compensated;
        CompensatedAt = DateTimeOffset.UtcNow;
    }
}

// ========== Stage 07c: Inter-Company Clearing ==========

/// <summary>
/// An immutable clearing intent carrying both legs and the group document that caused them.
/// Settled only when both companies acknowledge (ADR-105). Clearing balances net to zero
/// across the group at every instant.
/// </summary>
public sealed class InterCompanyClearingIntent
{
    private readonly List<InterCompanyClearingLeg> _legs = [];

    private InterCompanyClearingIntent() { }

    private InterCompanyClearingIntent(
        Guid tenantId, Guid groupDocumentId, string groupDocumentType,
        Guid fromCompanyId, Guid toCompanyId, Money amount, string currency)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("A tenant is required.", nameof(tenantId));
        if (fromCompanyId == Guid.Empty) throw new ArgumentException("A source company is required.", nameof(fromCompanyId));
        if (toCompanyId == Guid.Empty) throw new ArgumentException("A target company is required.", nameof(toCompanyId));
        if (fromCompanyId == toCompanyId) throw new ArgumentException("Clearing between the same company is meaningless.");
        Id = UuidV7.NewGuid();
        TenantId = tenantId;
        GroupDocumentId = groupDocumentId;
        GroupDocumentType = groupDocumentType.Trim();
        FromCompanyId = fromCompanyId;
        ToCompanyId = toCompanyId;
        Amount = amount;
        Currency = currency.Trim();
        State = InterCompanyClearingIntentState.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid GroupDocumentId { get; private set; }
    public string GroupDocumentType { get; private set; } = string.Empty;
    public Guid FromCompanyId { get; private set; }
    public Guid ToCompanyId { get; private set; }
    public Money Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public InterCompanyClearingIntentState State { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? SettledAt { get; private set; }
    public IReadOnlyList<InterCompanyClearingLeg> Legs => _legs;

    /// <summary>
    /// Creates an immutable clearing intent with two legs (one per company).
    /// </summary>
    public static InterCompanyClearingIntent Create(
        Guid tenantId, Guid groupDocumentId, string groupDocumentType,
        Guid fromCompanyId, Guid toCompanyId, Money amount, string currency)
    {
        if (amount.Amount <= 0) throw new ArgumentException("Amount must be positive.", nameof(amount));
        var intent = new InterCompanyClearingIntent(tenantId, groupDocumentId, groupDocumentType, fromCompanyId, toCompanyId, amount, currency);

        // Leg 1: From company — debits inter-company clearing, credits bank (the receiving side)
        intent._legs.Add(new InterCompanyClearingLeg
        {
            Id = UuidV7.NewGuid(),
            IntentId = intent.Id,
            TenantId = tenantId,
            CompanyId = fromCompanyId,
            Direction = "Debit",
            Amount = amount,
            Currency = currency,
            State = InterCompanyClearingLegState.Pending,
        });

        // Leg 2: To company — debits clearing and credits its customer (the benefiting side)
        intent._legs.Add(new InterCompanyClearingLeg
        {
            Id = UuidV7.NewGuid(),
            IntentId = intent.Id,
            TenantId = tenantId,
            CompanyId = toCompanyId,
            Direction = "Credit",
            Amount = amount,
            Currency = currency,
            State = InterCompanyClearingLegState.Pending,
        });

        return intent;
    }

    /// <summary>
    /// Acknowledges one leg. The intent is settled when both legs acknowledge.
    /// </summary>
    public void AcknowledgeLeg(Guid legId)
    {
        InterCompanyClearingLeg leg = _legs.FirstOrDefault(l => l.Id == legId)
            ?? throw new InvalidOperationException($"Leg {legId} not found.");
        leg.Acknowledge();

        if (_legs.All(l => l.State == InterCompanyClearingLegState.Acknowledged))
        {
            State = InterCompanyClearingIntentState.Settled;
            SettledAt = DateTimeOffset.UtcNow;
        }
        else
        {
            State = InterCompanyClearingIntentState.PartiallySettled;
        }
    }

    /// <summary>Compensates all outstanding legs.</summary>
    public void Compensate()
    {
        if (State is InterCompanyClearingIntentState.Settled or InterCompanyClearingIntentState.Compensated)
            throw new InvalidOperationException("A settled or already-compensated intent cannot be compensated.");
        foreach (InterCompanyClearingLeg leg in _legs.Where(l =>
            l.State is InterCompanyClearingLegState.Pending or InterCompanyClearingLegState.Failed))
        {
            leg.Compensate();
        }
        State = InterCompanyClearingIntentState.Compensated;
    }
}

/// <summary>One leg of an <see cref="InterCompanyClearingIntent"/>, targeting one company's database.</summary>
public sealed class InterCompanyClearingLeg
{
    private InterCompanyClearingLeg() { }

    public Guid Id { get; set; }
    public Guid IntentId { get; set; }
    public Guid TenantId { get; set; }
    public Guid CompanyId { get; set; }
    public string Direction { get; set; } = string.Empty;
    public Money Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public InterCompanyClearingLegState State { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public DateTimeOffset? CompensatedAt { get; set; }
    public string? ErrorMessage { get; set; }

    internal void Acknowledge()
    {
        if (State is InterCompanyClearingLegState.Acknowledged) return;
        if (State is not InterCompanyClearingLegState.Pending and not InterCompanyClearingLegState.Failed)
            throw new InvalidOperationException("Only pending or failed legs can be acknowledged.");
        State = InterCompanyClearingLegState.Acknowledged;
        AcknowledgedAt = DateTimeOffset.UtcNow;
        ErrorMessage = null;
    }

    internal void Compensate()
    {
        if (State is InterCompanyClearingLegState.Acknowledged or InterCompanyClearingLegState.Compensated) return;
        State = InterCompanyClearingLegState.Compensated;
        CompensatedAt = DateTimeOffset.UtcNow;
    }
}
