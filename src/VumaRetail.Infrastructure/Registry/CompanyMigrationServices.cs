using System.Collections.Concurrent;
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
    ICompanyConnectionResolver resolver) : ICompanyMigrationRunner
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
                company.SetMigration(connection.SchemaVersion, "Current");
                results.Add(new(company.Id, true, null, connection.SchemaVersion));
            }
            catch (Exception exception)
            {
                company.SetMigration(company.SchemaVersion, "Pending");
                results.Add(new(company.Id, false, "Company migration failed.", company.SchemaVersion));
            }
            finally { gate.Release(); }
        }));
        await registry.SaveChangesAsync(cancellationToken);
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

public interface ICompanyServingGuard
{
    Task EnsureServableAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken = default);
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
