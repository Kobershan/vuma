using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace VumaRetail.Infrastructure.Persistence.Registry;

/// <summary>
/// Builds a registry context for <c>dotnet ef</c> at design time, when there is no host and no
/// container. Mirrors <see cref="DesignTimeDbContextFactory"/>, one level up, for the registry's own
/// separate migration chain (ADR-117).
/// </summary>
public sealed class RegistryDesignTimeDbContextFactory : IDesignTimeDbContextFactory<VumaRegistryDbContext>
{
    private const string ConnectionVariable = "VUMA_REGISTRY_MIGRATIONS_CONNECTION";

    private const string LocalDevelopmentFallback =
        "Host=localhost;Port=5432;Database=vuma_dev_registry;Username=vuma;Password=vuma";

    /// <inheritdoc />
    public VumaRegistryDbContext CreateDbContext(string[] args)
    {
        string connectionString =
            Environment.GetEnvironmentVariable(ConnectionVariable) ?? LocalDevelopmentFallback;

        DbContextOptionsBuilder<VumaRegistryDbContext> options = new();

        options.UseNpgsql(connectionString, npgsql =>
        {
            npgsql.MigrationsHistoryTable("__ef_migrations_history", Schemas.Registry);
            npgsql.MigrationsAssembly(typeof(VumaRegistryDbContext).Assembly.FullName);
        });

        options.UseSnakeCaseNamingConvention();

        return new VumaRegistryDbContext(options.Options);
    }
}
