#pragma warning disable CS1591, IDE0011
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Projects;

public enum ProjectStatus { Draft, Active, Closed }
public enum ProjectBudgetStatus { Draft, Submitted, Approved }
public enum ContractVariationStatus { Proposed, Approved, Rejected }
public enum BillingMilestoneStatus { Planned, Approved, Billed }
public enum ProjectCostKind { Labour, Procurement, Other, Reversal }

[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class Project : Entity
{
    private Project(Guid tenantId, Guid? storeId, Guid companyId, string code, string name, string currency) : base(tenantId, storeId)
    { AssignCompany(companyId); Code = code.Trim(); Name = name.Trim(); Currency = currency.Trim().ToUpperInvariant(); Status = ProjectStatus.Draft; }
    private Project() { }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Currency { get; private set; } = string.Empty;
    public ProjectStatus Status { get; private set; }
    public static Project Create(Guid tenantId, Guid? storeId, Guid companyId, string code, string name, string currency)
    {
        if (tenantId == Guid.Empty || companyId == Guid.Empty) throw new ArgumentException("Tenant and company are required.");
        ArgumentException.ThrowIfNullOrWhiteSpace(code); ArgumentException.ThrowIfNullOrWhiteSpace(name); ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        return new Project(tenantId, storeId, companyId, code, name, currency);
    }
    public void Activate() { if (Status != ProjectStatus.Draft) throw new InvalidOperationException("Only a draft project can be activated."); Status = ProjectStatus.Active; }
    public void Close() { if (Status != ProjectStatus.Active) throw new InvalidOperationException("Only an active project can be closed."); Status = ProjectStatus.Closed; }
}

[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class ProjectBudget : Entity
{
    private ProjectBudget(Guid tenantId, Guid? storeId, Guid companyId, Guid projectId, int version, Money amount) : base(tenantId, storeId)
    { AssignCompany(companyId); ProjectId = projectId; Version = version; Amount = amount; Status = ProjectBudgetStatus.Draft; }
    private ProjectBudget() { }
    public Guid ProjectId { get; private set; }
    public int Version { get; private set; }
    public Money Amount { get; private set; }
    public Money Committed { get; private set; }
    public Money Actual { get; private set; }
    public ProjectBudgetStatus Status { get; private set; }
    public static ProjectBudget Create(Guid tenantId, Guid? storeId, Guid companyId, Guid projectId, int version, Money amount)
    {
        if (projectId == Guid.Empty || version <= 0 || amount.Amount < 0m) throw new ArgumentException("Budget identity and amount are invalid.");
        ProjectBudget budget = new(tenantId, storeId, companyId, projectId, version, amount);
        budget.Committed = Money.Zero(amount.Currency); budget.Actual = Money.Zero(amount.Currency);
        return budget;
    }
    public void Submit() { if (Status != ProjectBudgetStatus.Draft) throw new InvalidOperationException("Only a draft budget can be submitted."); Status = ProjectBudgetStatus.Submitted; }
    public void Approve() { if (Status != ProjectBudgetStatus.Submitted) throw new InvalidOperationException("Only a submitted budget can be approved."); Status = ProjectBudgetStatus.Approved; }
    public void SetMeasures(Money committed, Money actual)
    {
        if (committed.Currency != Amount.Currency || actual.Currency != Amount.Currency) throw new InvalidOperationException("Budget measures must use the budget currency.");
        if (committed.Amount < 0m || actual.Amount < 0m) throw new ArgumentOutOfRangeException(nameof(committed));
        Committed = committed; Actual = actual;
    }
    public Money Available => new(Math.Max(0m, Amount.Amount - Committed.Amount - Actual.Amount), Amount.Currency);
}

[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.AppendOnly)]
public sealed class ProjectCostEntry : Entity
{
    private ProjectCostEntry(Guid tenantId, Guid? storeId, Guid companyId, Guid projectId, string sourceReference,
        ProjectCostKind kind, Money amount, Guid? reversesEntryId) : base(tenantId, storeId)
    { AssignCompany(companyId); ProjectId = projectId; SourceReference = sourceReference.Trim(); Kind = kind; Amount = amount; ReversesEntryId = reversesEntryId; }
    private ProjectCostEntry() { }
    public Guid ProjectId { get; private set; }
    public string SourceReference { get; private set; } = string.Empty;
    public ProjectCostKind Kind { get; private set; }
    public Money Amount { get; private set; }
    public Guid? ReversesEntryId { get; private set; }
    public static ProjectCostEntry Record(Guid tenantId, Guid? storeId, Guid companyId, Guid projectId,
        string sourceReference, ProjectCostKind kind, Money amount)
    { if (tenantId == Guid.Empty || companyId == Guid.Empty || projectId == Guid.Empty) throw new ArgumentException("Cost identity is required."); ArgumentException.ThrowIfNullOrWhiteSpace(sourceReference); if (amount.IsNegative) throw new ArgumentOutOfRangeException(nameof(amount)); return new(tenantId, storeId, companyId, projectId, sourceReference, kind, amount, null); }
    public static ProjectCostEntry Reverse(ProjectCostEntry original, string sourceReference)
    { ArgumentNullException.ThrowIfNull(original); ArgumentException.ThrowIfNullOrWhiteSpace(sourceReference); return new(original.TenantId, original.StoreId, original.CompanyId!.Value, original.ProjectId, sourceReference, ProjectCostKind.Reversal, -original.Amount, original.Id); }
}

[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class ProjectContract : Entity
{
    private ProjectContract(Guid tenantId, Guid? storeId, Guid companyId, Guid projectId, string number, Money originalValue) : base(tenantId, storeId)
    { AssignCompany(companyId); ProjectId = projectId; Number = number.Trim(); OriginalValue = originalValue; }
    private ProjectContract() { }
    public Guid ProjectId { get; private set; }
    public string Number { get; private set; } = string.Empty;
    public Money OriginalValue { get; private set; }
    public static ProjectContract Create(Guid tenantId, Guid? storeId, Guid companyId, Guid projectId, string number, Money originalValue)
    {
        if (projectId == Guid.Empty || originalValue.Amount < 0m) throw new ArgumentException("Contract identity and value are invalid.");
        ArgumentException.ThrowIfNullOrWhiteSpace(number);
        return new ProjectContract(tenantId, storeId, companyId, projectId, number, originalValue);
    }
    public Money ValueWithApprovedVariation(IEnumerable<ContractVariation> variations)
    {
        decimal total = OriginalValue.Amount + variations.Where(x => x.Status == ContractVariationStatus.Approved).Sum(x => x.Amount.Amount);
        return new Money(total, OriginalValue.Currency);
    }
}

[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class ContractVariation : Entity
{
    private ContractVariation(Guid tenantId, Guid? storeId, Guid companyId, Guid contractId, string reason, Money amount) : base(tenantId, storeId)
    { AssignCompany(companyId); ContractId = contractId; Reason = reason.Trim(); Amount = amount; Status = ContractVariationStatus.Proposed; }
    private ContractVariation() { }
    public Guid ContractId { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public Money Amount { get; private set; }
    public ContractVariationStatus Status { get; private set; }
    public static ContractVariation Propose(Guid tenantId, Guid? storeId, Guid companyId, Guid contractId, string reason, Money amount)
    { if (contractId == Guid.Empty || amount.Amount < 0m) throw new ArgumentException("Variation identity and amount are invalid."); ArgumentException.ThrowIfNullOrWhiteSpace(reason); return new(tenantId, storeId, companyId, contractId, reason, amount); }
    public void Approve() { if (Status != ContractVariationStatus.Proposed) throw new InvalidOperationException("Only a proposed variation can be approved."); Status = ContractVariationStatus.Approved; }
    public void Reject() { if (Status != ContractVariationStatus.Proposed) throw new InvalidOperationException("Only a proposed variation can be rejected."); Status = ContractVariationStatus.Rejected; }
}

[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class BillingMilestone : Entity
{
    private BillingMilestone(Guid tenantId, Guid? storeId, Guid companyId, Guid contractId, string name, Money amount) : base(tenantId, storeId)
    { AssignCompany(companyId); ContractId = contractId; Name = name.Trim(); Amount = amount; Status = BillingMilestoneStatus.Planned; }
    private BillingMilestone() { }
    public Guid ContractId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public Money Amount { get; private set; }
    public BillingMilestoneStatus Status { get; private set; }
    public static BillingMilestone Plan(Guid tenantId, Guid? storeId, Guid companyId, Guid contractId, string name, Money amount)
    { if (contractId == Guid.Empty || amount.Amount < 0m) throw new ArgumentException("Milestone identity and amount are invalid."); ArgumentException.ThrowIfNullOrWhiteSpace(name); return new(tenantId, storeId, companyId, contractId, name, amount); }
    public void Approve() { if (Status != BillingMilestoneStatus.Planned) throw new InvalidOperationException("Only a planned milestone can be approved."); Status = BillingMilestoneStatus.Approved; }
    public void MarkBilled() { if (Status != BillingMilestoneStatus.Approved) throw new InvalidOperationException("Only an approved milestone can be billed."); Status = BillingMilestoneStatus.Billed; }
}
