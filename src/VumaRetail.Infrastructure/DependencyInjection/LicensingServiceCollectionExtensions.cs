using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Infrastructure.Licensing;
using VumaRetail.Infrastructure.Persistence.Repositories;
using VumaRetail.Licensing;
using VumaRetail.Licensing.Control;
using VumaRetail.Licensing.Enforcement;
using VumaRetail.Licensing.Entitlements;
using VumaRetail.Licensing.Hosting;
using VumaRetail.Licensing.Metering;
using VumaRetail.Licensing.Permissions;
using VumaRetail.Licensing.Signing;
using VumaRetail.Sync.Permissions;

namespace VumaRetail.Infrastructure.DependencyInjection;

/// <summary>Registers Stage 04b's licensing, activation and entitlement with the host's container.</summary>
public static class LicensingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the enforcement ladder, the entitlement choke point and the read-only guard.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <param name="options">The ladder and the pinned public key.</param>
    /// <param name="stateDirectory">Where the install id and the licence shadow copies live.</param>
    /// <param name="integrityHash">The SHA-256 the installer recorded for the licensing assembly.</param>
    /// <returns>The container, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Call after <c>AddVumaPersistence</c> and <c>AddVumaSync</c>: the repositories read the
    /// <c>DbContext</c>, and the activation repository needs this node's identity.
    /// </para>
    /// <para>
    /// The guard goes into pipeline slot 50, which Stage 03 reserved and left empty. Fifty is outside
    /// validation and outside the transaction, so a refused write costs no database round trip.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddVumaLicensing(
        this IServiceCollection services,
        LicensingOptions options,
        string stateDirectory,
        string integrityHash = "")
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(stateDirectory);

        services.AddSingleton(options);

        services.AddSingleton<ILicenceVerifier>(
            new Ed25519LicenceVerifier(options.PublicKey, options.UsesDevelopmentKey));

        services.AddSingleton<IEnforcementPolicy>(new EnforcementPolicy(options));

        // Singleton, and it has to be. It records which till sessions are open right now; a scoped one
        // would forget between the request that opened a sale and the request that finishes it.
        services.AddSingleton<IOpenSessionRegistry, OpenSessionRegistry>();

        services.AddSingleton<IHardwareFingerprintProvider, MachineFingerprintProvider>();
        services.AddSingleton<IInstallIdentity>(provider => new FileInstallIdentity(
            stateDirectory,
            provider.GetRequiredService<IClock>()));
        services.AddSingleton<IIntegrityChecker>(new AssemblyIntegrityChecker(integrityHash));

        services.AddSingleton<ILicenceShadowStore>(provider => new FileLicenceShadowStore(
            stateDirectory,
            provider.GetRequiredService<IHardwareFingerprintProvider>()));

        services.AddScoped<IActivationRepository, ActivationRepository>();
        services.AddScoped<ILicenceRepository, LicenceRepository>();
        services.AddScoped<ILeaseRepository, LeaseRepository>();
        services.AddScoped<ILicenceStateRepository, LicenceStateRepository>();
        services.AddScoped<IMeteringRepository, MeteringRepository>();
        services.AddScoped<ISupportGrantRepository, SupportGrantRepository>();
        services.AddScoped<IUsageCounterSource, UsageCounterSource>();

        // Scoped and memoised per scope: a request may ask the choke point three times and must get
        // one answer, without paying for three lease reads.
        services.AddScoped<IEntitlementService, EntitlementService>();
        services.AddScoped<MeteringCollector>();

        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IPipelineBehaviour, ReadOnlyGuardBehaviour>());

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModulePermissions, LicensingPermissions>());

        // Every module's manifest, declared next to that module's code. An architecture test asserts
        // the set matches the set of modules that declare permissions.
        services.TryAddEnumerable(
        [
            ServiceDescriptor.Singleton<IModuleManifest, PlatformModuleManifest>(),
            ServiceDescriptor.Singleton<IModuleManifest, IdentityModuleManifest>(),
            ServiceDescriptor.Singleton<IModuleManifest, SyncModuleManifest>(),
            ServiceDescriptor.Singleton<IModuleManifest, BackupModuleManifest>(),
            ServiceDescriptor.Singleton<IModuleManifest, LicensingModuleManifest>(),
        ]);

        // The licensing handlers and validators live in VumaRetail.Licensing, which the
        // Application-only scan does not reach.
        services.AddVumaMessaging(typeof(VumaRetail.Licensing.AssemblyMarker).Assembly);

        return services;
    }

    /// <summary>
    /// Registers the HTTP device-API client and the background lease, heartbeat and metering passes.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <param name="options">The control plane's address and the intervals.</param>
    /// <param name="host">The tenant a background pass runs as.</param>
    /// <returns>The container, for chaining.</returns>
    /// <remarks>
    /// Separate from <see cref="AddVumaLicensing"/> for the same reason the outbox dispatcher is
    /// separate from the outbox: the enforcement half must work on a machine that never talks to
    /// anybody — a restore in progress, a test, a cloud tier that is not itself licensed — while the
    /// talking half is the store server's job alone.
    /// </remarks>
    public static IServiceCollection AddVumaControlPlaneClient(
        this IServiceCollection services,
        LicensingOptions options,
        LicensingHostTenant host)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(host);

        services.AddSingleton(host);

        services.AddHttpClient<IControlPlaneClient, HttpControlPlaneClient>(client =>
        {
            if (!string.IsNullOrWhiteSpace(options.ControlPlaneBaseAddress))
            {
                client.BaseAddress = new Uri(options.ControlPlaneBaseAddress);
            }

            client.Timeout = options.ControlPlaneTimeout;
        });

        services.AddHostedService<LicenceMaintenanceService>();
        services.AddHostedService<MeteringRollupService>();

        return services;
    }

    /// <summary>
    /// Registers an in-process control plane instead of an HTTP one.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <param name="signer">The vendor's signing key.</param>
    /// <returns>The container, for chaining.</returns>
    /// <remarks>
    /// ADR-022's fake, and the reason the whole no-accidental-lockout suite is runnable. It is also
    /// what a demonstration runs on: <c>scripts/seed.sh</c> produces a fully licensed demo tenant with
    /// no vendor service anywhere, which is the difference between a demo that works on a laptop on a
    /// plane and one that does not.
    /// </remarks>
    public static IServiceCollection AddVumaInProcessControlPlane(
        this IServiceCollection services,
        LicenceSigner signer)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(signer);

        services.AddSingleton(signer);
        services.AddSingleton<InProcessControlPlane>();
        services.AddSingleton<IControlPlaneClient>(provider =>
            provider.GetRequiredService<InProcessControlPlane>());

        return services;
    }
}
