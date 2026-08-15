using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Domain.Licensing;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Licensing.Enforcement;

/// <summary>Everything the ladder is decided from, gathered once.</summary>
/// <param name="Now">
/// The effective instant: never earlier than the highest wall clock this install has ever seen, so
/// winding the clock back extends nothing.
/// </param>
/// <param name="IsActivated">Whether this installation has ever been activated.</param>
/// <param name="LastContactAt">The last successful exchange with the control plane, UTC.</param>
/// <param name="Lease">The most recent lease, or <c>null</c> if there has never been one.</param>
/// <param name="EmergencyUnlockUntil">When an in-force emergency code expires, UTC.</param>
public readonly record struct EnforcementInputs(
    DateTimeOffset Now,
    bool IsActivated,
    DateTimeOffset? LastContactAt,
    Lease? Lease,
    DateTimeOffset? EmergencyUnlockUntil);

/// <summary>
/// Decides where a tenant sits on the ladder. One function, one file, one reviewable list of rules.
/// </summary>
/// <remarks>
/// <para>
/// The stage brief asks for "a single <c>IEnforcementPolicy</c> [that] decides the level and what it
/// blocks, so the whole behaviour is one reviewable list rather than scattered checks". This is that
/// list. It touches no database, no clock and no network — everything it needs is in
/// <see cref="EnforcementInputs"/> — which is what makes both ladders, every boundary and the whole
/// no-accidental-lockout suite testable as arithmetic.
/// </para>
/// <para>
/// Read the rules in order. The first four are all ways of <em>not</em> restricting somebody, and that
/// ordering is the design: the expensive mistake in this module is a restriction that should not have
/// happened, so every exit that avoids one is checked before any rule that causes one.
/// </para>
/// </remarks>
public interface IEnforcementPolicy
{
    /// <summary>Decides the level, the reason and what the licence screen should say.</summary>
    /// <param name="inputs">Everything the decision is made from.</param>
    EnforcementDecision Evaluate(EnforcementInputs inputs);
}

/// <inheritdoc cref="IEnforcementPolicy" />
/// <param name="options">The configured ladder. The shipped values are <c>LICENSING.md</c> §4's.</param>
public sealed class EnforcementPolicy(LicensingOptions options) : IEnforcementPolicy
{
    /// <inheritdoc />
    public EnforcementDecision Evaluate(EnforcementInputs inputs)
    {
        // 1. An emergency code beats everything, and works with no network at all. This is the rung
        //    that means a bank reversal on a Friday evening is a phone call and not a lost weekend
        //    (LICENSING.md §5). It is checked first because a tenant holding one is, for its duration,
        //    simply not restricted — whatever else is true.
        if (inputs.EmergencyUnlockUntil is { } unlockUntil && inputs.Now < unlockUntil)
        {
            return new EnforcementDecision(
                EnforcementLevel.Normal,
                EnforcementReason.EmergencyUnlock,
                NoticeStage.BackOfficeBanner,
                inputs.Now,
                NextEscalationAt: unlockUntil,
                Messages: ["An emergency access code is in force. It expires automatically."]);
        }

        // 2. Never activated. Not a lapse — a beginning — and the activation wizard is what fixes it.
        //    The licence-screen commands carry the ADR-028 payment carve-out, so this state refuses
        //    every business write and still lets somebody activate.
        if (!inputs.IsActivated || inputs.Lease is null)
        {
            return options.RequireActivation
                ? new EnforcementDecision(
                    EnforcementLevel.ReadOnly,
                    EnforcementReason.NotActivated,
                    NoticeStage.BackOfficeBanner,
                    inputs.Now,
                    Messages: ["This installation has not been activated. Enter your licence key to begin."])
                : EnforcementDecision.Normal(inputs.Now);
        }

        Lease lease = inputs.Lease;

        // 3. A vendor-granted write unlock. A settled dispute, a payment the gateway has not confirmed
        //    yet, a customer who needs to close off month-end before cancelling (LICENSING.md §5).
        if (lease.WriteUnlockUntil is { } writeUnlock && inputs.Now < writeUnlock)
        {
            return Recovery(
                new EnforcementDecision(
                    EnforcementLevel.Normal,
                    EnforcementReason.WriteUnlock,
                    NoticeStage.BackOfficeBanner,
                    inputs.Now,
                    NextEscalationAt: writeUnlock),
                lease);
        }

        // 4. Path B — the only path on which the vendor has actually told this store anything. It is
        //    checked before Path A because being told beats not knowing: a tenant whose subscription
        //    lapsed and whose link then died is on Path B, at the point the link died, and must not
        //    quietly slide onto Path A's longer window.
        if (lease.DeclaredLevel is EnforcementLevel.ReadOnly
            && lease.DeclaredReason is EnforcementReason.SubscriptionLapsed)
        {
            return PathB(inputs, lease);
        }

        // A control plane that is only warning is warning, whatever the local ladder says.
        if (lease.DeclaredLevel is EnforcementLevel.Notice)
        {
            return Recovery(
                new EnforcementDecision(
                    EnforcementLevel.Notice,
                    lease.DeclaredReason is EnforcementReason.None
                        ? EnforcementReason.SubscriptionLapsed
                        : lease.DeclaredReason,
                    NoticeStage.BackOfficeBanner,
                    inputs.Now,
                    Messages: lease.MessageList()),
                lease);
        }

        // 5. Path A — we cannot verify. Measured from the last successful exchange, never from the
        //    lease's own expiry.
        return Recovery(PathA(inputs, lease), lease);
    }

    /// <summary>
    /// Path B: the control plane can be reached and has said the subscription is not current.
    /// </summary>
    /// <remarks>
    /// <b>A declaration without a completed dunning cycle is downgraded to a notice.</b> That is not
    /// defensive coding; it is the rule. <c>LICENSING.md</c> §4 and ADR-028 both require dunning to
    /// have <em>completed</em> with delivered notifications before anything is restricted, and a
    /// customer who was never warned has not been through dunning — whatever the lease says. It also
    /// means a control-plane bug that stamped every lease "lapsed" would produce banners rather than a
    /// customer base that cannot trade.
    /// </remarks>
    private EnforcementDecision PathB(EnforcementInputs inputs, Lease lease)
    {
        if (lease.DunningCompletedAt is not { } dunningCompleted)
        {
            return Recovery(
                new EnforcementDecision(
                    EnforcementLevel.Notice,
                    EnforcementReason.SubscriptionLapsed,
                    NoticeStage.FinalNotice,
                    inputs.Now,
                    Messages: lease.MessageList()),
                lease);
        }

        DateTimeOffset restrictedFrom = dunningCompleted.AddDays(options.DunningGraceDays);

        return Recovery(
            inputs.Now >= restrictedFrom
                ? new EnforcementDecision(
                    EnforcementLevel.ReadOnly,
                    EnforcementReason.SubscriptionLapsed,
                    NoticeStage.FinalNotice,
                    inputs.Now,
                    RestrictedSince: restrictedFrom,
                    Messages: lease.MessageList())
                : new EnforcementDecision(
                    EnforcementLevel.Notice,
                    EnforcementReason.SubscriptionLapsed,
                    NoticeStage.FinalNotice,
                    inputs.Now,
                    NextEscalationAt: restrictedFrom,
                    Messages: lease.MessageList()),
            lease);
    }

    /// <summary>
    /// Path A: the store cannot reach the control plane, so it runs on what it was last told.
    /// </summary>
    /// <remarks>
    /// The client computes this itself, from the last contact and nothing else, so that a store with a
    /// severed link reaches the same answer the control plane would have given it. That equivalence is
    /// asserted by a test — it is what makes "the network only refreshes the lease, it does not
    /// authorise" true rather than aspirational.
    /// </remarks>
    private EnforcementDecision PathA(EnforcementInputs inputs, Lease lease)
    {
        DateTimeOffset since = inputs.LastContactAt ?? lease.IssuedAt;
        double days = (inputs.Now - since).TotalDays;

        DateTimeOffset readOnlyAt = since.AddDays(options.OfflineReadOnlyAfterDays);
        DateTimeOffset posNoticeAt = since.AddDays(options.OfflinePosNoticeAfterDays);
        DateTimeOffset bannerAt = since.AddDays(options.OfflineBannerAfterDays);

        if (days >= options.OfflineReadOnlyAfterDays)
        {
            return new EnforcementDecision(
                EnforcementLevel.ReadOnly,
                EnforcementReason.CannotVerify,
                NoticeStage.FinalNotice,
                inputs.Now,
                RestrictedSince: readOnlyAt,
                Messages:
                [
                    "Vuma has not been able to reach the licence service for "
                    + $"{options.OfflineReadOnlyAfterDays} days. Restore this store's internet "
                    + "connection, or call support for an emergency access code.",
                ]);
        }

        if (days >= options.OfflinePosNoticeAfterDays)
        {
            return new EnforcementDecision(
                EnforcementLevel.Notice,
                EnforcementReason.CannotVerify,
                NoticeStage.PosSessionNotice,
                inputs.Now,
                NextEscalationAt: readOnlyAt);
        }

        if (days >= options.OfflineBannerAfterDays)
        {
            return new EnforcementDecision(
                EnforcementLevel.Notice,
                EnforcementReason.CannotVerify,
                NoticeStage.BackOfficeBanner,
                inputs.Now,
                NextEscalationAt: posNoticeAt);
        }

        // Silent. Days 0–3 of an outage are not the customer's problem and telling them about it makes
        // them think the software is broken.
        return new EnforcementDecision(
            EnforcementLevel.Normal,
            EnforcementReason.None,
            NoticeStage.None,
            inputs.Now,
            NextEscalationAt: bannerAt);
    }

    /// <summary>
    /// Attaches what the customer needs to fix it: what is owed, where to pay, who to ring.
    /// </summary>
    /// <remarks>
    /// Attached to <em>every</em> decision, including the ones that restrict nothing. The licence
    /// screen is reachable in all of them and a customer who wants to pay early should not have to be
    /// in trouble first.
    /// </remarks>
    private static EnforcementDecision Recovery(EnforcementDecision decision, Lease lease)
    {
        Money? due = lease.AmountDue();

        return decision with
        {
            AmountDue = due,
            PayUrl = lease.PayUrl,
            UpdatePaymentMethodUrl = lease.UpdatePaymentMethodUrl,
            SupportPhone = lease.SupportPhone,
            Messages = decision.Messages ?? lease.MessageList(),
        };
    }
}
