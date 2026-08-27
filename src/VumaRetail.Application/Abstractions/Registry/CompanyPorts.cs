using VumaRetail.Domain.Registry;
#pragma warning disable CS1591

namespace VumaRetail.Application.Abstractions.Registry;

public sealed record CompanyConnection(Guid CompanyId, Guid TenantId, string SecretReference, long SchemaVersion);
public interface ICompanyConnectionResolver
{
    Task<CompanyConnection> ResolveAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken = default);
    void Invalidate(Guid companyId);
}

public interface ICompanyContext
{
    Guid? CompanyId { get; }
    void SetCompany(Guid companyId);
    Guid RequireCompany();
}

public sealed record FanOutResult<T>(Guid CompanyId, T? Value, string? Error, DateTimeOffset AsAt)
{ public bool Succeeded => Error is null; }

public interface ICompanyFanOut
{
    Task<IReadOnlyList<FanOutResult<T>>> ReadAsync<T>(IReadOnlyCollection<Guid> companyIds, Func<Guid, CancellationToken, Task<T>> read, CancellationToken cancellationToken = default);
}

public interface ICompanyProvisioner
{
    Task<Company> ProvisionAsync(Company company, CancellationToken cancellationToken = default);
}

public interface ICompanyLifecycleService
{
    Task DeactivateAsync(Guid tenantId, Guid companyId, string reason, CancellationToken cancellationToken = default);
}
