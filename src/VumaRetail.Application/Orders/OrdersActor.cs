using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.Orders;

/// <summary>
/// Who is authorising an order return. Resolves the acting user from the ambient principal rather than
/// from the command.
/// </summary>
/// <remarks>
/// The same argument <c>SalesActor</c> makes, restated with an <c>ORDERS_</c> code rather than shared:
/// an operator refused for the wrong reason should read a code that names which module refused them.
/// </remarks>
public static class OrdersActor
{
    private const string UserPrefix = "user:";

    /// <summary>The acting user's id.</summary>
    /// <param name="principal">The ambient principal.</param>
    /// <exception cref="OrdersPrincipalException">The caller is not an authenticated person.</exception>
    public static Guid RequireUserId(IPrincipalAccessor principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        string value = principal.Principal;

        if (value.StartsWith(UserPrefix, StringComparison.Ordinal)
            && Guid.TryParse(value.AsSpan(UserPrefix.Length), out Guid userId))
        {
            return userId;
        }

        throw OrdersPrincipalException.NotAUser(value);
    }
}

/// <summary>An orders action that has to be attributed to a person was attempted by something else.</summary>
/// <param name="code">The stable machine-readable code.</param>
/// <param name="message">What is wrong, in words.</param>
public sealed class OrdersPrincipalException(string code, string message)
    : DomainException(code, message, DomainProblemKind.Forbidden)
{
    /// <summary>The caller is a system or terminal principal where a person is required.</summary>
    /// <param name="principal">What the principal actually was.</param>
    public static OrdersPrincipalException NotAUser(string principal)
        => new(
            "ORDERS_NOT_A_USER",
            $"'{principal}' is not an authenticated user. Raising an order return is attributed to the "
            + "person who authorised it — a background or terminal-only principal cannot.");
}
