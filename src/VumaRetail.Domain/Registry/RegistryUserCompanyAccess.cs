using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Registry;

/// <summary>
/// Access grants for a user to act in a specific company with specific roles.
/// </summary>
public sealed class RegistryUserCompanyAccess
{
    private RegistryUserCompanyAccess() { }

    private RegistryUserCompanyAccess(Guid id, Guid registryUserId, Guid companyId, string roles, string grantedBy)
    {
        Id = id;
        RegistryUserId = registryUserId;
        CompanyId = companyId;
        Roles = Require(roles, nameof(roles));
        GrantedBy = Require(grantedBy, nameof(grantedBy));
        GrantedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The access grant identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>The registry user.</summary>
    public Guid RegistryUserId { get; private set; }

    /// <summary>The company the user has access to.</summary>
    public Guid CompanyId { get; private set; }

    /// <summary>The roles granted in this company (comma-separated).</summary>
    public string Roles { get; private set; } = string.Empty;

    /// <summary>Who granted the access.</summary>
    public string GrantedBy { get; private set; } = string.Empty;

    /// <summary>When access was granted.</summary>
    public DateTimeOffset GrantedAt { get; private set; }

    /// <summary>Creates a new access grant.</summary>
    public static RegistryUserCompanyAccess Create(Guid registryUserId, Guid companyId, string roles, string grantedBy)
    {
        if (registryUserId == Guid.Empty)
        {
            throw new ArgumentException("A registry user is required.", nameof(registryUserId));
        }
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("A company is required.", nameof(companyId));
        }
        return new RegistryUserCompanyAccess(UuidV7.NewGuid(), registryUserId, companyId, roles, grantedBy);
    }

    private static string Require(string value, string parameterName)
        => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A value is required.", parameterName) : value.Trim();
}
