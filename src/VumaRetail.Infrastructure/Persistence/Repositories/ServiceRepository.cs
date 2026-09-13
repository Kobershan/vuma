using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Service;
using VumaRetail.Domain.Service;

namespace VumaRetail.Infrastructure.Persistence.Repositories;

/// <summary>EF persistence boundary for the Stage 23 service coordination records.</summary>
public sealed class ServiceRepository(VumaRetailDbContext context) : IServiceRepository
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

    public void Add(ServiceTicket ticket) => context.ServiceTickets.Add(ticket);
    public void Add(WarrantyClaim claim) => context.WarrantyClaims.Add(claim);
    public void Add(RepairJob job) => context.RepairJobs.Add(job);
    public void Add(ServicePartUsage usage) => context.ServicePartUsages.Add(usage);
}
