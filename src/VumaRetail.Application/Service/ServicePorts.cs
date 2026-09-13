#pragma warning disable CS1591
using VumaRetail.Domain.Service;

namespace VumaRetail.Application.Service;

/// <summary>Persistence boundary for service coordination records.</summary>
public interface IServiceRepository
{
    Task<IReadOnlyList<ServiceTicket>> ListTicketsAsync(Guid companyId, Guid? customerId = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ServiceCustodyEvent>> ListCustodyAsync(Guid companyId, Guid? customerId = null, CancellationToken cancellationToken = default);
    Task<ServiceTicket?> FindTicketAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ServiceTicket?> FindTicketByOperationIdAsync(Guid operationId, CancellationToken cancellationToken = default);
    Task<WarrantyClaim?> FindWarrantyAsync(Guid id, CancellationToken cancellationToken = default);
    Task<RepairJob?> FindRepairAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ServicePartUsage?> FindPartUsageByOperationIdAsync(Guid operationId, CancellationToken cancellationToken = default);
    void Add(ServiceTicket ticket);
    void Add(WarrantyClaim claim);
    void Add(RepairJob job);
    void Add(ServicePartUsage usage);
}
