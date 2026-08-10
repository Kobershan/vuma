using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VumaRetail.Application.Abstractions;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Persistence.Interceptors;
using VumaRetail.Infrastructure.Security;
using VumaRetail.Infrastructure.Time;

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
