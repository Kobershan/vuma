using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Sync;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Persistence.Interceptors;
using VumaRetail.Infrastructure.Security;
using VumaRetail.Infrastructure.Time;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Infrastructure.Registry;

namespace VumaRetail.Infrastructure.DependencyInjection;

/// <summary>Registers the persistence layer with the host's container.</summary>
public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="VumaRetailDbContext"/>, the audit interceptor and the ambient
    /// clock, principal and tenant services.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <returns>The container, for chaining.</returns>
    /// <remarks>
    /// Hosts call this and nothing else. The interceptor is registered here rather than left to each
    /// host because a host that forgets it gets a database with no audit trail and no soft-delete
    /// rewriting — and everything still appears to work, which is the worst way for it to fail.
    /// </remarks>
    public static IServiceCollection AddVumaPersistence(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<ITenantContext, AmbientTenantContext>();
        services.TryAddPrincipalAccessor();

        services.TryAddReplicationScope();
        services.AddScoped<AuditStamper>();
        services.AddScoped<AuditInterceptor>();

        services.AddDbContext<VumaRetailDbContext>((provider, options) =>
        {
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", Schemas.Platform));

            // CONVENTIONS.md §2: PostgreSQL folds unquoted identifiers to lower case, so a PascalCase
            // model means quoted identifiers in every hand-written query, backup script and psql
            // session for the life of the product.
            options.UseSnakeCaseNamingConvention();

            options.AddInterceptors(provider.GetRequiredService<AuditInterceptor>());
        });

        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<VumaRetailDbContext>());

        return services;
    }

    /// <summary>Registers the registry context against its separate registry database.</summary>
    public static IServiceCollection AddVumaRegistryPersistence(
        this IServiceCollection services,
        string registryConnectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(registryConnectionString);

        services.AddDbContext<VumaRegistryDbContext>((_, options) =>
        {
            options.UseNpgsql(registryConnectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "registry"));
            options.UseSnakeCaseNamingConvention();
        }, optionsLifetime: ServiceLifetime.Singleton);
        services.AddDbContextFactory<VumaRegistryDbContext>((_, options) =>
        { options.UseNpgsql(registryConnectionString, n => n.MigrationsHistoryTable("__ef_migrations_history", "registry")); options.UseSnakeCaseNamingConvention(); });
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<VumaRegistryDbContext>());
        services.AddScoped<ICompanyContext, AmbientCompanyContext>();
        services.AddScoped<ICompanyConnectionResolver, CompanyConnectionResolver>();
        services.AddScoped<ICompanyFanOut, CompanyFanOut>();
        services.AddScoped<ICompanyLifecycleService, CompanyLifecycleService>();
        services.AddScoped<IRegistrySagaRedriver, RegistrySagaRedriver>();
        services.AddScoped<ICompanyProvisioner, CompanyProvisioner>();
        services.AddScoped<ICompanyDbContextFactory, CompanyDbContextFactory>();
        services.AddScoped<ICompanyConnectionSecretStore, UnconfiguredCompanyConnectionSecretStore>();
        services.AddScoped<ICompanyMigrationRunner, CompanyMigrationRunner>();
        services.AddScoped<ICompanyServingGuard, CompanyServingGuard>();

        // Stage 06d: Group services
        services.AddScoped<ISagaCoordinator, SagaCoordinator>();
        services.AddScoped<IGroupCreditService, GroupCreditService>();
        services.AddScoped<IBarcodeResolver, BarcodeResolver>();
        services.AddScoped<IGroupReadStore, GroupReadStore>();

        // Stage 06e: Trading group
        services.AddScoped<ICompanyLinkService, CompanyLinkService>();
        services.AddScoped<IOperatorContext, OperatorContext>();
        services.AddScoped<IPremisesService, PremisesService>();
        services.AddScoped<IRegistryUserService, RegistryUserService>();
        services.AddScoped<ITerminalService, TerminalService>();
        services.AddScoped<IEntitlementCounters, EntitlementCounters>();

        return services;
    }

    /// <summary>
    /// Registers the replication scope, which the audit stamper reads to tell a locally-made change
    /// from one applied on a peer's instruction.
    /// </summary>
    /// <remarks>
    /// Registered here, with try-add semantics, rather than only in <c>AddVumaSync</c>: a host that
    /// wires persistence without sync — a migration run, a test that only touches the database —
    /// still constructs the stamper, and a missing registration would be a startup failure in the
    /// one code path nobody exercises before shipping. Where sync <em>is</em> wired, that call
    /// registers the same implementation first and this is a no-op.
    /// </remarks>
    private static void TryAddReplicationScope(this IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(IReplicationScope)))
        {
            return;
        }

        services.AddScoped<IReplicationScope, Sync.ReplicationScope>();
    }

    private static void TryAddPrincipalAccessor(this IServiceCollection services)
    {
        // Stage 02 replaces this with one backed by the authenticated identity. Registered with
        // TryAdd semantics so that replacement is a registration, not an edit to this file.
        if (services.Any(descriptor => descriptor.ServiceType == typeof(IPrincipalAccessor)))
        {
            return;
        }

        services.AddScoped<IPrincipalAccessor>(_ => new SystemPrincipalAccessor());
    }
}
