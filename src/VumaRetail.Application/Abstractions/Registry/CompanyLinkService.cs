using VumaRetail.Domain.Registry;

namespace VumaRetail.Application.Abstractions.Registry;

/// <summary>Links between companies, scoped and checked at every cross-company entry point.</summary>
public interface ICompanyLinkService
{
    /// <summary>Throws if the two companies do not have an active link with the required scope.</summary>
    Task RequireLink(Guid companyA, Guid companyB, CompanyLinkScope requiredScope, CancellationToken cancellationToken = default);
    
    /// <summary>Attempts to find an active link.</summary>
    Task<CompanyLink?> TryGetLinkAsync(Guid companyA, Guid companyB, CancellationToken cancellationToken = default);
    
    /// <summary>Lists all links for a company.</summary>
    Task<IReadOnlyList<CompanyLink>> GetLinksFor(Guid companyId, CancellationToken cancellationToken = default);
    
    /// <summary>Proposes a link between two companies (must be accepted by both).</summary>
    Task<CompanyLink> ProposeAsync(Guid companyA, Guid companyB, CompanyLinkScope scopes, CancellationToken cancellationToken = default);
    
    /// <summary>Accepts a proposed link.</summary>
    Task AcceptAsync(Guid linkId, Guid acceptingCompanyId, CancellationToken cancellationToken = default);
    
    /// <summary>Suspends an active link.</summary>
    Task SuspendAsync(Guid linkId, string reason, CancellationToken cancellationToken = default);
    
    /// <summary>Revokes a link.</summary>
    Task RevokeAsync(Guid linkId, string reason, CancellationToken cancellationToken = default);
}
