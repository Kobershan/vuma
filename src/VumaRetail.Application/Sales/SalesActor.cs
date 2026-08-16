using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.Sales;

/// <summary>
/// Who is authorising. Resolves the acting user from the ambient principal rather than from the
/// command.
/// </summary>
/// <remarks>
/// The same argument <c>PosActor</c> makes, restated here with a <c>SALES_</c> code rather than shared:
/// an operator who is refused a refund and reads <c>POS_NOT_AN_OPERATOR</c> learns nothing about which
/// of their two actions was wrong. Both a return and a price override are attributed to the person who
/// actually authenticated, because both are the exact thing a shrinkage investigation asks about, and a
/// user id supplied on the request is a user id the person supplying it chose.
/// </remarks>
public static class SalesActor
{
    private const string UserPrefix = "user:";

    /// <summary>The acting user's id.</summary>
    /// <param name="principal">The ambient principal.</param>
    /// <exception cref="SalesPrincipalException">The caller is not an authenticated person.</exception>
    public static Guid RequireUserId(IPrincipalAccessor principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        string value = principal.Principal;

        if (value.StartsWith(UserPrefix, StringComparison.Ordinal)
            && Guid.TryParse(value.AsSpan(UserPrefix.Length), out Guid userId))
        {
            return userId;
        }

        throw SalesPrincipalException.NotAUser(value);
    }
}

/// <summary>A sales action that has to be attributed to a person was attempted by something else.</summary>
/// <param name="code">The stable machine-readable code.</param>
/// <param name="message">What is wrong, in words.</param>
public sealed class SalesPrincipalException(string code, string message)
    : DomainException(code, message, DomainProblemKind.Forbidden)
{
    /// <summary>The caller is a system or terminal principal where a person is required.</summary>
    /// <param name="principal">What the principal actually was.</param>
    public static SalesPrincipalException NotAUser(string principal)
        => new(
            "SALES_NOT_A_USER",
            $"'{principal}' is not an authenticated user. A return and a price override are both "
            + "attributed to the person who authorised them — a background or terminal-only principal "
            + "cannot authorise either.");
}
