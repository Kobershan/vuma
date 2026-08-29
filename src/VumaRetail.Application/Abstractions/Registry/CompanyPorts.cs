using VumaRetail.Domain.Registry;
#pragma warning disable CS1591

namespace VumaRetail.Application.Abstractions.Registry;

public sealed record CompanyConnection(Guid CompanyId, Guid TenantId, string SecretReference, long SchemaVersion);
public enum CompanyAccessMode { Write, Read }
public interface ICompanyConnectionResolver
{
    Task<CompanyConnection> ResolveAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken = default);
    Task<CompanyConnection> ResolveAsync(Guid tenantId, Guid companyId, CompanyAccessMode access, CancellationToken cancellationToken = default);
    void Invalidate(Guid companyId);
}

public interface ICompanyContext
{
    Guid? CompanyId { get; }
    void SetCompany(Guid companyId);
    Guid RequireCompany();
}

/// <summary>Resolves encrypted company connection details without exposing them to callers.</summary>
public interface ICompanyConnectionSecretStore
{
    Task<string> ResolveAsync(string secretReference, CancellationToken cancellationToken = default);
}

/// <summary>The outcome of one independent company read.</summary>
/// <remarks>
/// <para><see cref="CompanyId"/> is the stable identity used to correlate a result, including a
/// failure, with the authorized company set. <see cref="Error"/> is an operator-safe failure category;
/// it must not contain provider, connection-string, or exception details.</para>
/// <para><see cref="AsAt"/> is captured when this company read completes (or fails), so a caller can
/// disclose that sibling results were observed at different instants.</para>
/// </remarks>
public sealed record FanOutResult<T>(Guid CompanyId, T? Value, string? Error, DateTimeOffset AsAt)
{
    public bool Succeeded => Error is null;
}

public interface ICompanyFanOut
{
    /// <summary>
    /// Reads each company independently with bounded concurrency.
    /// </summary>
    /// <param name="companyIds">
    /// The already tenant- and permission-filtered company set. Callers must not pass raw request
    /// IDs here; authorization belongs before this orchestration boundary.
    /// </param>
    /// <param name="read">A one-company read. It must not open another company context.</param>
    /// <param name="cancellationToken">Cancels the complete fan-out.</param>
    /// <remarks>
    /// A failure in one company is returned as a result for that company. Cancellation requested by
    /// the caller is different: it aborts the whole fan-out and is never converted to a business
    /// failure result.
    /// </remarks>
    Task<IReadOnlyList<FanOutResult<T>>> ReadAsync<T>(IReadOnlyCollection<Guid> companyIds, Func<Guid, CancellationToken, Task<T>> read, CancellationToken cancellationToken = default);
}

public interface ICompanyProvisioner
{
    Task<Company> ProvisionAsync(Company company, CancellationToken cancellationToken = default);
}

/// <summary>A resumable provisioning step. Implementations must be safe to run again.</summary>
public interface ICompanyProvisioningStep
{
    string Name { get; }
    /// <summary>The lifecycle state guaranteed after this step succeeds.</summary>
    CompanyLifecycleState CompletedState { get; }
    Task ExecuteAsync(Company company, CancellationToken cancellationToken = default);
}

/// <summary>Creates the isolated database for a company, or confirms an existing one.</summary>
public interface ICompanyDatabaseCreator
{
    Task CreateAsync(Company company, CancellationToken cancellationToken = default);
}

/// <summary>Applies the company database migration chain. Implementations must be re-runnable.</summary>
public interface ICompanyDatabaseMigrator
{
    Task MigrateAsync(Company company, CancellationToken cancellationToken = default);
}

/// <summary>Seeds company-owned defaults. Implementations must upsert by stable seed keys.</summary>
public interface ICompanyDataSeeder
{
    Task SeedAsync(Company company, CancellationToken cancellationToken = default);
}

/// <summary>Stores company connection details and returns only its encrypted secret reference.</summary>
public interface ICompanyConnectionRegistrar
{
    Task<string> RegisterAsync(Company company, CancellationToken cancellationToken = default);
}

public interface ICompanyLifecycleService
{
    Task DeactivateAsync(Guid tenantId, Guid companyId, string reason, CancellationToken cancellationToken = default);
}

/// <summary>Requeues the durable legs belonging to one restored company.</summary>
public interface IRegistrySagaRedriver
{
    Task<int> RedriveAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken = default);
}
