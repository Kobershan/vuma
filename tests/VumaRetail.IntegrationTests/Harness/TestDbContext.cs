using System.Reflection;
using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Abstractions;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Persistence.Interceptors;

namespace VumaRetail.IntegrationTests.Harness;

/// <summary>
/// Builds <see cref="VumaRetailDbContext"/> over a test database, with a controllable clock,
/// principal and tenant.
/// </summary>
/// <remarks>
/// A factory rather than a subclass. EF resolves both the migrations assembly and the model snapshot
/// from the <em>runtime</em> context type, so a <c>TestDbContext : VumaRetailDbContext</c> would be
/// a different context as far as migrations are concerned and would report the entire model as
/// pending changes. Constructing the production type directly also means these tests exercise the
/// class that ships, not a near relative of it.
/// </remarks>
public static class TestDbContextFactory
{
    /// <summary>Builds a context over the given database.</summary>
    /// <param name="connectionString">The test database.</param>
    /// <param name="clock">The clock the audit interceptor stamps from. Defaults to a fixed instant.</param>
    /// <param name="principal">Who is acting. Defaults to the test principal.</param>
    /// <param name="tenant">The tenant the query filter scopes to. Defaults to bypassed.</param>
    /// <param name="stamper">
    /// The audit stamper the interceptor uses. Supply the one the outbox behaviour holds so both
    /// see the same per-save marks.
    /// </param>
    public static VumaRetailDbContext For(
        string connectionString,
        IClock? clock = null,
        IPrincipalAccessor? principal = null,
        ITenantContext? tenant = null,
        AuditStamper? stamper = null)
        => new(
            BuildOptions<VumaRetailDbContext>(connectionString, clock, principal, stamper),
            tenant ?? TestTenantContext.Unfiltered());

    /// <summary>Builds a registry context over the given database.</summary>
    /// <param name="connectionString">The test database.</param>
    /// <param name="tenant">The tenant the query filter scopes to. Defaults to bypassed.</param>
    /// <remarks>
    /// Mirrors the host's registry registration: the <c>registry</c> migrations history table,
    /// snake-case naming, and no audit interceptor — the registry context ships none, so the
    /// tests exercise the class that ships, not a near relative of it.
    /// </remarks>
    public static VumaRegistryDbContext ForRegistry(
        string connectionString,
        ITenantContext? tenant = null)
    {
        DbContextOptionsBuilder<VumaRegistryDbContext> options = new();

        options.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsHistoryTable("__ef_migrations_history", "registry"));
        options.UseSnakeCaseNamingConvention();

        return new(options.Options, tenant ?? TestTenantContext.Unfiltered());
    }

    /// <summary>Builds the options every test context shares.</summary>
    /// <typeparam name="TContext">The context type the options are for.</typeparam>
    /// <param name="connectionString">The test database.</param>
    /// <param name="clock">The clock the audit interceptor stamps from.</param>
    /// <param name="principal">Who is acting.</param>
    internal static DbContextOptions BuildOptions<TContext>(
        string connectionString,
        IClock? clock = null,
        IPrincipalAccessor? principal = null,
        AuditStamper? stamper = null)
        where TContext : DbContext
    {
        DbContextOptionsBuilder<TContext> options = new();

        options.UseNpgsql(connectionString, npgsql =>
        {
            npgsql.MigrationsHistoryTable("__ef_migrations_history", Schemas.Platform);
            npgsql.MigrationsAssembly(typeof(VumaRetailDbContext).Assembly.FullName);
        });
        options.UseSnakeCaseNamingConvention();

        // The stamper may be supplied so a test can share the one instance the outbox behaviour
        // uses. Sharing matters: stamping is idempotent per save only because both callers see the
        // same set of already-stamped entities.
        options.AddInterceptors(new AuditInterceptor(
            stamper ?? new AuditStamper(
                clock ?? new TestClock(),
                principal ?? new TestPrincipalAccessor(),
                new VumaRetail.Infrastructure.Sync.ReplicationScope())));

        return options.Options;
    }
}

/// <summary>
/// The production context plus the mapping probe, for the mapping-conformance tests only.
/// </summary>
/// <remarks>
/// <para>
/// Separate from the production context because the probe is not in any migration. Databases for it
/// are built with <c>EnsureCreated</c> from the model, which is exactly the question these tests ask:
/// what do the mapping conventions actually produce in PostgreSQL, for a shape (<c>Money</c>,
/// <c>Quantity</c>) that no module has a table for until Stage 07.
/// </para>
/// <para>
/// Because it never runs a migration, EF never compares it to the snapshot, so the probe cannot make
/// the model-versus-migrations assertion in <c>MigrationTests</c> lie.
/// </para>
/// </remarks>
public sealed class ProbeDbContext : VumaRetailDbContext
{
    private ProbeDbContext(DbContextOptions options, ITenantContext tenantContext)
        : base(options, tenantContext)
    {
    }

    /// <inheritdoc />
    protected override IReadOnlyCollection<Assembly> AdditionalModelAssemblies => [typeof(ProbeDbContext).Assembly];

    /// <summary>Builds a probe context over the given database.</summary>
    /// <param name="connectionString">The test database.</param>
    /// <param name="clock">The clock the audit interceptor stamps from.</param>
    /// <param name="principal">Who is acting.</param>
    /// <param name="tenant">The tenant the query filter scopes to. Defaults to bypassed.</param>
    public static ProbeDbContext ForProbe(
        string connectionString,
        IClock? clock = null,
        IPrincipalAccessor? principal = null,
        ITenantContext? tenant = null)
        => new(
            TestDbContextFactory.BuildOptions<ProbeDbContext>(connectionString, clock, principal),
            tenant ?? TestTenantContext.Unfiltered());
}
