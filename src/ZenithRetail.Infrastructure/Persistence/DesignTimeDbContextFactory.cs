using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using ZenithRetail.Application.Abstractions;

namespace ZenithRetail.Infrastructure.Persistence;

/// <summary>
/// Builds a context for <c>dotnet ef</c> at design time, when there is no host and no container.
/// </summary>
/// <remarks>
/// <para>
/// The connection string comes from <c>ZENITH_MIGRATIONS_CONNECTION</c> when it is set, and otherwise
/// falls back to a local development database. Scaffolding a migration does not connect — the
/// provider only needs to know it is Npgsql to generate the right SQL — so the fallback being wrong
/// on somebody's machine costs nothing until they actually run <c>database update</c>.
/// </para>
/// <para>
/// Design time has no tenant, which is correct rather than a gap: a migration is DDL, and DDL is not
/// tenant-scoped.
/// </para>
/// </remarks>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ZenithRetailDbContext>
{
    private const string ConnectionVariable = "ZENITH_MIGRATIONS_CONNECTION";

    private const string LocalDevelopmentFallback =
        "Host=localhost;Port=5432;Database=zenith_dev;Username=zenith;Password=zenith";

    /// <inheritdoc />
    public ZenithRetailDbContext CreateDbContext(string[] args)
    {
        string connectionString =
            Environment.GetEnvironmentVariable(ConnectionVariable) ?? LocalDevelopmentFallback;

        DbContextOptionsBuilder<ZenithRetailDbContext> options = new();

        options.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsHistoryTable("__ef_migrations_history", Schemas.Platform));
        options.UseSnakeCaseNamingConvention();

        return new ZenithRetailDbContext(options.Options, new DesignTimeTenantContext());
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
