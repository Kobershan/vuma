using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Domain.Registry;
using VumaRetail.Infrastructure.Persistence;

namespace VumaRetail.Infrastructure.Registry;

public sealed class CompanyConnectionResolver(IDbContextFactory<VumaRegistryDbContext> factory, IClock clock) : ICompanyConnectionResolver
{
    private readonly ConcurrentDictionary<Guid, (CompanyConnection Value, DateTimeOffset Expires)> _cache = new();
    public async Task<CompanyConnection> ResolveAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(companyId, out var cached) && cached.Expires > clock.UtcNow && cached.Value.TenantId == tenantId) return cached.Value;
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var company = await db.Companies.AsNoTracking().SingleOrDefaultAsync(x => x.Id == companyId && x.TenantId == tenantId, cancellationToken)
            ?? throw new InvalidOperationException("Company was not found.");
        if (!company.CanServe || string.IsNullOrWhiteSpace(company.ConnectionSecretRef)) throw new InvalidOperationException("Company is not available for business operations.");
        var value = new CompanyConnection(company.Id, company.TenantId, company.ConnectionSecretRef, company.SchemaVersion);
        _cache[companyId] = (value, clock.UtcNow.AddMinutes(5)); return value;
    }
    public void Invalidate(Guid companyId) => _cache.TryRemove(companyId, out _);
}

public sealed class AmbientCompanyContext : ICompanyContext
{
    private Guid? _companyId;
    public Guid? CompanyId => _companyId;
    public void SetCompany(Guid companyId) => _companyId = companyId;
    public Guid RequireCompany() => _companyId ?? throw new InvalidOperationException("An acting company is required.");
}

/// <summary>Creates one business context for the already selected acting company.</summary>
public interface ICompanyDbContextFactory
{
    Task<VumaRetailDbContext> CreateAsync(CancellationToken cancellationToken = default);
}

internal sealed class UnconfiguredCompanyConnectionSecretStore : ICompanyConnectionSecretStore
{
    public Task<string> ResolveAsync(string secretReference, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("No company connection secret store is configured.");
}

public sealed class CompanyDbContextFactory(ICompanyContext context, ICompanyConnectionResolver resolver, ICompanyConnectionSecretStore secrets, VumaRetail.Application.Abstractions.ITenantContext tenant) : ICompanyDbContextFactory
{
    public async Task<VumaRetailDbContext> CreateAsync(CancellationToken cancellationToken = default)
    {
        var companyId = context.RequireCompany();
        var connection = await resolver.ResolveAsync(tenant.TenantId, companyId, cancellationToken);
        var connectionString = await secrets.ResolveAsync(connection.SecretReference, cancellationToken);
        var options = new DbContextOptionsBuilder<VumaRetailDbContext>().UseNpgsql(connectionString, n => n.MigrationsHistoryTable("__ef_migrations_history", "platform")).UseSnakeCaseNamingConvention().Options;
        return new VumaRetailDbContext(options, tenant);
    }
}

public sealed class CompanyFanOut(IClock clock) : ICompanyFanOut
{
    public async Task<IReadOnlyList<FanOutResult<T>>> ReadAsync<T>(IReadOnlyCollection<Guid> companyIds, Func<Guid, CancellationToken, Task<T>> read, CancellationToken cancellationToken = default)
    {
        var ids = companyIds.Distinct().ToArray(); var gate = new SemaphoreSlim(4); var results = new ConcurrentBag<FanOutResult<T>>();
        await Task.WhenAll(ids.Select(async id => { await gate.WaitAsync(cancellationToken); try { results.Add(new(id, await read(id, cancellationToken), null, clock.UtcNow)); } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { results.Add(new(id, default, "Timed out", clock.UtcNow)); } catch (Exception ex) { results.Add(new(id, default, ex.Message, clock.UtcNow)); } finally { gate.Release(); } }));
        return ids.Select(id => results.Single(x => x.CompanyId == id)).ToArray();
    }
}

public sealed class CompanyLifecycleService(VumaRegistryDbContext db, IUnitOfWork unitOfWork) : ICompanyLifecycleService
{
    public async Task DeactivateAsync(Guid tenantId, Guid companyId, string reason, CancellationToken cancellationToken = default)
    { if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A reason is required.", nameof(reason)); var company = await db.Companies.SingleOrDefaultAsync(x => x.Id == companyId && x.TenantId == tenantId, cancellationToken) ?? throw new InvalidOperationException("Company was not found."); company.Deactivate(); await unitOfWork.CommitAsync(cancellationToken); }
}

/// <summary>Small adapter seam for the physical database and seed operations.</summary>
public interface ICompanyProvisioningStep
{
    string Name { get; }
    Task ExecuteAsync(Company company, CancellationToken cancellationToken);
}

/// <summary>Runs provisioning steps in order and only publishes an active registry row last.</summary>
public sealed class CompanyProvisioner(VumaRegistryDbContext db, IUnitOfWork unitOfWork, IEnumerable<ICompanyProvisioningStep> steps) : ICompanyProvisioner
{
    public async Task<Company> ProvisionAsync(Company company, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(company);
        foreach (var step in steps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await step.ExecuteAsync(company, cancellationToken);
            company.SetMigration(company.SchemaVersion, step.Name);
            await unitOfWork.CommitAsync(cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(company.ConnectionSecretRef))
            throw new InvalidOperationException("Provisioning did not register a connection secret reference.");
        company.SetLifecycle(CompanyLifecycleState.Active, isActive: true);
        await unitOfWork.CommitAsync(cancellationToken);
        return company;
    }
}
