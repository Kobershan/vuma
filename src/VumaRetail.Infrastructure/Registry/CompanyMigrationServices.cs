using System.Collections.Concurrent;
#pragma warning disable EF1002
using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Domain.Registry;
using VumaRetail.Infrastructure.Persistence;

namespace VumaRetail.Infrastructure.Registry;

public sealed record CompanyMigrationResult(Guid CompanyId, bool Succeeded, string? Error, long SchemaVersion);

public interface ICompanyMigrationRunner
{
    Task<IReadOnlyList<CompanyMigrationResult>> MigrateAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

public sealed class CompanyMigrationRunner(
    VumaRegistryDbContext registry,
    ICompanyConnectionSecretStore secrets,
    ICompanyConnectionResolver resolver,
    IUnitOfWork unitOfWork) : ICompanyMigrationRunner
{
    public async Task<IReadOnlyList<CompanyMigrationResult>> MigrateAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        await registry.Database.MigrateAsync(cancellationToken);
        var companies = await registry.Companies.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken);
        using var gate = new SemaphoreSlim(4);
        var results = new ConcurrentBag<CompanyMigrationResult>();
        await Task.WhenAll(companies.Select(async company =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                var connection = await resolver.ResolveAsync(tenantId, company.Id, cancellationToken);
                var connectionString = await secrets.ResolveAsync(connection.SecretReference, cancellationToken);
                var options = new DbContextOptionsBuilder<VumaRetailDbContext>().UseNpgsql(connectionString).Options;
                await using var context = new VumaRetailDbContext(options, new FixedTenantContext(tenantId));
                await context.Database.MigrateAsync(cancellationToken);
                var tables = await context.Database.SqlQueryRaw<(string Schema, string Name)>("SELECT table_schema AS \"Schema\", table_name AS \"Name\" FROM information_schema.tables WHERE table_schema NOT IN ('pg_catalog','information_schema','registry') AND table_type='BASE TABLE'").ToListAsync(cancellationToken);
                foreach (var table in tables)
                    await context.Database.ExecuteSqlRawAsync($"UPDATE \"{table.Schema.Replace("\"", "\"\"")}\".\"{table.Name.Replace("\"", "\"\"")}\" SET company_id = '{company.Id}' WHERE company_id IS NULL", cancellationToken);
                company.SetMigration(connection.SchemaVersion, "Current");
                results.Add(new(company.Id, true, null, connection.SchemaVersion));
            }
            catch (Exception)
            {
                company.SetMigration(company.SchemaVersion, "Pending");
                results.Add(new(company.Id, false, "Company migration failed.", company.SchemaVersion));
            }
            finally { gate.Release(); }
            resolver.Invalidate(company.Id);
        }));
        await unitOfWork.CommitAsync(cancellationToken);
        return results.ToArray();
    }

    private sealed class FixedTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId; public Guid? StoreId => null; public bool IsFilterBypassed => false;
        public void SetTenant(Guid id, Guid? storeId = null) { }
        public IDisposable BypassTenantFilter(string reason) => new Scope();
        private sealed class Scope : IDisposable { public void Dispose() { } }
    }
}

public sealed class CompanyServingGuard(VumaRegistryDbContext registry) : ICompanyServingGuard
{
    public async Task EnsureServableAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken = default)
    {
        var company = await registry.Companies.AsNoTracking().SingleOrDefaultAsync(x => x.Id == companyId && x.TenantId == tenantId, cancellationToken);
        if (company is null || !company.CanServe) throw new InvalidOperationException("COMPANY_NOT_SERVABLE");
        if (!string.Equals(company.MigrationState, "Current", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("COMPANY_MIGRATION_REQUIRED");
    }
}
