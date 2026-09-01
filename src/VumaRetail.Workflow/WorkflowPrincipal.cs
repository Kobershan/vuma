using VumaRetail.Domain.Primitives;

namespace VumaRetail.Workflow;

/// <summary>Recovers a user id from an <c>IPrincipalAccessor.Principal</c> string.</summary>
/// <remarks>
/// Shaped <c>user:{id}</c> for a person (<c>docs/SECURITY.md</c> §1). Several workflow operations —
/// deciding an approval, reading your own notification inbox — only make sense for a signed-in person,
/// never for a terminal or a system principal, and this is where that boundary is drawn once.
/// </remarks>
internal static class WorkflowPrincipal
{
    private const string UserPrefix = "user:";

    /// <summary>Parses a user id out of a principal string, or throws.</summary>
    /// <param name="principalValue">The principal, from <c>IPrincipalAccessor.Principal</c>.</param>
    /// <exception cref="PrincipalMustBeAUserException">The principal is not a signed-in person.</exception>
    public static Guid RequireUserId(string principalValue)
        => TryGetUserId(principalValue, out Guid userId)
            ? userId
            : throw new PrincipalMustBeAUserException();

    /// <summary>Parses a user id out of a principal string, without throwing.</summary>
    /// <param name="principalValue">The principal, from <c>IPrincipalAccessor.Principal</c>.</param>
    /// <param name="userId">The parsed id, if the principal is a person.</param>
    public static bool TryGetUserId(string principalValue, out Guid userId)
    {
        if (principalValue.StartsWith(UserPrefix, StringComparison.Ordinal)
            && Guid.TryParse(principalValue[UserPrefix.Length..], out userId))
        {
            return true;
        }

        userId = Guid.Empty;
        return false;
    }
}

/// <summary>Raised when an operation that only makes sense for a signed-in person is attempted by something else.</summary>
public sealed class PrincipalMustBeAUserException()
    : DomainException(
        "WORKFLOW_PRINCIPAL_MUST_BE_A_USER",
        "Only a signed-in person can perform this action.",
        DomainProblemKind.Forbidden);
