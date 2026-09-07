namespace VumaRetail.Application.Inventory.Sourcing;

using VumaRetail.Domain.Inventory.Sourcing;

/// <summary>
/// Service to expire reservations past their policy window.
/// One company at a time; expiry writes ledger rows so history explains itself.
/// </summary>
public sealed class ReservationExpiryService
{
    private readonly IStockReservationRepository _reservationRepository;
    private readonly IReservationExpiryPolicyRepository _policyRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public ReservationExpiryService(
        IStockReservationRepository reservationRepository,
        IReservationExpiryPolicyRepository policyRepository,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _reservationRepository = reservationRepository ?? throw new ArgumentNullException(nameof(reservationRepository));
        _policyRepository = policyRepository ?? throw new ArgumentNullException(nameof(policyRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>
    /// Expire all reservations past their policy window for a company.
    /// </summary>
    public async Task ExpireDueAsync(Guid companyId, CancellationToken ct)
    {
        var policies = await _policyRepository.GetAllAsync(_unitOfWork.TenantId, ct);
        var now = _clock.UtcNow;

        foreach (var policy in policies.Where(p => p.ExpiryHours.HasValue))
        {
            var expiresBefore = now.AddHours(-(policy.ExpiryHours!.Value));

            var expiredReservations = await _reservationRepository.FindExpiredAsync(
                companyId,
                policy.SourceDocumentType,
                expiresBefore,
                ct);

            foreach (var reservation in expiredReservations)
            {
                var expiredRow = StockReservation.Expire(reservation, now);
                await _reservationRepository.AddAsync(expiredRow, ct);
            }
        }

        await _unitOfWork.SaveChangesAsync(ct);
    }
}
