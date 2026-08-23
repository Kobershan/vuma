using System.Collections.Concurrent;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Licensing;

namespace VumaRetail.Licensing.Enforcement;

/// <summary>
/// Refuses writes while a tenant is read-only. One interceptor, not forty modules' worth of checks.
/// </summary>
/// <remarks>
/// <para>
/// Pipeline slot <see cref="PipelineOrder.ReadOnlyGuard"/> (50), which Stage 03 reserved and left
/// empty for exactly this. Fifty is <em>outside</em> validation (100) and the transaction (200), so a
/// refusal costs nothing: no validator runs, no transaction opens, and a store being told "no" three
/// hundred times an hour by an impatient till is not also being asked for three hundred database round
/// trips.
/// </para>
/// <para>
/// Everything this behaviour needs is already on the envelope. Stage 03's
/// <see cref="MessageEnvelope"/> reads <c>[CommandSideEffect]</c> once, so the guard reflects over
/// nothing per request — it compares two enum values and, in the overwhelmingly common case, calls
/// <c>next</c>.
/// </para>
/// <para>
/// <b>Queries are not touched.</b> Not "queries are allowed through" — the guard never asks about
/// them, so there is no code path on which a report, a reprint or an export could be refused. ADR-028
/// promises those keep working and this is the shape of that promise.
/// </para>
/// </remarks>
/// <param name="entitlements">Where the tenant sits on the ladder.</param>
/// <param name="sessions">Which till sessions were open when the restriction fell due.</param>
/// <param name="options">Whether the open-session carve-out is on.</param>
public sealed class ReadOnlyGuardBehaviour(
    IEnforcementStatusReader entitlements,
    IOpenSessionRegistry sessions,
    LicensingOptions options) : IPipelineBehaviour
{
    /// <inheritdoc />
    public int Order => PipelineOrder.ReadOnlyGuard;

    /// <inheritdoc />
    public async Task<TResult> HandleAsync<TResult>(
        MessageEnvelope envelope,
        Func<CancellationToken, Task<TResult>> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(next);

        // A query, or a command that only reads. Nothing to decide, and nothing to pay for deciding.
        if (envelope.Kind is not MessageKind.Command || envelope.Effect is not SideEffect.Write)
        {
            return await next(cancellationToken).ConfigureAwait(false);
        }

        // The three ADR-028 carve-outs, declared on the command itself and capped at a closed list by
        // an architecture test. Checked before the level is even looked up: a backup and an offline
        // flush must run in every state, so asking would be asking a question whose answer cannot
        // change what happens next.
        if (envelope.Exemption is not ReadOnlyExemption.None)
        {
            return await next(cancellationToken).ConfigureAwait(false);
        }

        EnforcementDecision decision = await entitlements
            .CurrentLevel(cancellationToken)
            .ConfigureAwait(false);

        if (!decision.IsReadOnly)
        {
            return await next(cancellationToken).ConfigureAwait(false);
        }

        // The open-session carve-out. A sale on the screen and a cash-up not yet closed finish; no new
        // sale starts. The alternative is a drawer of unrecorded cash and a customer holding goods,
        // which is a mess the vendor would have created to make a billing point. Bounded twice over:
        // MayCarryOn refuses anything opened after the restriction began, and refuses anything that has
        // outlived its own deadline as of `decision.EvaluatedAt` — the same watermarked instant the
        // decision itself was computed from, so winding the clock back buys nothing here either.
        if (options.OpenSessionCarveOut
            && envelope.Message is ISessionScopedCommand session
            && sessions.MayCarryOn(session.SessionId, decision.RestrictedSince, decision.EvaluatedAt))
        {
            return await next(cancellationToken).ConfigureAwait(false);
        }

        throw new LicenceReadOnlyException(decision);
    }
}

/// <summary>
/// Which till sessions were open before a restriction fell due (<c>LICENSING.md</c> §4).
/// </summary>
/// <remarks>
/// <para>
/// In-memory and per node, which is the right lifetime: a session belongs to a running till, and a
/// store server that has restarted has no in-progress sales to protect — every terminal reconnects and
/// starts a new session. Persisting it would create a way for a session opened last month to still be
/// "in progress" today, which is a hole rather than a feature.
/// </para>
/// <para>
/// Stage 09's POS opens and closes real sessions against this. It is built here, with the guard, and
/// tested here, because a carve-out retrofitted into an enforcement path is a carve-out that ends up
/// half-applied.
/// </para>
/// </remarks>
public sealed class OpenSessionRegistry : IOpenSessionRegistry
{
    private readonly ConcurrentDictionary<Guid, Entry> _open = new();

    /// <inheritdoc />
    public void Open(Guid id, DateTimeOffset at, TimeSpan maxDuration) => _open[id] = new Entry(at, maxDuration);

    /// <inheritdoc />
    public void Close(Guid id) => _open.TryRemove(id, out _);

    /// <inheritdoc />
    public bool MayCarryOn(Guid id, DateTimeOffset? restrictedSince, DateTimeOffset evaluatedAt)
    {
        if (!_open.TryGetValue(id, out Entry entry))
        {
            return false;
        }

        // Bound 1: opened *after* the restriction began gets no carve-out — that is a new sale, and no
        // new sale may start. Without this comparison the carve-out would be a permanent bypass for
        // anything that called itself a session.
        bool openedBeforeRestriction = restrictedSince is not { } since || entry.OpenedAt < since;

        // Bound 2: the hard deadline. Without it, a tenant who simply never closes a session or a sale
        // keeps every one of its writes carved out indefinitely — this is what makes the worst case
        // "what was physically on screen, finished within its window" rather than "forever".
        bool withinDeadline = evaluatedAt - entry.OpenedAt <= entry.MaxDuration;

        return openedBeforeRestriction && withinDeadline;
    }

    private readonly record struct Entry(DateTimeOffset OpenedAt, TimeSpan MaxDuration);
}
