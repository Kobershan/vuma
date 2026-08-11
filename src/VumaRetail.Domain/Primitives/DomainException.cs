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

    /// <summary>
    /// The caller is known and the request is well formed, and they may not do it anyway. Becomes
    /// <c>403</c>.
    /// </summary>
    /// <remarks>
    /// Added in Stage 04b for the two licensing refusals — <c>LICENCE_READ_ONLY</c> and
    /// <c>LICENCE_MODULE_NOT_ENABLED</c> — which <c>LICENSING.md</c> §4 and §6 both specify as 403.
    /// Neither is a rule violation (422 would tell a till its sale was malformed) and neither is an
    /// authorisation failure in the ADR-013 sense, so the domain needed a fourth word for it.
    /// </remarks>
    Forbidden = 4,
}

/// <summary>
/// A domain exception that carries structured fields the API edge should put on the problem document.
/// </summary>
/// <remarks>
/// <para>
/// The error <em>code</em> is the contract and the message is for humans — but a read-only refusal has
/// to hand the UI an amount due and a working payment link, or the licence screen can only show a dead
/// end (<c>LICENSING.md</c> Rule 3). Those are neither a code nor a message.
/// </para>
/// <para>
/// Deliberately generic. The alternative is a table in the exception handler mapping error codes to
/// extra fields, which is exactly the growing central list <see cref="DomainException"/>'s own doc
/// comment argues against — one interface keeps the mapping in one place while letting each module
/// decide what it needs to say.
/// </para>
/// </remarks>
public interface IProblemExtensions
{
    /// <summary>
    /// Fields to merge into the <c>ProblemDetails</c> extensions. Must contain nothing a caller is not
    /// entitled to see: this is serialised into the response body.
    /// </summary>
    IReadOnlyDictionary<string, object?> ProblemExtensions { get; }
}
