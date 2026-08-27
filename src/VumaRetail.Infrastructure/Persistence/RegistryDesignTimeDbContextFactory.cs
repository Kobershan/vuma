using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace VumaRetail.Infrastructure.Persistence;

/// <summary>Builds the registry context for EF migration commands.</summary>
public sealed class RegistryDesignTimeDbContextFactory : IDesignTimeDbContextFactory<VumaRegistryDbContext>
{
    public VumaRegistryDbContext CreateDbContext(string[] args)
    {
        string connectionString = Environment.GetEnvironmentVariable("VUMA_REGISTRY_MIGRATIONS_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=vuma_registry_dev;Username=vuma;Password=vuma";

        DbContextOptionsBuilder<VumaRegistryDbContext> options = new();
        options.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsHistoryTable("__ef_migrations_history", "registry"));
        options.UseSnakeCaseNamingConvention();
        return new VumaRegistryDbContext(options.Options);
    }
}
