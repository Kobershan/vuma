using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Assets;
using VumaRetail.Application.Service;
using VumaRetail.Domain.Assets;
using VumaRetail.Domain.Service;

namespace VumaRetail.Infrastructure.Persistence.Repositories;

/// <summary>EF persistence boundary for the Stage 23 service coordination records.</summary>
public sealed class ServiceRepository(VumaRetailDbContext context) : IServiceRepository, IAssetRepository, IChecklistRepository
{
    public async Task<IReadOnlyList<ServiceTicket>> ListTicketsAsync(Guid companyId, Guid? customerId = null, CancellationToken cancellationToken = default)
        => await context.ServiceTickets.AsNoTracking()
            .Where(x => x.CompanyId == companyId && (customerId == null || x.CustomerId == customerId))
            .OrderByDescending(x => x.OpenedAtUtc).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<ServiceCustodyEvent>> ListCustodyAsync(Guid companyId, Guid? customerId = null, CancellationToken cancellationToken = default)
        => await context.ServiceCustodyEvents.AsNoTracking()
            .Where(x => x.CompanyId == companyId && (customerId == null || x.CustomerId == customerId))
            .OrderByDescending(x => x.OccurredAtUtc).ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task<ServiceTicket?> FindTicketAsync(Guid id, CancellationToken cancellationToken = default)
        => context.ServiceTickets.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<ServiceTicket?> FindTicketByOperationIdAsync(Guid operationId, CancellationToken cancellationToken = default)
        => context.ServiceTickets.FirstOrDefaultAsync(x => x.OperationId == operationId, cancellationToken);

    public Task<WarrantyClaim?> FindWarrantyAsync(Guid id, CancellationToken cancellationToken = default)
        => context.WarrantyClaims.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<RepairJob?> FindRepairAsync(Guid id, CancellationToken cancellationToken = default)
        => context.RepairJobs.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<ServicePartUsage?> FindPartUsageByOperationIdAsync(Guid operationId, CancellationToken cancellationToken = default)
        => context.ServicePartUsages.FirstOrDefaultAsync(x => x.OperationId == operationId, cancellationToken);

    public Task<ServiceSla?> FindSlaByNameAsync(Guid companyId, string name, CancellationToken cancellationToken = default)
        => context.ServiceSlas.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Name == name.Trim(), cancellationToken);
    public Task<ServiceSlaBreachEvent?> FindSlaBreachAsync(Guid ticketId, string slaName,
        ServiceSlaBreachType breachType, CancellationToken cancellationToken = default)
        => context.ServiceSlaBreachEvents.FirstOrDefaultAsync(x => x.TicketId == ticketId
            && x.SlaName == slaName.Trim() && x.BreachType == breachType, cancellationToken);
    public async Task<IReadOnlyList<ServiceSlaBreachEvent>> ListSlaBreachesAsync(Guid companyId,
        Guid? ticketId = null, CancellationToken cancellationToken = default)
        => await context.ServiceSlaBreachEvents.AsNoTracking()
            .Where(x => x.CompanyId == companyId && (ticketId == null || x.TicketId == ticketId))
            .OrderByDescending(x => x.ObservedAtUtc).ThenBy(x => x.TicketId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public void Add(ServiceTicket ticket) => context.ServiceTickets.Add(ticket);
    public void Add(WarrantyClaim claim) => context.WarrantyClaims.Add(claim);
    public void Add(RepairJob job) => context.RepairJobs.Add(job);
    public void Add(ServicePartUsage usage) => context.ServicePartUsages.Add(usage);
    public void Add(ServiceSla sla) => context.ServiceSlas.Add(sla);
    public void Add(ServiceSlaBreachEvent breach) => context.ServiceSlaBreachEvents.Add(breach);

    public Task<FixedAsset?> FindAssetAsync(Guid id, CancellationToken cancellationToken = default)
        => context.FixedAssets.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<AssetBook?> FindBookAsync(Guid id, CancellationToken cancellationToken = default)
        => context.AssetBooks.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<AssetBook?> FindBookAsync(Guid assetId, string bookName, CancellationToken cancellationToken = default)
        => context.AssetBooks.FirstOrDefaultAsync(x => x.AssetId == assetId && x.BookName == bookName.Trim(), cancellationToken);

    public Task<DepreciationRun?> FindDepreciationRunAsync(Guid assetBookId, DateOnly period, CancellationToken cancellationToken = default)
        => context.DepreciationRuns.FirstOrDefaultAsync(x => x.AssetBookId == assetBookId && x.Period == period, cancellationToken);

    public Task<MaintenanceOrder?> FindMaintenanceOrderAsync(Guid id, CancellationToken cancellationToken = default)
        => context.MaintenanceOrders.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public void Add(FixedAsset asset) => context.FixedAssets.Add(asset);
    public void Add(AssetBook book) => context.AssetBooks.Add(book);
    public void Add(DepreciationRun run) => context.DepreciationRuns.Add(run);
    public void Add(MaintenanceOrder order) => context.MaintenanceOrders.Add(order);
    public Task<StoreChecklist?> FindAsync(Guid id, CancellationToken cancellationToken = default) => context.StoreChecklists.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    public Task<ChecklistExecution?> FindExecutionAsync(Guid id, CancellationToken cancellationToken = default) => context.ChecklistExecutions.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    public Task<ChecklistExecution?> FindExecutionByOperationIdAsync(Guid operationId, CancellationToken cancellationToken = default) => context.ChecklistExecutions.FirstOrDefaultAsync(x => x.OperationId == operationId, cancellationToken);
    public void Add(StoreChecklist checklist) => context.StoreChecklists.Add(checklist);
    public void Add(ChecklistExecution execution) => context.ChecklistExecutions.Add(execution);
}
