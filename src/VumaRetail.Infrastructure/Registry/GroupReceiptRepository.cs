using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Domain.Registry;
using VumaRetail.Infrastructure.Persistence;

namespace VumaRetail.Infrastructure.Registry;

/// <summary>
/// Repository for group receipt entities in the registry database.
/// </summary>
public sealed class GroupReceiptRepository : IGroupReceiptRepository
{
    private readonly VumaRegistryDbContext _registry;

    public GroupReceiptRepository(VumaRegistryDbContext registry)
    {
        _registry = registry;
    }

    public async Task<GroupReceipt?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _registry.GroupReceipts.FindAsync([id], cancellationToken);
    }

    public async Task<IReadOnlyList<GroupReceipt>> GetUnallocatedAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        return await Task.FromResult(
            _registry.GroupReceipts
                .Where(r => r.TenantId == tenantId &&
                    (r.Status == GroupReceiptStatus.Draft || r.Status == GroupReceiptStatus.PartiallyAllocated))
                .OrderByDescending(r => r.CapturedAt)
                .ToList());
    }

    public async Task AddAsync(GroupReceipt receipt, CancellationToken cancellationToken = default)
    {
        await _registry.GroupReceipts.AddAsync(receipt, cancellationToken);
    }

    public async Task UpdateAsync(GroupReceipt receipt, CancellationToken cancellationToken = default)
    {
    }

    public async Task<GroupPaymentRun?> GetPaymentByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _registry.GroupPaymentRuns.FindAsync([id], cancellationToken);
    }

    public async Task AddAsync(GroupPaymentRun payment, CancellationToken cancellationToken = default)
    {
        await _registry.GroupPaymentRuns.AddAsync(payment, cancellationToken);
    }

    public async Task UpdateAsync(GroupPaymentRun payment, CancellationToken cancellationToken = default)
    {
    }

    public async Task<InterCompanyClearingIntent?> GetClearingIntentByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _registry.InterCompanyClearingIntents.FindAsync([id], cancellationToken);
    }

    public async Task AddAsync(InterCompanyClearingIntent intent, CancellationToken cancellationToken = default)
    {
        await _registry.InterCompanyClearingIntents.AddAsync(intent, cancellationToken);
    }

    public async Task UpdateAsync(InterCompanyClearingIntent intent, CancellationToken cancellationToken = default)
    {
    }

    public async Task<IReadOnlyList<InterCompanyClearingIntent>> GetOutstandingIntentsAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        return await Task.FromResult(
            _registry.InterCompanyClearingIntents
                .Where(i => i.TenantId == tenantId && i.State != InterCompanyClearingIntentState.Settled && i.State != InterCompanyClearingIntentState.Compensated)
                .ToList());
    }

    public async Task<IReadOnlyList<InterCompanyClearingIntent>> GetSettledIntentsForDocumentAsync(Guid groupDocumentId, CancellationToken cancellationToken = default)
    {
        return await Task.FromResult(
            _registry.InterCompanyClearingIntents
                .Where(i => i.GroupDocumentId == groupDocumentId && i.State == InterCompanyClearingIntentState.Settled)
                .ToList());
    }
}
