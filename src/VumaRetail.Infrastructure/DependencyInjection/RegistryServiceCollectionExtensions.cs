using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Persistence.Registry;
using VumaRetail.Infrastructure.Security;

namespace VumaRetail.Infrastructure.DependencyInjection;

/// <summary>Registers the tenant's registry database with the host's container (Stage 06c).</summary>
public static class RegistryServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="VumaRegistryDbContext"/> and the connection-secret protector.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <param name="registryConnectionString">The registry database's own PostgreSQL connection string.</param>
    /// <param name="connectionSecretOptions">The key that encrypts company connection strings at rest.</param>
    /// <returns>The container, for chaining.</returns>
    public static IServiceCollection AddVumaRegistry(
        this IServiceCollection services,
        string registryConnectionString,
        ConnectionSecretOptions connectionSecretOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(registryConnectionString);
        ArgumentNullException.ThrowIfNull(connectionSecretOptions);

        services.AddDbContext<VumaRegistryDbContext>(options =>
        {
            options.UseNpgsql(registryConnectionString, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__ef_migrations_history", Schemas.Registry);
                npgsql.MigrationsAssembly(typeof(VumaRegistryDbContext).Assembly.FullName);
            });

            // CONVENTIONS.md §2, same reasoning as AddVumaPersistence: PostgreSQL folds unquoted
            // identifiers to lower case.
            options.UseSnakeCaseNamingConvention();
        });

        services.AddSingleton(connectionSecretOptions);
        services.AddSingleton<IConnectionSecretProtector, AesGcmConnectionSecretProtector>();

        return services;
    }
}
