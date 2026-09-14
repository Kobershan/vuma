#pragma warning disable CS1591
using VumaRetail.Domain.Quality;
using VumaRetail.Domain.Primitives;
using VumaRetail.Application.Inventory;

namespace VumaRetail.Application.Quality;

public interface IInspectionPlanRepository
{
    Task<InspectionPlan?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    Task<InspectionPlan?> FindPublishedAsync(Guid companyId, Guid? itemId, Guid? itemVariantId, CancellationToken cancellationToken = default);
    void Add(InspectionPlan plan);
}

public interface IQualityCertificateRepository
{
    Task<QualityCertificate?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    void Add(QualityCertificate certificate);
}

public interface IRecallCaseRepository
{
    Task<RecallCase?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    Task<RecallCase?> FindByOperationIdAsync(Guid operationId, CancellationToken cancellationToken = default);
    void Add(RecallCase recall);
}

/// <summary>Persistence boundary for tenant/company-scoped quality holds.</summary>
public interface IQualityHoldRepository
{
    Task<QualityHold?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    Task<QualityHold?> FindByOperationIdAsync(Guid operationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<QualityHold>> ListActiveForStockAsync(Guid locationId, Guid? itemId, Guid? itemVariantId, CancellationToken cancellationToken = default);
    void Add(QualityHold hold);
}

/// <summary>Dispatch boundary used by Warehouse to prevent held stock leaving the site.</summary>
public interface IQualityDispatchGate
{
    Task EnsureDispatchAllowedAsync(Guid locationId, Guid? itemId, Guid? itemVariantId, Quantity quantity,
        CancellationToken cancellationToken = default);
    Task EnsureDispatchAllowedAsync(Guid locationId, Guid? itemId, Guid? itemVariantId, Quantity quantity,
        DateOnly asOfDate, CancellationToken cancellationToken = default);
}

/// <summary>Default gate when Quality is not hosted.</summary>
public sealed class NoQualityDispatchGate : IQualityDispatchGate
{
    public Task EnsureDispatchAllowedAsync(Guid locationId, Guid? itemId, Guid? itemVariantId, Quantity quantity,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task EnsureDispatchAllowedAsync(Guid locationId, Guid? itemId, Guid? itemVariantId, Quantity quantity,
        DateOnly asOfDate, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

/// <summary>Blocks dispatch whenever the requested SKU has an active quality hold.</summary>
public sealed class QualityDispatchGate(IQualityHoldRepository holds, IStockLedgerRepository? ledger = null) : IQualityDispatchGate
{
    public Task EnsureDispatchAllowedAsync(Guid locationId, Guid? itemId, Guid? itemVariantId, Quantity quantity,
        CancellationToken cancellationToken = default)
        => EnsureDispatchAllowedAsync(locationId, itemId, itemVariantId, quantity,
            null, cancellationToken);

    public async Task EnsureDispatchAllowedAsync(Guid locationId, Guid? itemId, Guid? itemVariantId, Quantity quantity,
        DateOnly asOfDate, CancellationToken cancellationToken = default)
        => await EnsureDispatchAllowedAsync(locationId, itemId, itemVariantId, quantity,
            (DateOnly?)asOfDate, cancellationToken).ConfigureAwait(false);

    private async Task EnsureDispatchAllowedAsync(Guid locationId, Guid? itemId, Guid? itemVariantId, Quantity quantity,
        DateOnly? asOfDate, CancellationToken cancellationToken)
    {
        if (await holds.ListActiveForStockAsync(locationId, itemId, itemVariantId, cancellationToken).ConfigureAwait(false) is { Count: > 0 })
        {
            throw QualityRuleException.DispatchBlocked();
        }

        if (ledger is not null && asOfDate is { } date && await ledger.HasExpiredTrackedStockAsync(
                locationId, itemId, itemVariantId, date, cancellationToken)
                .ConfigureAwait(false))
        {
            throw QualityRuleException.DispatchBlockedForExpiredStock();
        }
    }
}

public interface IInspectionResultRepository
{
    Task<InspectionResult?> FindByOperationIdAsync(Guid operationId, CancellationToken cancellationToken = default);
    void Add(InspectionResult result);
}

public interface INonConformanceRepository
{
    Task<NonConformance?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    Task<NonConformance?> FindByOperationIdAsync(Guid operationId, CancellationToken cancellationToken = default);
    void Add(NonConformance nonConformance);
}
