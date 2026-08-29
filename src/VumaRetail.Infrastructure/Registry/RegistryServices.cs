using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Domain.Licensing;
using VumaRetail.Domain.Registry;
using VumaRetail.Infrastructure.Persistence;

namespace VumaRetail.Infrastructure.Registry;

public sealed class CompanyConnectionResolver(IDbContextFactory<VumaRegistryDbContext> factory, IClock clock) : ICompanyConnectionResolver
{
    private readonly ConcurrentDictionary<(Guid TenantId, Guid CompanyId), CacheEntry> _cache = new();
    private readonly ConcurrentDictionary<(Guid TenantId, Guid CompanyId), long> _generations = new();

    public Task<CompanyConnection> ResolveAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken = default)
        => ResolveAsync(tenantId, companyId, CompanyAccessMode.Write, cancellationToken);

    public async Task<CompanyConnection> ResolveAsync(Guid tenantId, Guid companyId, CompanyAccessMode access, CancellationToken cancellationToken = default)
    {
        var key = (tenantId, companyId);
        long generation = _generations.GetOrAdd(key, 0);
        if (_cache.TryGetValue(key, out var cached) && cached.Generation == generation && cached.Expires > clock.UtcNow)
            return cached.Value;

        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var company = await db.Companies.AsNoTracking().SingleOrDefaultAsync(x => x.Id == companyId && x.TenantId == tenantId, cancellationToken)
            ?? throw new InvalidOperationException("Company was not found.");

        if ((access == CompanyAccessMode.Write && !company.CanServe)
            || (access == CompanyAccessMode.Read && company.LifecycleState is not (CompanyLifecycleState.Active or CompanyLifecycleState.Deactivated))
            || !string.Equals(company.MigrationState, "Current", StringComparison.OrdinalIgnoreCase)
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
    Task<VumaRetailDbContext> CreateAsync(CompanyAccessMode access, CancellationToken cancellationToken = default);
}

/// <summary>Checks that a company is authorized and safe to serve before its database is opened.</summary>
public interface ICompanyServingGuard
{
    Task EnsureServableAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken = default);
    Task EnsureAccessibleAsync(Guid tenantId, Guid companyId, CompanyAccessMode access, CancellationToken cancellationToken = default);
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

    public Task<VumaRetailDbContext> CreateAsync(CancellationToken cancellationToken = default)
        => CreateAsync(CompanyAccessMode.Write, cancellationToken);

    public async Task<VumaRetailDbContext> CreateAsync(CompanyAccessMode access, CancellationToken cancellationToken = default)
    {
        if (_created)
            throw new InvalidOperationException("Only one company DbContext may be created per operation.");

        var companyId = context.RequireCompany();
        if (tenant.TenantId == Guid.Empty)
            throw new InvalidOperationException("An authenticated tenant is required before opening a company database.");

        await servingGuard.EnsureAccessibleAsync(tenant.TenantId, companyId, access, cancellationToken);
        var connection = access == CompanyAccessMode.Write
            ? await resolver.ResolveAsync(tenant.TenantId, companyId, cancellationToken)
            : await resolver.ResolveAsync(tenant.TenantId, companyId, access, cancellationToken);
        if (connection.TenantId != tenant.TenantId || connection.CompanyId != companyId)
            throw new InvalidOperationException("The resolved company is outside the acting tenant or context.");

        var connectionString = await secrets.ResolveAsync(connection.SecretReference, cancellationToken);
        var options = new DbContextOptionsBuilder<VumaRetailDbContext>().UseNpgsql(connectionString, n => n.MigrationsHistoryTable("__ef_migrations_history", "platform")).UseSnakeCaseNamingConvention().Options;
        _created = true;
        return new VumaRetailDbContext(options, tenant, context);
    }
}

public sealed class CompanyFanOut(IClock clock, int maxConcurrency = 4, TimeSpan? readTimeout = null) : ICompanyFanOut
{
    private static readonly TimeSpan DefaultReadTimeout = TimeSpan.FromSeconds(30);
    private readonly int _maxConcurrency = maxConcurrency > 0
        ? maxConcurrency
        : throw new ArgumentOutOfRangeException(nameof(maxConcurrency), "Concurrency must be positive.");
    private readonly TimeSpan _readTimeout = readTimeout is null || readTimeout.Value > TimeSpan.Zero
        ? readTimeout ?? DefaultReadTimeout
        : throw new ArgumentOutOfRangeException(nameof(readTimeout), "The read timeout must be positive.");

    public async Task<IReadOnlyList<FanOutResult<T>>> ReadAsync<T>(IReadOnlyCollection<Guid> companyIds, Func<Guid, CancellationToken, Task<T>> read, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(companyIds);
        ArgumentNullException.ThrowIfNull(read);

        var ids = companyIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return Array.Empty<FanOutResult<T>>();
        }

        using var gate = new SemaphoreSlim(_maxConcurrency, _maxConcurrency);
        var results = new FanOutResult<T>[ids.Length];
        var tasks = ids.Select((id, index) => ReadOneAsync(id, index, results, gate, read, cancellationToken));
        await Task.WhenAll(tasks).ConfigureAwait(false);
        return results;
    }

    private async Task ReadOneAsync<T>(Guid companyId, int index, FanOutResult<T>[] results, SemaphoreSlim gate,
        Func<Guid, CancellationToken, Task<T>> read, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_readTimeout);

            try
            {
                var value = await read(companyId, timeout.Token).ConfigureAwait(false);
                results[index] = new(companyId, value, null, clock.UtcNow);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                results[index] = new(companyId, default, "Read timed out.", clock.UtcNow);
            }
            catch (Exception)
            {
                // Provider and connection exceptions can contain secrets. The company identity is
                // already carried by the result, so callers need only an operator-safe category.
                results[index] = new(companyId, default, "Company read failed.", clock.UtcNow);
            }
        }
        finally
        {
            gate.Release();
        }
    }
}

public sealed class CompanyLifecycleService(VumaRegistryDbContext db, ICompanyConnectionResolver resolver, IPrincipalAccessor? principal = null, IClock? clock = null) : ICompanyLifecycleService
{
    public async Task DeactivateAsync(Guid tenantId, Guid companyId, string reason, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A reason is required.", nameof(reason));
        var company = await db.Companies.SingleOrDefaultAsync(x => x.Id == companyId && x.TenantId == tenantId, cancellationToken)
            ?? throw new InvalidOperationException("Company was not found.");
        string actor = principal?.Principal ?? "system:company-lifecycle";
        DateTimeOffset occurredAt = clock?.UtcNow
            ?? throw new InvalidOperationException("A clock is required for lifecycle audit timestamps.");
        CompanyLifecycleState fromState = company.LifecycleState;
        if (!company.Deactivate(actor, reason, occurredAt)) return;
        db.CompanyLifecycleAudits.Add(CompanyLifecycleAudit.Record(tenantId, companyId, fromState,
            CompanyLifecycleState.Deactivated, actor, reason, occurredAt));
        await db.CommitAsync(cancellationToken);
        resolver.Invalidate(companyId);
    }
}

/// <summary>Re-drives pending saga legs after an isolated company restore.</summary>
public sealed class RegistrySagaRedriver(VumaRegistryDbContext db, IUnitOfWork unitOfWork, IClock clock) : IRegistrySagaRedriver
{
    public async Task<int> RedriveAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("A tenant is required.", nameof(tenantId));
        if (companyId == Guid.Empty) throw new ArgumentException("A company is required.", nameof(companyId));

        var intents = await db.SagaIntents
            .Include(intent => intent.Legs)
            .Where(intent => intent.TenantId == tenantId && intent.Legs.Any(leg => leg.CompanyId == companyId
                && leg.State != SagaLegState.Acknowledged && leg.State != SagaLegState.Compensated))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        int queued = 0;
        foreach (var intent in intents)
        {
            foreach (var leg in intent.Legs.Where(leg => leg.CompanyId == companyId
                && leg.State is not (SagaLegState.Acknowledged or SagaLegState.Compensated)))
            {
                string key = $"saga-leg:{intent.Id:D}:{leg.LegId:D}";
                if (await db.RegistryOutboxMessages.AnyAsync(message => message.TenantId == tenantId
                    && message.IdempotencyKey == key, cancellationToken).ConfigureAwait(false)) continue;
                db.RegistryOutboxMessages.Add(new RegistryOutboxMessage(tenantId, "saga.leg.redrive",
                    JsonSerializer.Serialize(new { intentId = intent.Id, legId = leg.LegId, companyId }),
                    clock.UtcNow, key, intent.OperationStamp));
                queued++;
            }
        }
        if (queued > 0) await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
        return queued;
    }
}

/// <summary>Small adapter seam for the physical database and seed operations.</summary>
/// <summary>Runs provisioning steps in order and only publishes an active registry row last.</summary>
public sealed class CompanyProvisioner : ICompanyProvisioner
{
    private readonly VumaRegistryDbContext _db;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IReadOnlyList<ICompanyProvisioningStep> _steps;
    private readonly ICompanyConnectionResolver _resolver;
    private readonly VumaRetail.Application.Abstractions.Licensing.IEntitlementService? _entitlements;

    public CompanyProvisioner(
        VumaRegistryDbContext db,
        IUnitOfWork unitOfWork,
        IEnumerable<ICompanyProvisioningStep> steps,
        ICompanyConnectionResolver resolver,
        VumaRetail.Application.Abstractions.Licensing.IEntitlementService? entitlements = null)
    {
        _db = db;
        _unitOfWork = unitOfWork;
        // DI registration order is the workflow order. The order is deliberately explicit because
        // database creation, migration, seed and registration are not interchangeable operations.
        _steps = steps.ToArray();
        _resolver = resolver;
        _entitlements = entitlements;
    }

    public async Task<Company> ProvisionAsync(Company company, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(company);

        // Re-drive the registry row when a caller retries after a lost response. A company ID is the
        // idempotency key for this application command; never create a second row for the same ID.
        var persisted = await _db.Companies.SingleOrDefaultAsync(
            x => x.Id == company.Id && x.TenantId == company.TenantId, cancellationToken);
        if (persisted is not null)
            company = persisted;
        else if (await _db.Companies.IgnoreQueryFilters().AnyAsync(x => x.Id == company.Id, cancellationToken))
            throw new InvalidOperationException("COMPANY_TENANT_MISMATCH");
        else
            _db.Companies.Add(company);

        if (company.LifecycleState == CompanyLifecycleState.Active)
            return company;

        if (_entitlements is not null)
        {
            var count = await _db.Companies.CountAsync(x => x.TenantId == company.TenantId && x.Id != company.Id, cancellationToken);
            var limit = await _entitlements.CheckLimitAsync(LimitKind.Stores, count + 1, cancellationToken);
            if (limit.Exceeded)
                throw new InvalidOperationException("COMPANY_LIMIT_EXCEEDED");
        }

        foreach (var step in _steps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsAlreadyComplete(company, step))
                continue;

            try
            {
                await step.ExecuteAsync(company, cancellationToken);
                company.RecordProvisioningProgress(step.Name);
                company.SetLifecycle(step.CompletedState);
                await _unitOfWork.CommitAsync(cancellationToken);
                _resolver.Invalidate(company.Id);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                company.RecordProvisioningFailure("Provisioning step timed out.");
                await _unitOfWork.CommitAsync(CancellationToken.None);
                throw;
            }
            catch (Exception)
            {
                company.RecordProvisioningFailure("Provisioning step failed.");
                await _unitOfWork.CommitAsync(CancellationToken.None);
                throw;
            }
        }

        if (string.IsNullOrWhiteSpace(company.ConnectionSecretRef))
            throw new InvalidOperationException("Provisioning did not register a connection secret reference.");
        if (company.LifecycleState != CompanyLifecycleState.Registered)
            throw new InvalidOperationException("Provisioning did not register the company connection.");
        company.SetLifecycle(CompanyLifecycleState.Active, isActive: true);
        await _unitOfWork.CommitAsync(cancellationToken);
        _resolver.Invalidate(company.Id);
        return company;
    }

    private static bool IsAlreadyComplete(Company company, ICompanyProvisioningStep step)
        => company.LifecycleState > step.CompletedState
            || (company.LifecycleState == step.CompletedState &&
                string.Equals(company.ProvisioningStep, step.Name, StringComparison.OrdinalIgnoreCase));
}
