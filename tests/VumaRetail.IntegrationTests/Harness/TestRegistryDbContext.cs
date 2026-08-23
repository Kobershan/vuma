using Microsoft.EntityFrameworkCore;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Persistence.Registry;

namespace VumaRetail.IntegrationTests.Harness;

/// <summary>
/// Builds <see cref="VumaRegistryDbContext"/> over a test database. Mirrors
/// <see cref="TestDbContextFactory"/>, one level up, for the registry's own separate migration chain
/// (ADR-117).
/// </summary>
public static class TestRegistryDbContextFactory
{
    /// <summary>Builds a context over the given database.</summary>
    /// <param name="connectionString">The test database.</param>
    public static VumaRegistryDbContext For(string connectionString)
    {
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
