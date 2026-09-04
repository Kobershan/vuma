using VumaRetail.Domain.Registry;

namespace VumaRetail.Application.Abstractions.Registry;

/// <summary>
/// Manages the registry user directory and company access grants.
/// </summary>
public interface IRegistryUserService
{
    /// <summary>Creates a new registry user.</summary>
    Task<RegistryUser> CreateAsync(Guid tenantId, string login, string displayName, Guid operatorId, string contactDetails, CancellationToken cancellationToken = default);

    /// <summary>Grants a user access to a company. Refused if the companies are under different operators.</summary>
    Task<RegistryUserCompanyAccess> GrantAccessAsync(Guid registryUserId, Guid companyId, string roles, string grantedBy, CancellationToken cancellationToken = default);

    /// <summary>Revokes a user's access to a company.</summary>
    Task RevokeAccessAsync(Guid registryUserId, Guid companyId, CancellationToken cancellationToken = default);

    /// <summary>Lists all companies a user has access to.</summary>
    Task<IReadOnlyList<Guid>> ListUserCompaniesAsync(Guid registryUserId, CancellationToken cancellationToken = default);
}
