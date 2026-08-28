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
    private readonly ConcurrentDictionary<(Guid TenantId, Guid CompanyId), CacheEntry> _cache = new();
    private readonly ConcurrentDictionary<(Guid TenantId, Guid CompanyId), long> _generations = new();

    public async Task<CompanyConnection> ResolveAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken = default)
    {
        var key = (tenantId, companyId);
        long generation = _generations.GetOrAdd(key, 0);
        if (_cache.TryGetValue(key, out var cached) && cached.Generation == generation && cached.Expires > clock.UtcNow)
            return cached.Value;

        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var company = await db.Companies.AsNoTracking().SingleOrDefaultAsync(x => x.Id == companyId && x.TenantId == tenantId, cancellationToken)
            ?? throw new InvalidOperationException("Company was not found.");

        if (!company.CanServe || !string.Equals(company.MigrationState, "Current", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(company.ConnectionSecretRef))
            throw new InvalidOperationException("Company is not available for business operations.");

        var value = new CompanyConnection(company.Id, company.TenantId, company.ConnectionSecretRef, company.SchemaVersion);
        if (_generations.TryGetValue(key, out long currentGeneration) && currentGeneration == generation)
            _cache[key] = new(value, clock.UtcNow.AddMinutes(5), generation);
        return value;
    }

    public void Invalidate(Guid companyId)
    {
        var keys = _cache.Keys.Concat(_generations.Keys)
            .Where(key => key.CompanyId == companyId)
            .Distinct()
            .ToArray();
        foreach (var key in keys)
        {
            _cache.TryRemove(key, out _);
            _generations.AddOrUpdate(key, 1, static (_, generation) => generation + 1);
        }
    }

    private sealed record CacheEntry(CompanyConnection Value, DateTimeOffset Expires, long Generation);
}

public sealed class AmbientCompanyContext : ICompanyContext
{
    private Guid? _companyId;
    public Guid? CompanyId => _companyId;
    public void SetCompany(Guid companyId)
    {
        if (companyId == Guid.Empty)
            throw new ArgumentException("A company is required.", nameof(companyId));

        if (_companyId is { } current)
        {
            if (current != companyId)
                throw new InvalidOperationException("The acting company cannot change within an operation.");

            throw new InvalidOperationException("The acting company is already bound.");
        }

        _companyId = companyId;
    }

    public Guid RequireCompany() => _companyId
        ?? throw new InvalidOperationException("An acting company is required.");
}

/// <summary>Creates one business context for the already selected acting company.</summary>
public interface ICompanyDbContextFactory
{
    Task<VumaRetailDbContext> CreateAsync(CancellationToken cancellationToken = default);
}

/// <summary>Checks that a company is authorized and safe to serve before its database is opened.</summary>
public interface ICompanyServingGuard
{
    Task EnsureServableAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken = default);
}

internal sealed class UnconfiguredCompanyConnectionSecretStore : ICompanyConnectionSecretStore
{
    public Task<string> ResolveAsync(string secretReference, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("No company connection secret store is configured.");
}

public sealed class CompanyDbContextFactory(
    ICompanyContext context,
    ICompanyConnectionResolver resolver,
    ICompanyConnectionSecretStore secrets,
    VumaRetail.Application.Abstractions.ITenantContext tenant,
    ICompanyServingGuard servingGuard) : ICompanyDbContextFactory
{
    private bool _created;

    public async Task<VumaRetailDbContext> CreateAsync(CancellationToken cancellationToken = default)
    {
        if (_created)
            throw new InvalidOperationException("Only one company DbContext may be created per operation.");

        var companyId = context.RequireCompany();
        if (tenant.TenantId == Guid.Empty)
            throw new InvalidOperationException("An authenticated tenant is required before opening a company database.");

        await servingGuard.EnsureServableAsync(tenant.TenantId, companyId, cancellationToken);
        var connection = await resolver.ResolveAsync(tenant.TenantId, companyId, cancellationToken);
        if (connection.TenantId != tenant.TenantId || connection.CompanyId != companyId)
            throw new InvalidOperationException("The resolved company is outside the acting tenant or context.");

        var connectionString = await secrets.ResolveAsync(connection.SecretReference, cancellationToken);
        var options = new DbContextOptionsBuilder<VumaRetailDbContext>().UseNpgsql(connectionString, n => n.MigrationsHistoryTable("__ef_migrations_history", "platform")).UseSnakeCaseNamingConvention().Options;
        _created = true;
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

public sealed class CompanyLifecycleService(VumaRegistryDbContext db, IUnitOfWork unitOfWork, ICompanyConnectionResolver resolver) : ICompanyLifecycleService
{
    public async Task DeactivateAsync(Guid tenantId, Guid companyId, string reason, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A reason is required.", nameof(reason));
        var company = await db.Companies.SingleOrDefaultAsync(x => x.Id == companyId && x.TenantId == tenantId, cancellationToken)
            ?? throw new InvalidOperationException("Company was not found.");
        company.Deactivate();
        await unitOfWork.CommitAsync(cancellationToken);
        resolver.Invalidate(companyId);
    }
}

/// <summary>Small adapter seam for the physical database and seed operations.</summary>
public interface ICompanyProvisioningStep
{
    string Name { get; }
    Task ExecuteAsync(Company company, CancellationToken cancellationToken);
}

/// <summary>Runs provisioning steps in order and only publishes an active registry row last.</summary>
public sealed class CompanyProvisioner(VumaRegistryDbContext db, IUnitOfWork unitOfWork, IEnumerable<ICompanyProvisioningStep> steps, ICompanyConnectionResolver resolver) : ICompanyProvisioner
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
            resolver.Invalidate(company.Id);
        }

        if (string.IsNullOrWhiteSpace(company.ConnectionSecretRef))
            throw new InvalidOperationException("Provisioning did not register a connection secret reference.");
        company.SetLifecycle(CompanyLifecycleState.Active, isActive: true);
        await unitOfWork.CommitAsync(cancellationToken);
        resolver.Invalidate(company.Id);
        return company;
    }
}
