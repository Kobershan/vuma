using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Abstractions;

namespace VumaRetail.Infrastructure.Persistence;

/// <summary>The registry database's connection string, as resolved at host startup.</summary>
/// <param name="ConnectionString">The Npgsql connection string for the tenant's registry database.</param>
public sealed record RegistryDatabaseOptions(string ConnectionString);

/// <summary>
/// Creates registry contexts that see the ambient tenant — the scoped replacement for the
/// framework's singleton <c>IDbContextFactory</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>VumaRegistryDbContext</c> takes the scoped <c>ITenantContext</c> for its tenant query
/// filters. A singleton factory resolves that from the root provider: every context it creates
/// then carries an empty tenant and matches nothing (or throws outright under scope validation).
/// This factory is scoped, so the ambient tenant flows into each created context instead.
/// </para>
/// <para>
/// Each call returns an independent context the caller owns and disposes — the fan-out's parallel
/// company reads depend on that — while the tenant comes from the ambient scope, not from a
/// parameter, so a caller cannot quietly query as the wrong tenant.
/// </para>
/// </remarks>
public sealed class ScopedRegistryDbContextFactory(
    RegistryDatabaseOptions options,
    ITenantContext tenant) : IDbContextFactory<VumaRegistryDbContext>
{
    /// <inheritdoc />
    public VumaRegistryDbContext CreateDbContext()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ConnectionString);

        var builder = new DbContextOptionsBuilder<VumaRegistryDbContext>();
        builder.UseNpgsql(
            options.ConnectionString,
            npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "registry"));
        builder.UseSnakeCaseNamingConvention();

        return new VumaRegistryDbContext(builder.Options, tenant);
    }

    /// <inheritdoc />
    public Task<VumaRegistryDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(CreateDbContext());
}
