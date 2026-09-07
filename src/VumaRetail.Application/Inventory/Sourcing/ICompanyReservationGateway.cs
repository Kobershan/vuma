namespace VumaRetail.Application.Inventory.Sourcing;

using VumaRetail.Domain.Inventory.Sourcing;
using VumaRetail.Domain.Orders;

/// <summary>
/// Gateway for reserving inventory in a company's database.
/// Implementations: production (per-leg scope) and test (named test databases).
/// </summary>
public interface ICompanyReservationGateway
{
    /// <summary>
    /// Reserve a quantity in a company's database.
    /// Idempotent on (intent_id, leg_id).
    /// Re-reads real availability and holds min(available, planned).
    /// </summary>
    Task<decimal> ReserveLegAsync(
        Guid companyId,
        Guid legId,
        Guid intentId,
        StockItemReference itemReference,
        decimal plannedQuantity,
        ReservationSource source,
        Guid sourceDocumentId,
        CancellationToken ct);

    /// <summary>
    /// Compensate a held reservation by creating Release rows.
    /// </summary>
    Task CompensateLegAsync(
        Guid companyId,
        Guid intentId,
        Guid legId,
        CancellationToken ct);
}
