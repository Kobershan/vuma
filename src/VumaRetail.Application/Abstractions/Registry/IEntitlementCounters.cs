using VumaRetail.Application.Abstractions.Licensing;

namespace VumaRetail.Application.Abstractions.Registry;

/// <summary>
/// Metering counters for the trading group module (R10: counts only, no business data).
/// Extended from the platform to include trading-group dimensions.
/// </summary>
public interface IEntitlementCounters
{
    /// <summary>Number of active companies under this tenant.</summary>
    Task<int> CompaniesActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Number of named users per company.</summary>
    Task<int> NamedUsersPerCompanyAsync(Guid companyId, CancellationToken cancellationToken = default);

    /// <summary>Number of tills registered per company.</summary>
    Task<int> TillsPerCompanyAsync(Guid companyId, CancellationToken cancellationToken = default);

    /// <summary>Number of active company links.</summary>
    Task<int> ActiveLinksAsync(CancellationToken cancellationToken = default);
}
