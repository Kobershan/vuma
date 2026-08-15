using VumaRetail.Domain.Platform;

namespace VumaRetail.Application.Platform;

/// <summary>Reads and writes <see cref="Store"/> rows.</summary>
/// <remarks>
/// Repositories return tracked entities and never commit. The unit of work is the pipeline's
/// (<c>IUnitOfWork</c>, CLAUDE.md §7 rule 2).
/// </remarks>
public interface IStoreRepository
{
    /// <summary>Finds a store by id.</summary>
    /// <param name="storeId">The store.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<Store?> FindAsync(Guid storeId, CancellationToken cancellationToken = default);

    /// <summary>Finds a store by its short code.</summary>
    /// <param name="code">The code as typed; comparison is case-insensitive because <see cref="Store"/>
    /// upper-cases its own.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<Store?> FindByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>Adds a new store.</summary>
    /// <param name="store">The store to add.</param>
    void Add(Store store);
}

/// <summary>Reads <see cref="Tenant"/> rows.</summary>
/// <remarks>
/// Read-only, and deliberately so: a tenant is created by activation (Stage 04b) and edited through
/// the admin surface, neither of which goes through this port. Stage 09 added it because a receipt is
/// a tax document — a VAT invoice must carry the seller's registration number — and
/// <see cref="Tenant.TaxRegistrationNumber"/> is the only place that lives.
/// </remarks>
public interface ITenantRepository
{
    /// <summary>Finds a tenant by id.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<Tenant?> FindAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
