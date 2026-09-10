namespace VumaRetail.Application.Abstractions.Registry;

/// <summary>
/// Reads the tenant's company register: which companies exist and which one acts. Single-company
/// callers (an order ships from one location, in one company) resolve their company here instead of
/// guessing it — several active companies is a refusal, never a pick-first.
/// </summary>
public interface ICompanyDirectory
{
    /// <summary>
    /// The tenant's single active company.
    /// </summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The company id.</returns>
    /// <exception cref="InvalidOperationException">No active company, or more than one — name one explicitly.</exception>
    Task<Guid> RequireSingleActiveAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
