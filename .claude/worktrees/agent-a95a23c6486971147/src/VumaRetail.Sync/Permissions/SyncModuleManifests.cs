using VumaRetail.Application.Abstractions.Licensing;

namespace VumaRetail.Sync.Permissions;

/// <summary>
/// The <c>sync</c> module's manifest (R7).
/// </summary>
/// <remarks>
/// Core. A store that stopped replicating because of a plan change would diverge from head office
/// silently, and the divergence would outlive the plan change by however long it took somebody to
/// notice — which is the failure mode ADR-006 exists to make impossible.
/// </remarks>
public sealed class SyncModuleManifest : IModuleManifest
{
    /// <inheritdoc />
    public string Module => "sync";

    /// <inheritdoc />
    public string LicenceFlag => "sync";

    /// <inheritdoc />
    public string Description => "Replication between terminals, the store server and the cloud.";

    /// <inheritdoc />
    public bool IsCore => true;
}

/// <summary>
/// The <c>backup</c> module's manifest (R7).
/// </summary>
/// <remarks>
/// Core, and non-negotiably so. ADR-028's third rule keeps backup running even while a tenant is
/// read-only; making it an entitlement would let a plan change do what a lapsed subscription is
/// forbidden from doing, which is put a customer's data at risk to make a commercial point.
/// </remarks>
public sealed class BackupModuleManifest : IModuleManifest
{
    /// <inheritdoc />
    public string Module => "backup";

    /// <inheritdoc />
    public string LicenceFlag => "backup";

    /// <inheritdoc />
    public string Description => "Encrypted snapshots, verification and restore (R4).";

    /// <inheritdoc />
    public bool IsCore => true;
}
