#pragma warning disable EF1002
using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Domain.Registry;
using VumaRetail.Domain.Primitives;
using VumaRetail.Infrastructure.Persistence;

namespace VumaRetail.Infrastructure.Registry;

public sealed record CompanyMigrationResult(Guid CompanyId, bool Succeeded, string? Error, long SchemaVersion);

public interface ICompanyMigrationRunner
{
    Task<IReadOnlyList<CompanyMigrationResult>> MigrateAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

public sealed class CompanyMigrationRunner : ICompanyMigrationRunner
{
    private const int DefaultMaxConcurrency = 4;
    private readonly VumaRegistryDbContext _registry;
    private readonly ICompanyConnectionSecretStore _secrets;
    private readonly IUnitOfWork _unitOfWork;
    private readonly int _maxConcurrency;

    public CompanyMigrationRunner(
        VumaRegistryDbContext registry,
        ICompanyConnectionSecretStore secrets,
        IUnitOfWork unitOfWork,
        int maxConcurrency = DefaultMaxConcurrency)
    {
        if (maxConcurrency <= 0) throw new ArgumentOutOfRangeException(nameof(maxConcurrency));
        _registry = registry;
        _secrets = secrets;
        _unitOfWork = unitOfWork;
        _maxConcurrency = maxConcurrency;
    }

    public async Task<IReadOnlyList<CompanyMigrationResult>> MigrateAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        // The registry is the control plane. It must be healthy before any company is touched.
        await _registry.Database.MigrateAsync(cancellationToken);
        var companies = await _registry.Companies
            .Where(x => x.TenantId == tenantId)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

        // Persist the unsafe state before fan-out. A process crash after this point therefore
        // leaves every company unservable and makes the next run resume rather than guess.
        foreach (var company in companies)
        {
            company.SetMigration(company.SchemaVersion, "Pending");
        }
        await _unitOfWork.CommitAsync(cancellationToken);

        using var gate = new SemaphoreSlim(_maxConcurrency, _maxConcurrency);
        var tasks = companies.Select(company => MigrateCompanyAsync(company, tenantId, gate, cancellationToken));
        var results = await Task.WhenAll(tasks);

        // Only this context writes registry state, serially, after all independent databases have
        // completed. This avoids using one DbContext concurrently from migration workers.
        foreach (var result in results)
        {
            var company = companies.Single(x => x.Id == result.CompanyId);
            company.SetMigration(result.SchemaVersion, result.Succeeded ? "Current" : "Pending");
            if (!result.Succeeded)
                company.RecordProvisioningFailure(result.Error ?? "Company migration failed.");
        }
        await _unitOfWork.CommitAsync(cancellationToken);
        return results;
    }

    private async Task<CompanyMigrationResult> MigrateCompanyAsync(
        Company company,
        Guid tenantId,
        SemaphoreSlim gate,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        string? pendingMigration = null;
        try
        {
            if (string.IsNullOrWhiteSpace(company.ConnectionSecretRef))
                return new(company.Id, false, "Company has no registered database connection.", company.SchemaVersion);

            var connectionString = await _secrets.ResolveAsync(company.ConnectionSecretRef, cancellationToken);
            var options = new DbContextOptionsBuilder<VumaRetailDbContext>()
                .UseNpgsql(connectionString, n => n.MigrationsHistoryTable("__ef_migrations_history", Schemas.Platform))
                .UseSnakeCaseNamingConvention()
                .Options;
            await using var context = new VumaRetailDbContext(options, new FixedTenantContext(tenantId));
            pendingMigration = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).FirstOrDefault();
            await context.Database.MigrateAsync(cancellationToken);
            var applied = (await context.Database.GetAppliedMigrationsAsync(cancellationToken)).ToArray();
            return new(company.Id, true, null, MigrationVersion(applied.LastOrDefault()));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Do not expose provider text or connection diagnostics. The first pending ID is enough
            // for an operator to identify the deployment that needs attention.
            string suffix = pendingMigration is null ? string.Empty : $" '{pendingMigration}'";
            return new(company.Id, false, $"Company migration failed; retry pending migration{suffix}.", company.SchemaVersion);
        }
        finally
        {
            gate.Release();
        }
    }

    private static long MigrationVersion(string? migrationId)
        => migrationId is not null && long.TryParse(migrationId[..Math.Min(14, migrationId.Length)], out var version)
            ? version
            : 0;

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
        await EnsureAccessibleAsync(tenantId, companyId, CompanyAccessMode.Write, cancellationToken);
    }

    public async Task EnsureAccessibleAsync(Guid tenantId, Guid companyId, CompanyAccessMode access, CancellationToken cancellationToken = default)
    {
        var company = await registry.Companies.AsNoTracking().SingleOrDefaultAsync(x => x.Id == companyId && x.TenantId == tenantId, cancellationToken);
        if (company is null) throw new InvalidOperationException("COMPANY_NOT_FOUND");
        if (access == CompanyAccessMode.Write && !company.CanServe) throw new CompanyReadOnlyException(company.LifecycleState);
        if (access == CompanyAccessMode.Read && company.LifecycleState is not (CompanyLifecycleState.Active or CompanyLifecycleState.Deactivated))
            throw new InvalidOperationException("COMPANY_NOT_SERVABLE");
        if (!string.Equals(company.MigrationState, "Current", StringComparison.OrdinalIgnoreCase))
        {
            string detail = string.IsNullOrWhiteSpace(company.ProvisioningError)
                ? "a pending migration"
                : company.ProvisioningError;
            throw new InvalidOperationException(
                $"COMPANY_MIGRATION_REQUIRED: company '{company.Code}' is not served until {detail}");
        }
    }
}

public sealed class CompanyReadOnlyException(CompanyLifecycleState state)
    : DomainException("COMPANY_READ_ONLY", $"The company is {state.ToString().ToLowerInvariant()} and does not accept business writes.", DomainProblemKind.Forbidden);
