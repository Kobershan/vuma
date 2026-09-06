using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Registry;

/// <summary>
/// Access grants for a user to act in a specific company with specific roles.
/// </summary>
public sealed class RegistryUserCompanyAccess
{
    private RegistryUserCompanyAccess() { }

    private RegistryUserCompanyAccess(Guid id, Guid tenantId, Guid registryUserId, Guid companyId, string roles, string grantedBy, DateTimeOffset grantedAt)
    {
        Id = id;
        TenantId = tenantId;
        RegistryUserId = registryUserId;
        CompanyId = companyId;
        Roles = Require(roles, nameof(roles));
        GrantedBy = Require(grantedBy, nameof(grantedBy));
        GrantedAt = grantedAt;
    }

    /// <summary>The access grant identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>The tenant that owns this grant.</summary>
    public Guid TenantId { get; private set; }

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
    public static RegistryUserCompanyAccess Create(Guid tenantId, Guid registryUserId, Guid companyId, string roles, string grantedBy, DateTimeOffset grantedAt)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A tenant is required.", nameof(tenantId));
        }
        if (registryUserId == Guid.Empty)
        {
            throw new ArgumentException("A registry user is required.", nameof(registryUserId));
        }
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("A company is required.", nameof(companyId));
        }
        return new RegistryUserCompanyAccess(UuidV7.NewGuid(), tenantId, registryUserId, companyId, roles, grantedBy, grantedAt);
    }

    private static string Require(string value, string parameterName)
        => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A value is required.", parameterName) : value.Trim();
}
