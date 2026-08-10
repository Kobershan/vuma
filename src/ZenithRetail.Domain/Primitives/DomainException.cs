namespace ZenithRetail.Domain.Primitives;

/// <summary>
/// A business rule was broken. Expected, and mapped to <c>422</c> at the API edge.
/// </summary>
/// <remarks>
/// <para>
/// <c>CONVENTIONS.md</c> §5: every API error carries a <b>stable machine-readable code</b>. The code
/// is the contract a client branches on; the message is for a human and may be reworded freely, in
/// any release, without anybody's integration breaking. That is why <see cref="Code"/> is required
/// rather than derived from the exception type name — a rename would otherwise be a breaking change
/// nobody noticed making.
/// </para>
/// <para>
/// Infrastructure failures are not these. A timeout, a dropped connection or a disk error propagates
/// and is logged once, centrally, by the pipeline.
/// </para>
/// </remarks>
/// <param name="code">The stable machine-readable code, <c>SCREAMING_SNAKE_CASE</c>.</param>
/// <param name="message">The human-readable explanation.</param>
public abstract class DomainException(string code, string message) : Exception(message)
{
    /// <summary>The stable machine-readable code. Clients branch on this, never on the message.</summary>
    public string Code { get; } = code;
}
