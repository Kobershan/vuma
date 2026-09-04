using VumaRetail.Domain.Registry;

#pragma warning disable CS1591

namespace VumaRetail.Domain.Primitives;

/// <summary>
/// Registry-level domain exceptions for the trading group module.
/// </summary>
public static class RegistryExceptions
{
    /// <summary>
    /// Raised when a company link is required but not found or not active with the required scope.
    /// </summary>
    public static CompanyLinkRequiredException CompanyLinkRequired(Guid companyA, Guid companyB, CompanyLinkScope requiredScope)
        => new(companyA, companyB, requiredScope);

    /// <summary>
    /// Raised when a user is granted access across different Operator IDs.
    /// </summary>
    public static InvalidOperationException CrossOperatorAccessDenied(Guid userId, Guid operatorA, Guid operatorB)
        => new($"User {userId} cannot be granted access across operators {operatorA} and {operatorB}.");

    /// <summary>
    /// Raised when a company's licence has lapsed and it drops out of links for writes.
    /// </summary>
    public static InvalidOperationException LapsedCompanyCannotWrite(Guid companyId)
        => new($"Company {companyId} has lapsed and cannot participate in write operations.");

    /// <summary>
    /// Raised when a link is attempted between companies under different operators.
    /// </summary>
    public static InvalidOperationException DifferentOperatorsCannotLink(Guid operatorA, Guid operatorB)
        => new($"Companies under operators {operatorA} and {operatorB} cannot be linked.");

    /// <summary>
    /// Raised when a new entitlement grant would exceed the limit.
    /// </summary>
    public static InvalidOperationException EntitlementLimitReached(string entitlement, int limit)
        => new($"Entitlement {entitlement} limit of {limit} reached.");

    /// <summary>
    /// Raised when a token does not carry the requested company.
    /// </summary>
    public static ForbiddenException CompanyNotInToken(Guid companyId)
        => new(companyId);
}

/// <summary>
/// Raised when two companies do not have an active link with the required scope.
/// </summary>
public sealed class CompanyLinkRequiredException : DomainException
{
    public Guid CompanyA { get; }
    public Guid CompanyB { get; }
    public CompanyLinkScope RequiredScope { get; }

    public CompanyLinkRequiredException(Guid companyA, Guid companyB, CompanyLinkScope requiredScope)
        : base("COMPANY_LINK_REQUIRED", $"Companies {companyA} and {companyB} do not have an active link with scope {requiredScope}.", DomainProblemKind.Forbidden)
    {
        CompanyA = companyA;
        CompanyB = companyB;
        RequiredScope = requiredScope;
    }
}

/// <summary>
/// Raised when a request names a company absent from the JWT token.
/// Returns 403, not 404 (ADR-127).
/// </summary>
public sealed class ForbiddenException : DomainException
{
    public Guid CompanyId { get; }

    public ForbiddenException(Guid companyId)
        : base("COMPANY_NOT_IN_TOKEN", $"Company {companyId} is not present in the access token.", DomainProblemKind.Forbidden)
    {
        CompanyId = companyId;
    }
}
