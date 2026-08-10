namespace VumaRetail.Domain.Primitives;

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
/// <param name="kind">What sort of failure it is, so the API edge can pick a status code.</param>
public abstract class DomainException(string code, string message, DomainProblemKind kind = DomainProblemKind.Rule)
    : Exception(message)
{
    /// <summary>The stable machine-readable code. Clients branch on this, never on the message.</summary>
    public string Code { get; } = code;

    /// <summary>What sort of failure this is. See <see cref="DomainProblemKind"/>.</summary>
    public DomainProblemKind Kind { get; } = kind;
}

/// <summary>
/// The shape of a domain failure, in the domain's own vocabulary.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not an HTTP status code. <c>CONVENTIONS.md</c> §4 keeps HTTP out of the layers below
/// the edge, and the domain has no opinion on whether it is being called over REST, from a desktop
/// process or by the sync receiver. What it does know is the difference between "that does not
/// exist", "something else already has that name" and "the rule says no" — and those three want
/// different answers from any caller, transport or not.
/// </para>
/// <para>
/// Stage 03's exception handler is the only place that turns one of these into a status code
/// (<c>docs/API_STANDARDS.md</c>). Adding a fourth kind means editing exactly one mapping.
/// </para>
/// </remarks>
public enum DomainProblemKind
{
    /// <summary>A business rule refused a well-formed request. The default; becomes <c>422</c>.</summary>
    Rule = 0,

    /// <summary>The thing being acted on does not exist, or is not this tenant's. Becomes <c>404</c>.</summary>
    NotFound = 1,

    /// <summary>Something must be unique and already is not. Becomes <c>409</c>.</summary>
    Conflict = 2,

    /// <summary>The request was malformed and the caller can fix it from the response. Becomes <c>400</c>.</summary>
    Malformed = 3,
}
