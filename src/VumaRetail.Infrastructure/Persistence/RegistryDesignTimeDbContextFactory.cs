using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using VumaRetail.Application.Abstractions;

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
        return new VumaRegistryDbContext(options.Options, new DesignTimeTenantContext());
    }

    private sealed class DesignTimeTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public Guid? StoreId => null;
        public bool IsFilterBypassed => true;
        public void SetTenant(Guid tenantId, Guid? storeId = null) { }
        public IDisposable BypassTenantFilter(string reason) => new Scope();
        private sealed class Scope : IDisposable { public void Dispose() { } }
    }
}
