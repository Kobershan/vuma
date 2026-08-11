using VumaRetail.Domain.Licensing;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.Abstractions.Licensing;

/// <summary>
/// Where a tenant stands, and everything the licence screen needs to say about it.
/// </summary>
/// <param name="Level">What is refused.</param>
/// <param name="Reason">Why.</param>
/// <param name="Notice">What is shown.</param>
/// <param name="EvaluatedAt">The instant the decision was made against, UTC.</param>
/// <param name="RestrictedSince">When the current restriction began, UTC, if one has.</param>
/// <param name="NextEscalationAt">When the next rung is reached if nothing changes, UTC.</param>
/// <param name="AmountDue">What is owed, if the control plane said.</param>
/// <param name="PayUrl">Where to pay from inside the product.</param>
/// <param name="UpdatePaymentMethodUrl">Where to fix an expired card.</param>
/// <param name="SupportPhone">The vendor's number.</param>
/// <param name="Messages">Anything else the vendor wants shown.</param>
public sealed record EnforcementDecision(
    EnforcementLevel Level,
    EnforcementReason Reason,
    NoticeStage Notice,
    DateTimeOffset EvaluatedAt,
    DateTimeOffset? RestrictedSince = null,
    DateTimeOffset? NextEscalationAt = null,
    Money? AmountDue = null,
    string? PayUrl = null,
    string? UpdatePaymentMethodUrl = null,
    string? SupportPhone = null,
    IReadOnlyList<string>? Messages = null)
{
    /// <summary>True when writes are refused.</summary>
    public bool IsReadOnly => Level is EnforcementLevel.ReadOnly;

    /// <summary>Everything is fine. The state a store spends its life in.</summary>
    /// <param name="at">The instant the decision was made against, UTC.</param>
    public static EnforcementDecision Normal(DateTimeOffset at)
        => new(EnforcementLevel.Normal, EnforcementReason.None, NoticeStage.None, at);
}

/// <summary>
/// The one place anything asks whether a tenant may do something (<c>LICENSING.md</c> §6).
/// </summary>
/// <remarks>
/// <para>
/// A single choke point, deliberately. Module navigation, endpoints and desktop commands all gate
/// through this service and nothing gates itself — an architecture test fails the build on a module
/// that reads a licence or an enforcement level directly, because a second implementation of "are we
/// allowed to" is a second set of edge cases and only one of them gets fixed.
/// </para>
/// <para>
/// <b>Read paths must never call <see cref="CurrentLevel"/>.</b> ADR-028 promises a lapsed tenant full
/// read, report, reprint and export access, so a query that consults the enforcement level is a bug by
/// construction — there is no answer it could act on that would be correct. There is a test for that
/// too.
/// </para>
/// </remarks>
public interface IEntitlementService
{
    /// <summary>
    /// True when the tenant's plan includes a module.
    /// </summary>
    /// <param name="module">The module's licence flag, from its manifest.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <remarks>
    /// An unlicensed module is invisible in navigation and answers <c>403</c> with
    /// <c>LICENCE_MODULE_NOT_ENABLED</c> and an upgrade reference at its endpoints, so the UI can offer
    /// the next step instead of a dead end.
    /// </remarks>
    Task<bool> IsModuleEnabledAsync(string module, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks a quantity against the plan.
    /// </summary>
    /// <param name="kind">Which limit.</param>
    /// <param name="proposedValue">The value after the change the caller is about to make.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>What the plan says about it.</returns>
    /// <remarks>
    /// Call at <b>configuration time only</b> — registering a terminal, creating a user — never in a
    /// transaction path. That is a usability rule before it is a performance one: nothing a cashier
    /// does mid-sale may hit a limit, whatever state the tenant is in.
    /// </remarks>
    Task<LimitCheck> CheckLimitAsync(
        LimitKind kind,
        long proposedValue,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Where the tenant sits on the enforcement ladder.
    /// </summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <remarks>
    /// For the read-only guard and the licence screen. Not for a query handler — see the type's
    /// remarks, and the architecture test that enforces them.
    /// </remarks>
    Task<EnforcementDecision> CurrentLevel(CancellationToken cancellationToken = default);
}

/// <summary>What the plan says about a proposed quantity.</summary>
/// <param name="Kind">Which limit.</param>
/// <param name="Ceiling">The plan's ceiling.</param>
/// <param name="Proposed">The value that was checked.</param>
/// <param name="IsHard">Whether exceeding it refuses or merely warns.</param>
/// <param name="Exceeded">Whether the proposed value is above the ceiling.</param>
public readonly record struct LimitCheck(
    LimitKind Kind,
    long Ceiling,
    long Proposed,
    bool IsHard,
    bool Exceeded)
{
    /// <summary>True when the caller must refuse: the limit is hard and it is exceeded.</summary>
    public bool MustRefuse => IsHard && Exceeded;

    /// <summary>True when the caller should warn but carry on: a soft limit at or past 80%.</summary>
    public bool ShouldWarn => !IsHard && Ceiling > 0 && Ceiling != long.MaxValue
        && Proposed * 100 >= Ceiling * 80;

    /// <summary>Throws when a hard limit is exceeded, naming the plan's ceiling.</summary>
    /// <exception cref="LicenceLimitExceededException">The limit is hard and exceeded.</exception>
    public void ThrowIfExceeded()
    {
        if (MustRefuse)
        {
            throw new LicenceLimitExceededException(Kind, Ceiling, Proposed);
        }
    }
}

/// <summary>
/// A module's declaration of the licence flag that switches it on (R7).
/// </summary>
/// <remarks>
/// Declared next to the module's code and registered at startup, exactly like
/// <c>IModulePermissions</c>. An architecture test asserts that every module declaring permissions also
/// declares a manifest, so a module cannot ship without saying how it is licensed — which is the
/// failure that ends with a paid-for module that nobody can switch off.
/// </remarks>
public interface IModuleManifest
{
    /// <summary>The module, matching the first segment of its permission keys.</summary>
    string Module { get; }

    /// <summary>The entitlement flag a licence carries to enable it.</summary>
    string LicenceFlag { get; }

    /// <summary>What the module is, in a sentence a shop owner recognises on an invoice.</summary>
    string Description { get; }

    /// <summary>
    /// True for modules that are part of the platform and are never sold separately — identity, sync,
    /// backup, licensing itself.
    /// </summary>
    /// <remarks>
    /// A tenant with a licence that somehow omitted <c>identity</c> would be a tenant who cannot sign
    /// in to fix it. Core modules are enabled whatever the licence says, and the abuse queue is where a
    /// licence with a missing core flag gets noticed.
    /// </remarks>
    bool IsCore => false;
}

/// <summary>
/// A command whose writes belong to a session that was already open (<c>LICENSING.md</c> §4).
/// </summary>
/// <remarks>
/// The open-session carve-out. If read-only falls due while a till has a sale on screen or a cash-up
/// unclosed, that sale and that cash-up complete and <em>then</em> the terminal goes read-only —
/// because the alternative is a drawer of unrecorded cash and a customer holding goods, which is a
/// mess the vendor created to make a billing point.
/// </remarks>
public interface ISessionScopedCommand
{
    /// <summary>The POS session or cash-up this command belongs to.</summary>
    Guid SessionId { get; }
}

/// <summary>
/// Which sessions were open when a restriction fell due, and may therefore still be finished.
/// </summary>
/// <remarks>
/// Stage 09 opens and closes real till sessions against this; Stage 04b builds the mechanism, wires it
/// into the guard and tests it, because retrofitting a carve-out into an enforcement path is how a
/// carve-out ends up being half-applied.
/// </remarks>
public interface IOpenSessionRegistry
{
    /// <summary>Records that a session has opened.</summary>
    /// <param name="sessionId">The session.</param>
    /// <param name="at">When, UTC.</param>
    void Open(Guid sessionId, DateTimeOffset at);

    /// <summary>Records that a session has closed. It gets no further carve-out.</summary>
    /// <param name="sessionId">The session.</param>
    void Close(Guid sessionId);

    /// <summary>
    /// True when a session was open before the restriction began and may still be completed.
    /// </summary>
    /// <param name="sessionId">The session.</param>
    /// <param name="restrictedSince">When the restriction began, UTC.</param>
    bool MayCarryOn(Guid sessionId, DateTimeOffset? restrictedSince);
}

/// <summary>Raised when a write is refused because the tenant is read-only (ADR-028).</summary>
/// <remarks>
/// Carries the amount due and a working payment link on the problem document, because a refusal that
/// only says "no" turns every recovery into a phone call — which is the failure mode
/// <c>LICENSING.md</c> Rule 3 exists to prevent.
/// </remarks>
public sealed class LicenceReadOnlyException : DomainException, IProblemExtensions
{
    /// <summary>The stable code a client branches on.</summary>
    public const string ErrorCode = "LICENCE_READ_ONLY";

    /// <summary>Builds the refusal from the decision that produced it.</summary>
    /// <param name="decision">Where the tenant stands and how to fix it.</param>
    public LicenceReadOnlyException(EnforcementDecision decision)
        : base(
            ErrorCode,
            "This subscription is not current, so Vuma is read-only: everything can be viewed, "
            + "reported, reprinted and exported, and nothing can be changed until payment is made.",
            DomainProblemKind.Forbidden)
    {
        ArgumentNullException.ThrowIfNull(decision);

        Decision = decision;

        Dictionary<string, object?> extensions = new(StringComparer.Ordinal)
        {
            ["enforcementLevel"] = decision.Level.ToString(),
            ["enforcementReason"] = decision.Reason.ToString(),
        };

        if (decision.AmountDue is { } due)
        {
            extensions["amountDue"] = due.Amount;
            extensions["currency"] = due.Currency;
        }

        if (decision.PayUrl is not null)
        {
            extensions["payUrl"] = decision.PayUrl;
        }

        if (decision.UpdatePaymentMethodUrl is not null)
        {
            extensions["updatePaymentMethodUrl"] = decision.UpdatePaymentMethodUrl;
        }

        if (decision.SupportPhone is not null)
        {
            extensions["supportPhone"] = decision.SupportPhone;
        }

        ProblemExtensions = extensions;
    }

    /// <summary>The decision that produced the refusal.</summary>
    public EnforcementDecision Decision { get; }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object?> ProblemExtensions { get; }
}

/// <summary>Raised when a module is used that the tenant's plan does not include.</summary>
/// <param name="module">The module.</param>
public sealed class ModuleNotEnabledException(string module)
    : DomainException(
        ErrorCode,
        $"The '{module}' module is not part of this subscription.",
        DomainProblemKind.Forbidden),
        IProblemExtensions
{
    /// <summary>The stable code a client branches on.</summary>
    public const string ErrorCode = "LICENCE_MODULE_NOT_ENABLED";

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object?> ProblemExtensions { get; } =
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["module"] = module,
            // The UI's next step. Without it, "not licensed" is a dead end and the customer rings
            // support to buy something they were ready to buy on their own.
            ["upgrade"] = $"module:{module}",
        };
}

/// <summary>Raised when a hard limit would be exceeded (<c>LICENSING.md</c> §6).</summary>
/// <param name="kind">Which limit.</param>
/// <param name="ceiling">The plan's ceiling.</param>
/// <param name="proposed">What was asked for.</param>
public sealed class LicenceLimitExceededException(LimitKind kind, long ceiling, long proposed)
    : DomainException(
        ErrorCode,
        $"This plan allows {ceiling} {Describe(kind)} and this would make {proposed}."),
        IProblemExtensions
{
    /// <summary>The stable code a client branches on.</summary>
    public const string ErrorCode = "LICENCE_LIMIT_EXCEEDED";

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object?> ProblemExtensions { get; } =
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["limit"] = kind.ToString(),
            ["ceiling"] = ceiling,
            ["proposed"] = proposed,
            ["upgrade"] = $"limit:{kind}",
        };

    private static string Describe(LimitKind kind) => kind switch
    {
        LimitKind.Stores => "stores",
        LimitKind.Terminals => "terminals",
        LimitKind.NamedUsers => "named users",
        _ => kind.ToString(),
    };
}
