using VumaRetail.Application.Abstractions.Licensing;

namespace VumaRetail.Application.Identity.Permissions;

/// <summary>
/// The <c>platform</c> module's manifest (R7).
/// </summary>
/// <remarks>
/// Core. Tenants, stores and the audit trail are not a thing anybody buys separately, and a licence
/// that omitted them would describe a system with nowhere to put a row.
/// </remarks>
public sealed class PlatformModuleManifest : IModuleManifest
{
    /// <inheritdoc />
    public string Module => "platform";

    /// <inheritdoc />
    public string LicenceFlag => "platform";

    /// <inheritdoc />
    public string Description => "Tenants, stores, localisation and the audit trail.";

    /// <inheritdoc />
    public bool IsCore => true;
}

/// <summary>
/// The <c>identity</c> module's manifest (R7).
/// </summary>
/// <remarks>
/// Core, and it is the clearest case of why <see cref="IModuleManifest.IsCore"/> exists: a tenant
/// whose licence failed to enable identity would be a tenant nobody could sign in to fix.
/// </remarks>
public sealed class IdentityModuleManifest : IModuleManifest
{
    /// <inheritdoc />
    public string Module => "identity";

    /// <inheritdoc />
    public string LicenceFlag => "identity";

    /// <inheritdoc />
    public string Description => "Users, roles, permissions and terminals.";

    /// <inheritdoc />
    public bool IsCore => true;
}
