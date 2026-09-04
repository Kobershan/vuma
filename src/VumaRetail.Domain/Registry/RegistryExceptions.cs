using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Registry;

/// <summary>
/// Registry-level domain failures for the trading group module.
/// </summary>
/// <remarks>
/// Lives in <c>Domain.Registry</c>, not <c>Domain.Primitives</c>: it names
/// <see cref="CompanyLinkScope"/>, and primitives must not depend on the registry.
/// </remarks>
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
/// <remarks>
/// Carries the stable problem type <c>https://vuma.dev/problems/company-link-required</c> and the
/// two companies plus the missing scope as extensions, exactly what Stage 06e's API contract
/// promises the caller.
/// </remarks>
public sealed class CompanyLinkRequiredException : DomainException, IProblemExtensions
{
    /// <summary>The stable problem type for a missing company link.</summary>
    public const string ProblemType = "https://vuma.dev/problems/company-link-required";

    /// <summary>One side of the missing link.</summary>
    public Guid CompanyA { get; }
    /// <summary>The other side of the missing link.</summary>
    public Guid CompanyB { get; }
    /// <summary>The scope the refused operation needed.</summary>
    public CompanyLinkScope RequiredScope { get; }

    /// <summary>Creates the refusal naming both companies and the missing scope.</summary>
    public CompanyLinkRequiredException(Guid companyA, Guid companyB, CompanyLinkScope requiredScope)
        : base("COMPANY_LINK_REQUIRED", $"Companies {companyA} and {companyB} do not have an active link with scope {requiredScope}.", DomainProblemKind.Forbidden)
    {
        CompanyA = companyA;
        CompanyB = companyB;
        RequiredScope = requiredScope;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object?> ProblemExtensions => new Dictionary<string, object?>
    {
        ["companyA"] = CompanyA,
        ["companyB"] = CompanyB,
        ["requiredScope"] = RequiredScope.ToString(),
    };

    /// <inheritdoc />
    public string? ProblemTypeUrl => ProblemType;
}

/// <summary>
/// Raised when a request names a company absent from the JWT token.
/// Returns 403, not 404 (ADR-127).
/// </summary>
public sealed class ForbiddenException : DomainException
{
    /// <summary>The company absent from the token.</summary>
    public Guid CompanyId { get; }

    /// <summary>Creates the refusal naming the company absent from the token.</summary>
    public ForbiddenException(Guid companyId)
        : base("COMPANY_NOT_IN_TOKEN", $"Company {companyId} is not present in the access token.", DomainProblemKind.Forbidden)
    {
        CompanyId = companyId;
    }
}
