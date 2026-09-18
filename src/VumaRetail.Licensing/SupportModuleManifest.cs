using VumaRetail.Application.Abstractions.Licensing;

namespace VumaRetail.Licensing;

/// <summary>The recurring support capabilities sold on the Vuma rate card.</summary>
public sealed class SupportModuleManifest : IModuleManifest
{
    /// <inheritdoc />
    public string Module => "support";

    /// <inheritdoc />
    public string LicenceFlag => "support";

    /// <inheritdoc />
    public string Description => "Recurring support, maintenance, resilience, security and offline assurance.";

    /// <inheritdoc />
    public bool IsCore => false;

    /// <inheritdoc />
    public IReadOnlyCollection<ModuleEntitlementDeclaration> Entitlements =>
    [
        new("support.standard", "Standard Support & Bug-Fix"),
        new("support.maintenance", "Codebase Maintenance"),
        new("support.backup-dr", "Backup & DR"),
        new("support.monitoring", "Monitoring"),
        new("support.security-licensing", "Security & Licensing"),
        new("support.offline-sync", "Offline Sync Assurance"),
        new("support.hardware", "Hardware Support"),
    ];
}
