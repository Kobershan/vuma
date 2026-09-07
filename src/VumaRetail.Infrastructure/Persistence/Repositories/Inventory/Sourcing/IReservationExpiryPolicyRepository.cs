namespace VumaRetail.Infrastructure.Persistence.Repositories.Inventory.Sourcing;

using VumaRetail.Application.Inventory.Sourcing;
using VumaRetail.Domain.Inventory.Sourcing;

/// <summary>
/// Repository interface for reservation expiry policies.
/// </summary>
public interface IReservationExpiryPolicyRepository
{
    Task<IReadOnlyList<ReservationExpiryPolicy>> GetAllAsync(Guid tenantId, CancellationToken ct);
    Task AddAsync(ReservationExpiryPolicy policy, CancellationToken ct);
}
