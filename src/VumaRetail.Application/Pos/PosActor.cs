using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.Pos;

/// <summary>
/// Who is at the till. Resolves the acting operator and terminal from the ambient principal rather
/// than from the command.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not a field on any POS command. A cashier who could name the operator on the request
/// could ring a sale up as somebody else, and every control in this module — the void permission, the
/// reprint log, the cash-up the operator signs for — rests on the operator being the person who
/// actually authenticated. <c>IPrincipalAccessor</c> is populated from the authenticated identity at
/// the request edge (Stage 02), which is the only place that knows.
/// </para>
/// <para>
/// The terminal is the same argument with a device certificate behind it instead of a password.
/// </para>
/// </remarks>
public static class PosActor
{
    private const string UserPrefix = "user:";

    /// <summary>The acting operator's user id.</summary>
    /// <param name="principal">The ambient principal.</param>
    /// <exception cref="PosPrincipalException">The caller is not an authenticated person.</exception>
    public static Guid RequireUserId(IPrincipalAccessor principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        string value = principal.Principal;

        if (value.StartsWith(UserPrefix, StringComparison.Ordinal)
            && Guid.TryParse(value.AsSpan(UserPrefix.Length), out Guid userId))
        {
            return userId;
        }

        throw PosPrincipalException.NotAnOperator(value);
    }

    /// <summary>The terminal the action came from.</summary>
    /// <param name="principal">The ambient principal.</param>
    /// <exception cref="PosPrincipalException">The action did not come from a registered terminal.</exception>
    public static Guid RequireTerminalId(IPrincipalAccessor principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        return principal.TerminalId
            ?? throw PosPrincipalException.NotATerminal();
    }
}

/// <summary>A till operation was attempted by something that is not a person at a terminal.</summary>
/// <param name="code">The stable machine-readable code.</param>
/// <param name="message">What is wrong, in words.</param>
public sealed class PosPrincipalException(string code, string message)
    : DomainException(code, message, DomainProblemKind.Forbidden)
{
    /// <summary>The caller is a system or terminal principal where a person is required.</summary>
    /// <param name="principal">What the principal actually was.</param>
    public static PosPrincipalException NotAnOperator(string principal)
        => new(
            "POS_NOT_AN_OPERATOR",
            $"'{principal}' is not an authenticated operator. Every sale, void and cash-up is attributed "
            + "to the person who signed in — a background or terminal-only principal cannot ring one up.");

    /// <summary>The action did not arrive from a registered terminal.</summary>
    public static PosPrincipalException NotATerminal()
        => new(
            "POS_NOT_A_TERMINAL",
            "This action must come from a registered terminal. A till session belongs to a physical "
            + "drawer, and a request with no terminal has no drawer to count.");
}
