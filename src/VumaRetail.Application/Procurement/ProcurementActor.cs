using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.Procurement;

/// <summary>
/// Who is authorising. Resolves the acting user from the ambient principal rather than from the
/// command.
/// </summary>
/// <remarks>
/// The argument <c>PosActor</c> and <c>SalesActor</c> both make, restated with a
/// <c>PROCUREMENT_</c> code so a refused buyer learns which of their actions was wrong. It matters more
/// here than anywhere: approving a requisition, awarding an RFQ and releasing a supplier invoice for
/// payment are the three acts an internal fraud investigation asks about by name, and a user id supplied
/// on the request is a user id the person supplying it chose.
/// </remarks>
public static class ProcurementActor
{
    private const string UserPrefix = "user:";

    /// <summary>The acting user's id.</summary>
    /// <param name="principal">The ambient principal.</param>
    /// <exception cref="ProcurementPrincipalException">The caller is not an authenticated person.</exception>
    public static Guid RequireUserId(IPrincipalAccessor principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        string value = principal.Principal;

        if (value.StartsWith(UserPrefix, StringComparison.Ordinal)
            && Guid.TryParse(value.AsSpan(UserPrefix.Length), out Guid userId))
        {
            return userId;
        }

        throw ProcurementPrincipalException.NotAUser(value);
    }
}

/// <summary>
/// A procurement action that has to be attributed to a person was attempted by something else.
/// </summary>
/// <param name="code">The stable machine-readable code.</param>
/// <param name="message">What is wrong, in words.</param>
public sealed class ProcurementPrincipalException(string code, string message)
    : DomainException(code, message, DomainProblemKind.Forbidden)
{
    /// <summary>The caller is a system or terminal principal where a person is required.</summary>
    /// <param name="principal">What the principal actually was.</param>
    public static ProcurementPrincipalException NotAUser(string principal)
        => new(
            "PROCUREMENT_NOT_A_USER",
            $"'{principal}' is not an authenticated user. Approving a requisition, awarding a quote and "
            + "releasing a supplier invoice are each attributed to the person who authorised them — a "
            + "background or terminal-only principal cannot authorise any of them.");
}
