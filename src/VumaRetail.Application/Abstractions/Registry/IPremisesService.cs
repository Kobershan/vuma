using VumaRetail.Domain.Registry;

namespace VumaRetail.Application.Abstractions.Registry;

/// <summary>
/// Manages premises occupancy and mirrors bin layouts into occupying companies' warehouses.
/// </summary>
public interface IPremisesService
{
    /// <summary>Creates a new premises.</summary>
    Task<Premises> CreateAsync(Guid tenantId, string code, string name, string address, string geoLocation, string tradingHours, CancellationToken cancellationToken = default);

    /// <summary>Adds a company as an occupant of a premises.</summary>
    Task<PremisesOccupancy> AddOccupancyAsync(Guid premisesId, Guid companyId, Guid storeId, CancellationToken cancellationToken = default);

    /// <summary>Publishes a bin layout from a premises into all occupying companies' warehouse schemas as a saga leg.</summary>
    Task PublishBinLayoutAsync(Guid premisesId, CancellationToken cancellationToken = default);

    /// <summary>Gets all occupancies for a premises.</summary>
    Task<IReadOnlyList<PremisesOccupancy>> GetOccupanciesAsync(Guid premisesId, CancellationToken cancellationToken = default);
}
