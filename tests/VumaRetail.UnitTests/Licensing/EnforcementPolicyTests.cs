using FluentAssertions;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Domain.Licensing;
using VumaRetail.Domain.Primitives;
using VumaRetail.Licensing;
using VumaRetail.Licensing.Enforcement;

namespace VumaRetail.UnitTests.Licensing;

/// <summary>
/// Both ladders, every boundary (ADR-028, <c>LICENSING.md</c> §4).
/// </summary>
/// <remarks>
/// <para>
/// This is the no-accidental-lockout suite's arithmetic half — <c>docs/TESTING.md</c> §7 calls it
/// "the suite that protects the business from its own licensing". The policy takes no database, no
/// clock and no network, so every one of these is a table row rather than an arrangement, and the
/// boundaries are asserted at the exact hour rather than approximately.
/// </para>
/// <para>
/// Note what is <em>not</em> here: there is no test that reaches a hard lockout, because the ladder
/// cannot produce one. <see cref="EnforcementLevel"/> has three members and hard lockout is a manual,
/// two-person, audited vendor action (ADR-028).
/// </para>
/// </remarks>
public sealed class EnforcementPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly LicensingOptions Options = new();

    private static readonly EnforcementPolicy Policy = new(Options);

    [Fact]
    public void An_unactivated_installation_is_read_only_and_says_so()
    {
        EnforcementDecision decision = Policy.Evaluate(new EnforcementInputs(Now, false, null, null, null));

        decision.Level.Should().Be(EnforcementLevel.ReadOnly);
        decision.Reason.Should().Be(EnforcementReason.NotActivated);
    }

    [Fact]
    public void An_unactivated_installation_trades_when_activation_is_not_required()
    {
        // The DR path and the automated drills need a store server that starts and answers before
        // there is a control plane to activate against.
        EnforcementPolicy relaxed = new(new LicensingOptions { RequireActivation = false });

        relaxed.Evaluate(new EnforcementInputs(Now, false, null, null, null))
            .Level.Should().Be(EnforcementLevel.Normal);
    }

    [Theory]
    // Path A, from LICENSING.md §4's table. Silent for three days, then escalating warnings, and
    // read-only only at the configured boundary — never a minute earlier.
    [InlineData(0, EnforcementLevel.Normal, NoticeStage.None)]
    [InlineData(3, EnforcementLevel.Normal, NoticeStage.None)]
    [InlineData(4, EnforcementLevel.Notice, NoticeStage.BackOfficeBanner)]
    [InlineData(7, EnforcementLevel.Notice, NoticeStage.BackOfficeBanner)]
    [InlineData(8, EnforcementLevel.Notice, NoticeStage.PosSessionNotice)]
    [InlineData(14, EnforcementLevel.Notice, NoticeStage.PosSessionNotice)]
    [InlineData(15, EnforcementLevel.ReadOnly, NoticeStage.FinalNotice)]
    [InlineData(45, EnforcementLevel.ReadOnly, NoticeStage.FinalNotice)]
    public void Path_A_escalates_only_at_the_configured_boundaries(
        int daysSinceContact,
        EnforcementLevel expected,
        NoticeStage notice)
    {
        DateTimeOffset lastContact = Now.AddDays(-daysSinceContact);

        EnforcementDecision decision = Policy.Evaluate(new EnforcementInputs(
            Now,
            true,
            lastContact,
            CurrentLease(lastContact),
            null));

        decision.Level.Should().Be(expected);
        decision.Notice.Should().Be(notice);

        if (expected is not EnforcementLevel.Normal)
        {
            decision.Reason.Should().Be(EnforcementReason.CannotVerify);
        }
    }

    [Fact]
    public void Path_A_does_not_restrict_a_minute_before_the_boundary()
    {
        // The assertion docs/TESTING.md §7 words as "locks only at the configured boundary — never
        // earlier". One minute matters: it is the difference between a shop trading on the fifteenth
        // morning and a shop that stopped overnight.
        DateTimeOffset lastContact = Now.AddDays(-Options.OfflineReadOnlyAfterDays).AddMinutes(1);

        Policy.Evaluate(new EnforcementInputs(Now, true, lastContact, CurrentLease(lastContact), null))
            .Level.Should().Be(EnforcementLevel.Notice);
    }

    [Fact]
    public void An_expired_lease_alone_restricts_nobody()
    {
        // The most counter-intuitive rule in the module, and the one that keeps a vendor outage from
        // stopping every customer at once. A lease lives 72 hours; the tolerance window is a
        // fortnight, and it is the window that decides.
        DateTimeOffset lastContact = Now.AddDays(-5);

        Lease expired = Lease.Record(
            Tenant,
            null,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "document",
            ["pos"],
            LicenceLimits.Unlimited,
            EnforcementLevel.Normal,
            EnforcementReason.None,
            lastContact,
            lastContact.AddHours(72),
            1);

        expired.ExpiresAt.Should().BeBefore(Now);

        Policy.Evaluate(new EnforcementInputs(Now, true, lastContact, expired, null))
            .Level.Should().Be(EnforcementLevel.Notice);
    }

    [Fact]
    public void One_successful_heartbeat_a_day_before_the_boundary_resets_to_normal()
    {
        DateTimeOffset almost = Now.AddDays(-(Options.OfflineReadOnlyAfterDays - 1));

        Policy.Evaluate(new EnforcementInputs(Now, true, almost, CurrentLease(almost), null))
            .Level.Should().Be(EnforcementLevel.Notice);

        // The heartbeat lands. Nothing else changes.
        Policy.Evaluate(new EnforcementInputs(Now, true, Now, CurrentLease(Now), null))
            .Level.Should().Be(EnforcementLevel.Normal);
    }

    [Fact]
    public void Path_B_never_restricts_without_a_completed_dunning_cycle()
    {
        // A single failed charge, and a second, and a third: no restriction. The control plane may
        // declare read-only all it likes — without a completed cycle the customer was never warned,
        // and a customer who was never warned has not been through dunning.
        Lease declared = LapsedLease(dunningCompletedAt: null);

        EnforcementDecision decision = Policy.Evaluate(
            new EnforcementInputs(Now, true, Now, declared, null));

        decision.Level.Should().Be(EnforcementLevel.Notice);
        decision.Reason.Should().Be(EnforcementReason.SubscriptionLapsed);
        decision.Notice.Should().Be(NoticeStage.FinalNotice);
    }

    [Fact]
    public void Path_B_restricts_once_dunning_has_completed()
    {
        Lease declared = LapsedLease(Now.AddHours(-1));

        EnforcementDecision decision = Policy.Evaluate(
            new EnforcementInputs(Now, true, Now, declared, null));

        decision.Level.Should().Be(EnforcementLevel.ReadOnly);
        decision.Reason.Should().Be(EnforcementReason.SubscriptionLapsed);
        decision.RestrictedSince.Should().Be(Now.AddHours(-1));

        // And the customer is told what to do about it, which is the whole of LICENSING.md Rule 3.
        decision.AmountDue.Should().Be(new Money(1499.00m, "ZAR"));
        decision.PayUrl.Should().NotBeNullOrWhiteSpace();
        decision.UpdatePaymentMethodUrl.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Path_B_honours_a_configured_grace_period()
    {
        EnforcementPolicy lenient = new(new LicensingOptions { DunningGraceDays = 3 });

        Lease declared = LapsedLease(Now.AddDays(-2));

        lenient.Evaluate(new EnforcementInputs(Now, true, Now, declared, null))
            .Level.Should().Be(EnforcementLevel.Notice);

        lenient.Evaluate(new EnforcementInputs(Now.AddDays(2), true, Now, declared, null))
            .Level.Should().Be(EnforcementLevel.ReadOnly);
    }

    [Fact]
    public void Path_B_beats_path_A_rather_than_sliding_onto_its_longer_window()
    {
        // A tenant whose subscription lapsed and whose link then died. Being told beats not knowing,
        // and the alternative would be a fortnight of free trading for anybody who unplugged the
        // router after the final notice.
        Lease declared = LapsedLease(Now.AddDays(-20));

        Policy.Evaluate(new EnforcementInputs(Now, true, Now.AddDays(-20), declared, null))
            .Reason.Should().Be(EnforcementReason.SubscriptionLapsed);
    }

    [Fact]
    public void An_emergency_code_beats_everything_including_a_completed_dunning_cycle()
    {
        Lease declared = LapsedLease(Now.AddDays(-5));

        EnforcementDecision decision = Policy.Evaluate(new EnforcementInputs(
            Now,
            true,
            Now.AddDays(-30),
            declared,
            Now.AddHours(72)));

        decision.Level.Should().Be(EnforcementLevel.Normal);
        decision.Reason.Should().Be(EnforcementReason.EmergencyUnlock);
        decision.NextEscalationAt.Should().Be(Now.AddHours(72));
    }

    [Fact]
    public void An_expired_emergency_code_stops_helping_exactly_on_time()
    {
        Lease declared = LapsedLease(Now.AddDays(-5));
        DateTimeOffset expiry = Now.AddHours(1);

        Policy.Evaluate(new EnforcementInputs(expiry.AddSeconds(-1), true, Now, declared, expiry))
            .Level.Should().Be(EnforcementLevel.Normal);

        Policy.Evaluate(new EnforcementInputs(expiry, true, Now, declared, expiry))
            .Level.Should().Be(EnforcementLevel.ReadOnly);
    }

    [Fact]
    public void A_vendor_write_unlock_restores_writes_without_touching_the_reason_it_was_needed()
    {
        Lease declared = LapsedLease(Now.AddDays(-5), writeUnlockUntil: Now.AddDays(3));

        EnforcementDecision decision = Policy.Evaluate(
            new EnforcementInputs(Now, true, Now, declared, null));

        decision.Level.Should().Be(EnforcementLevel.Normal);
        decision.Reason.Should().Be(EnforcementReason.WriteUnlock);

        // Still shown what is owed. A settled dispute is not a forgiven invoice.
        decision.AmountDue.Should().NotBeNull();
    }

    [Fact]
    public void A_clock_wound_backwards_buys_nothing()
    {
        // The policy is handed the *effective* instant — never earlier than the highest this install
        // has ever seen — so an input that claims the past cannot roll a ladder back. The watermark
        // that produces it is asserted separately, in ClockWatermarkTests.
        DateTimeOffset lastContact = Now.AddDays(-20);

        Policy.Evaluate(new EnforcementInputs(Now, true, lastContact, CurrentLease(lastContact), null))
            .Level.Should().Be(EnforcementLevel.ReadOnly);
    }

    [Fact]
    public void The_decision_always_says_how_to_fix_it_even_when_nothing_is_wrong()
    {
        // A customer who wants to pay early should not have to be in trouble first.
        EnforcementDecision decision = Policy.Evaluate(
            new EnforcementInputs(Now, true, Now, CurrentLease(Now), null));

        decision.Level.Should().Be(EnforcementLevel.Normal);
        decision.UpdatePaymentMethodUrl.Should().NotBeNullOrWhiteSpace();
        decision.SupportPhone.Should().NotBeNullOrWhiteSpace();
    }

    private static Lease CurrentLease(DateTimeOffset issuedAt)
        => Lease.Record(
                Tenant,
                null,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "document",
                ["pos", "inventory"],
                LicenceLimits.Unlimited,
                EnforcementLevel.Normal,
                EnforcementReason.None,
                issuedAt,
                issuedAt + TimeSpan.FromHours(72),
                1)
            .WithRecovery(
                null,
                null,
                "https://pay.vumaretail.app/payment-method",
                null,
                null,
                "+27 11 000 0000",
                []);

    private static Lease LapsedLease(
        DateTimeOffset? dunningCompletedAt,
        DateTimeOffset? writeUnlockUntil = null)
        => Lease.Record(
                Tenant,
                null,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "document",
                ["pos"],
                LicenceLimits.Unlimited,
                EnforcementLevel.ReadOnly,
                EnforcementReason.SubscriptionLapsed,
                Now.AddHours(-2),
                Now.AddHours(70),
                1)
            .WithRecovery(
                new Money(1499.00m, "ZAR"),
                "https://pay.vumaretail.app/invoice",
                "https://pay.vumaretail.app/payment-method",
                dunningCompletedAt,
                writeUnlockUntil,
                "+27 11 000 0000",
                ["Your subscription is not current."]);
}
