using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using VumaRetail.Application.Abstractions;

namespace VumaRetail.Infrastructure.Persistence;

/// <summary>
/// Builds a context for <c>dotnet ef</c> at design time, when there is no host and no container.
/// </summary>
/// <remarks>
/// <para>
/// The connection string comes from <c>VUMA_MIGRATIONS_CONNECTION</c> when it is set, and otherwise
/// falls back to a local development database. Scaffolding a migration does not connect — the
/// provider only needs to know it is Npgsql to generate the right SQL — so the fallback being wrong
/// on somebody's machine costs nothing until they actually run <c>database update</c>.
/// </para>
/// <para>
/// Design time has no tenant, which is correct rather than a gap: a migration is DDL, and DDL is not
/// tenant-scoped.
/// </para>
/// </remarks>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<VumaRetailDbContext>
{
    private const string ConnectionVariable = "VUMA_MIGRATIONS_CONNECTION";

    private const string LocalDevelopmentFallback =
        "Host=localhost;Port=5432;Database=vuma_dev;Username=vuma;Password=vuma";

    /// <inheritdoc />
    public VumaRetailDbContext CreateDbContext(string[] args)
    {
        string connectionString =
            Environment.GetEnvironmentVariable(ConnectionVariable) ?? LocalDevelopmentFallback;

        DbContextOptionsBuilder<VumaRetailDbContext> options = new();

        options.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsHistoryTable("__ef_migrations_history", Schemas.Platform));
        options.UseSnakeCaseNamingConvention();

        return new VumaRetailDbContext(options.Options, new DesignTimeTenantContext());
    }

    private sealed class DesignTimeTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;

        public Guid? StoreId => null;

        public bool IsFilterBypassed => true;

        public void SetTenant(Guid tenantId, Guid? storeId = null)
        {
        }

        public IDisposable BypassTenantFilter(string reason) => new NoOpScope();

        private sealed class NoOpScope : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
