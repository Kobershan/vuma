#pragma warning disable CS1591, IDE0011
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Assets;

public enum AssetStatus { Draft, InService, Disposed }

public enum MaintenanceOrderStatus { Open, InProgress, Completed, Cancelled }

[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class StoreChecklist : Entity
{
    private StoreChecklist(Guid tenantId, Guid? storeId, Guid companyId, string code, string name, string itemCodes)
        : base(tenantId, storeId)
    {
        AssignCompany(companyId);
        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
        ItemCodes = itemCodes;
    }

    private StoreChecklist() { }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string ItemCodes { get; private set; } = string.Empty;

    public static StoreChecklist Create(Guid tenantId, Guid? storeId, Guid companyId, string code,
        string name, IReadOnlyCollection<string> itemCodes)
    {
        if (tenantId == Guid.Empty || companyId == Guid.Empty) throw new ArgumentException("Tenant and company are required.");
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (itemCodes is null || itemCodes.Count == 0) throw new ArgumentException("At least one checklist item is required.", nameof(itemCodes));
        string[] normalized = itemCodes.Select(item => item.Trim().ToUpperInvariant()).ToArray();
        if (normalized.Any(string.IsNullOrWhiteSpace) || normalized.Distinct(StringComparer.Ordinal).Count() != normalized.Length)
            throw new ArgumentException("Checklist item codes must be unique and non-empty.", nameof(itemCodes));
        return new StoreChecklist(tenantId, storeId, companyId, code, name, string.Join('|', normalized));
    }
}

[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.AppendOnly)]
public sealed class ChecklistExecution : Entity, IImmutableRecord
{
    private ChecklistExecution(Guid tenantId, Guid? storeId, Guid companyId, Guid checklistId, Guid operationId,
        string deviceId, DateTimeOffset capturedAt, DateTimeOffset submittedAt, string evidenceReference)
        : base(tenantId, storeId)
    {
        AssignCompany(companyId);
        ChecklistId = checklistId;
        OperationId = operationId;
        DeviceId = deviceId.Trim();
        CapturedAt = capturedAt;
        SubmittedAt = submittedAt;
        EvidenceReference = evidenceReference.Trim();
    }

    private ChecklistExecution() { }
    public Guid ChecklistId { get; private set; }
    public Guid OperationId { get; private set; }
    public string DeviceId { get; private set; } = string.Empty;
    public DateTimeOffset CapturedAt { get; private set; }
    public DateTimeOffset SubmittedAt { get; private set; }
    public string EvidenceReference { get; private set; } = string.Empty;

    public static ChecklistExecution Submit(Guid tenantId, Guid? storeId, Guid companyId, Guid checklistId,
        Guid operationId, string deviceId, DateTimeOffset capturedAt, DateTimeOffset submittedAt,
        string evidenceReference)
    {
        if (tenantId == Guid.Empty || companyId == Guid.Empty || checklistId == Guid.Empty || operationId == Guid.Empty)
            throw new ArgumentException("Checklist identities are required.");
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceReference);
        if (submittedAt < capturedAt) throw new ArgumentException("Submission cannot precede capture.", nameof(submittedAt));
        return new ChecklistExecution(tenantId, storeId, companyId, checklistId, operationId, deviceId,
            capturedAt, submittedAt, evidenceReference);
    }
}

[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.LastWriterWins)]
public sealed class MaintenanceOrder : Entity
{
    private MaintenanceOrder(Guid tenantId, Guid? storeId, Guid companyId, Guid assetId,
        string description, DateOnly? scheduledOn) : base(tenantId, storeId)
    {
        AssignCompany(companyId);
        AssetId = assetId;
        Description = description.Trim();
        ScheduledOn = scheduledOn;
    }

    private MaintenanceOrder() { }

    public Guid AssetId { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public DateOnly? ScheduledOn { get; private set; }
    public MaintenanceOrderStatus Status { get; private set; } = MaintenanceOrderStatus.Open;
    public DateTimeOffset? CompletedAt { get; private set; }

    public static MaintenanceOrder Create(Guid tenantId, Guid? storeId, Guid companyId, Guid assetId,
        string description, DateOnly? scheduledOn = null)
    {
        if (tenantId == Guid.Empty || companyId == Guid.Empty || assetId == Guid.Empty)
            throw new ArgumentException("Tenant, company and asset are required.");
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        return new MaintenanceOrder(tenantId, storeId, companyId, assetId, description, scheduledOn);
    }

    public void Start()
    {
        if (Status != MaintenanceOrderStatus.Open)
            throw new InvalidOperationException("Only an open maintenance order can start.");
        Status = MaintenanceOrderStatus.InProgress;
    }

    public void Complete(DateTimeOffset completedAt)
    {
        if (Status != MaintenanceOrderStatus.InProgress)
            throw new InvalidOperationException("Only an in-progress maintenance order can complete.");
        CompletedAt = completedAt;
        Status = MaintenanceOrderStatus.Completed;
    }

    public void Cancel()
    {
        if (Status is MaintenanceOrderStatus.Completed or MaintenanceOrderStatus.Cancelled)
            throw new InvalidOperationException("Closed maintenance orders cannot be cancelled.");
        Status = MaintenanceOrderStatus.Cancelled;
    }
}

[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class FixedAsset : Entity
{
    private FixedAsset(Guid tenantId, Guid? storeId, Guid companyId, string assetNumber, string description,
        DateOnly acquiredOn, Money cost) : base(tenantId, storeId)
    {
        AssignCompany(companyId); AssetNumber = assetNumber.Trim(); Description = description.Trim();
        AcquiredOn = acquiredOn; Cost = cost; Status = AssetStatus.Draft;
    }
    private FixedAsset() { }
    public string AssetNumber { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public DateOnly AcquiredOn { get; private set; }
    public Money Cost { get; private set; }
    public AssetStatus Status { get; private set; }
    public DateOnly? DisposedOn { get; private set; }

    public static FixedAsset Create(Guid tenantId, Guid? storeId, Guid companyId, string assetNumber,
        string description, DateOnly acquiredOn, Money cost)
    {
        if (tenantId == Guid.Empty || companyId == Guid.Empty) throw new ArgumentException("Tenant and company are required.");
        ArgumentException.ThrowIfNullOrWhiteSpace(assetNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        if (cost.Amount < 0m) throw new ArgumentOutOfRangeException(nameof(cost));
        return new FixedAsset(tenantId, storeId, companyId, assetNumber, description, acquiredOn, cost);
    }
    public void PlaceInService() { if (Status != AssetStatus.Draft) throw new InvalidOperationException("Only a draft asset can enter service."); Status = AssetStatus.InService; }
    public void Dispose(DateOnly date) { if (Status != AssetStatus.InService) throw new InvalidOperationException("Only an in-service asset can be disposed."); DisposedOn = date; Status = AssetStatus.Disposed; }
}

[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class AssetBook : Entity
{
    private AssetBook(Guid tenantId, Guid? storeId, Guid companyId, Guid assetId, string bookName,
        DateOnly inServiceOn, Money residualValue, int usefulLifeMonths) : base(tenantId, storeId)
    { AssignCompany(companyId); AssetId = assetId; BookName = bookName.Trim(); InServiceOn = inServiceOn; ResidualValue = residualValue; UsefulLifeMonths = usefulLifeMonths; }
    private AssetBook() { }
    public Guid AssetId { get; private set; }
    public string BookName { get; private set; } = string.Empty;
    public DateOnly InServiceOn { get; private set; }
    public Money ResidualValue { get; private set; }
    public int UsefulLifeMonths { get; private set; }
    public static AssetBook Create(Guid tenantId, Guid? storeId, Guid companyId, Guid assetId, string bookName,
        DateOnly inServiceOn, Money residualValue, int usefulLifeMonths)
    {
        if (tenantId == Guid.Empty || companyId == Guid.Empty || assetId == Guid.Empty) throw new ArgumentException("Asset identities are required.");
        ArgumentException.ThrowIfNullOrWhiteSpace(bookName);
        if (usefulLifeMonths <= 0 || residualValue.Amount < 0m) throw new ArgumentOutOfRangeException(nameof(usefulLifeMonths));
        return new AssetBook(tenantId, storeId, companyId, assetId, bookName, inServiceOn, residualValue, usefulLifeMonths);
    }
}

public sealed record DepreciationCharge(Guid AssetId, Guid AssetBookId, DateOnly Period, Money Amount, Money ClosingNetBookValue);

[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class DepreciationRun : Entity
{
    private DepreciationRun(Guid tenantId, Guid? storeId, Guid companyId, Guid assetId, Guid assetBookId,
        DateOnly period, Money amount, Money closingNetBookValue) : base(tenantId, storeId)
    {
        AssignCompany(companyId); AssetId = assetId; AssetBookId = assetBookId; Period = period;
        Amount = amount; ClosingNetBookValue = closingNetBookValue;
    }
    private DepreciationRun() { }
    public Guid AssetId { get; private set; }
    public Guid AssetBookId { get; private set; }
    public DateOnly Period { get; private set; }
    public Money Amount { get; private set; }
    public Money ClosingNetBookValue { get; private set; }

    public static DepreciationRun Record(Guid tenantId, Guid? storeId, Guid companyId, DepreciationCharge charge)
    {
        ArgumentNullException.ThrowIfNull(charge);
        if (tenantId == Guid.Empty || companyId == Guid.Empty) throw new ArgumentException("Tenant and company are required.");
        return new DepreciationRun(tenantId, storeId, companyId, charge.AssetId, charge.AssetBookId,
            charge.Period, charge.Amount, charge.ClosingNetBookValue);
    }
}

public static class DepreciationCalculator
{
    public static DepreciationCharge Calculate(FixedAsset asset, AssetBook book, DateOnly period)
    {
        ArgumentNullException.ThrowIfNull(asset); ArgumentNullException.ThrowIfNull(book);
        if (asset.Id != book.AssetId) throw new InvalidOperationException("The asset book does not belong to the asset.");
        if (period < new DateOnly(book.InServiceOn.Year, book.InServiceOn.Month, 1)) throw new ArgumentOutOfRangeException(nameof(period));
        int elapsed = (period.Year - book.InServiceOn.Year) * 12 + period.Month - book.InServiceOn.Month;
        if (elapsed >= book.UsefulLifeMonths) return new(asset.Id, book.Id, period, Money.Zero(asset.Cost.Currency), book.ResidualValue);
        decimal depreciable = asset.Cost.Amount - book.ResidualValue.Amount;
        decimal monthly = decimal.Round(depreciable / book.UsefulLifeMonths, 2, Money.Rounding);
        decimal charged = Math.Min(monthly, Math.Max(0m, asset.Cost.Amount - book.ResidualValue.Amount));
        decimal net = asset.Cost.Amount - (charged * (elapsed + 1));
        return new(asset.Id, book.Id, period, new Money(charged, asset.Cost.Currency),
            new Money(Math.Max(book.ResidualValue.Amount, net), asset.Cost.Currency));
    }
}
