#pragma warning disable CS1591
using VumaRetail.Domain.Assets;

namespace VumaRetail.Application.Assets;

public interface IAssetRepository
{
    Task<FixedAsset?> FindAssetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<AssetBook?> FindBookAsync(Guid id, CancellationToken cancellationToken = default);
    Task<AssetBook?> FindBookAsync(Guid assetId, string bookName, CancellationToken cancellationToken = default);
    Task<DepreciationRun?> FindDepreciationRunAsync(Guid assetBookId, DateOnly period, CancellationToken cancellationToken = default);
    Task<MaintenanceOrder?> FindMaintenanceOrderAsync(Guid id, CancellationToken cancellationToken = default);
    void Add(MaintenanceOrder order);
    void Add(FixedAsset asset);
    void Add(AssetBook book);
    void Add(DepreciationRun run);
}

/// <summary>Raises depreciation runs through Finance's posting-rule boundary.</summary>
public interface IAssetDepreciationFinancialEventPublisher
{
    Task PublishAsync(DepreciationRun run, CancellationToken cancellationToken = default);
}

public interface IChecklistRepository
{
    Task<StoreChecklist?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ChecklistExecution?> FindExecutionAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ChecklistExecution?> FindExecutionByOperationIdAsync(Guid operationId, CancellationToken cancellationToken = default);
    void Add(StoreChecklist checklist);
    void Add(ChecklistExecution execution);
}
