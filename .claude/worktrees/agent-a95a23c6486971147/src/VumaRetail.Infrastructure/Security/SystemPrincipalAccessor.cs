using VumaRetail.Application.Abstractions;

namespace VumaRetail.Infrastructure.Security;

/// <summary>
/// The <see cref="IPrincipalAccessor"/> in force before Stage 02 introduces authenticated identity.
/// </summary>
/// <remarks>
/// It reports a named system principal rather than an empty string or <c>"unknown"</c>. A row whose
/// <c>created_by</c> reads <c>system:migration</c> is answerable during an investigation; one that
/// reads <c>unknown</c> is a hole in R6 that looks like data.
/// </remarks>
/// <param name="component">The component acting, for example <c>seed</c> or <c>sync-receiver</c>.</param>
public sealed class SystemPrincipalAccessor(string component = "host") : IPrincipalAccessor
{
    /// <inheritdoc />
    public string Principal { get; } = $"system:{component}";

    /// <inheritdoc />
    public Guid? TerminalId => null;

    /// <inheritdoc />
    public bool IsSystem => true;
}
