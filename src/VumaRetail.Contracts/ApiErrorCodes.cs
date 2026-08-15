namespace VumaRetail.Contracts;

/// <summary>
/// The transport-level error codes every Vuma API can return, whatever module is being called.
/// </summary>
/// <remarks>
/// <para>
/// <c>CONVENTIONS.md</c> §5: the code is the contract and the message is for humans. A client
/// branches on these and never on the <c>detail</c> text, which is reworded whenever it reads badly.
/// </para>
/// <para>
/// These are the codes the platform itself produces. Business codes — <c>IDENTITY_PIN_TAKEN</c>,
/// <c>LICENCE_READ_ONLY</c> — come from the module that raised them, on the same terms. They live
/// with their exception rather than in a central list, because a central list of four hundred codes
/// is a file nobody keeps accurate.
/// </para>
/// <para>
/// This type is in <c>Contracts</c> so the desktop shell and the Android app can both compile
/// against it (ADR-008). The public API host has its own, by ADR-021.
/// </para>
/// </remarks>
public static class ApiErrorCodes
{
    /// <summary>The request was malformed. The <c>errors</c> extension says which properties.</summary>
    public const string ValidationFailed = "VALIDATION_FAILED";

    /// <summary>
    /// The request never reached a handler: a required query or route parameter was missing or
    /// unparseable, or the body could not be read.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="ValidationFailed"/>, and the distinction is the useful part for a
    /// client. <c>VALIDATION_FAILED</c> means the request bound successfully and a validator rejected
    /// a value, so there is an <c>errors</c> extension naming the properties. This one means binding
    /// itself failed, so there is nothing to enumerate — the caller has sent something the endpoint
    /// could not read at all.
    /// </remarks>
    public const string MalformedRequest = "MALFORMED_REQUEST";

    /// <summary>No usable credential was presented, or it has expired.</summary>
    public const string Unauthenticated = "UNAUTHENTICATED";

    /// <summary>Authenticated, but without the permission the endpoint requires (ADR-013).</summary>
    public const string Forbidden = "FORBIDDEN";

    /// <summary>Nothing here — or nothing this tenant can see, which is answered the same way.</summary>
    public const string NotFound = "NOT_FOUND";

    /// <summary>The verb is not allowed on this resource.</summary>
    public const string MethodNotAllowed = "METHOD_NOT_ALLOWED";

    /// <summary>Something must be unique and already is not.</summary>
    public const string Conflict = "CONFLICT";

    /// <summary>A business rule refused a well-formed request.</summary>
    public const string RuleViolation = "RULE_VIOLATION";

    /// <summary>
    /// Something failed on the server. Deliberately opaque: the response carries a correlation id
    /// and nothing else, and support asks for that id.
    /// </summary>
    public const string InternalError = "INTERNAL_ERROR";

    /// <summary>
    /// A sign-in attempt failed. One code for every cause — no such user, wrong password, wrong PIN,
    /// locked out, deactivated — because distinguishing them turns the endpoint into an oracle
    /// (<c>docs/SECURITY.md</c> §1).
    /// </summary>
    public const string InvalidCredentials = "AUTH_INVALID_CREDENTIALS";
}
