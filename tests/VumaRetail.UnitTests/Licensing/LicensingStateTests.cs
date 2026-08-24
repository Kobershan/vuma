using FluentAssertions;
using VumaRetail.Domain.Licensing;
using VumaRetail.Licensing.Enforcement;

namespace VumaRetail.UnitTests.Licensing;

/// <summary>
/// The clock watermark, the open-session carve-out and the plan's own arithmetic.
/// </summary>
public sealed class LicensingStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Tenant = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [Fact]
    public void The_watermark_moves_forward_and_never_back()
    {
        ClockWatermark watermark = ClockWatermark.Start(Tenant, null, "store:1", Now);

        watermark.Observe(Now.AddHours(1)).Should().BeFalse();
        watermark.HighestSeen.Should().Be(Now.AddHours(1));

        watermark.Observe(Now.AddYears(-1)).Should().BeTrue();
        watermark.HighestSeen.Should().Be(Now.AddHours(1));
        watermark.RollbackCount.Should().Be(1);
    }

    [Fact]
    public void A_clock_wound_back_is_flagged_and_buys_no_time()
    {
        ClockWatermark watermark = ClockWatermark.Start(Tenant, null, "store:1", Now);

        // The whole point: the effective instant a lease's age is measured against never goes
        // backwards, so setting the system clock back is a tamper flag rather than a free extension
        // (LICENSING.md §7).
        watermark.Effective(Now.AddDays(-30)).Should().Be(Now);
        watermark.Effective(Now.AddDays(30)).Should().Be(Now.AddDays(30));
    }

    [Fact]
    public void An_open_session_may_finish_and_a_new_one_may_not_start()
    {
        OpenSessionRegistry registry = new();
        TimeSpan window = TimeSpan.FromMinutes(30);

        Guid openBefore = Guid.NewGuid();
        Guid openedAfter = Guid.NewGuid();

        registry.Open(openBefore, Now.AddMinutes(-5), window);
        registry.Open(openedAfter, Now.AddMinutes(5), window);

        DateTimeOffset restrictedSince = Now;

        registry.MayCarryOn(openBefore, restrictedSince, Now).Should().BeTrue();

        // A session opened after the restriction began is a new sale, and no new sale may start.
        // Without this the carve-out would be a permanent bypass for anything calling itself a session.
        registry.MayCarryOn(openedAfter, restrictedSince, Now).Should().BeFalse();

        registry.Close(openBefore);
        registry.MayCarryOn(openBefore, restrictedSince, Now).Should().BeFalse();
    }

    [Fact]
    public void An_unknown_session_gets_no_carve_out()
        => new OpenSessionRegistry().MayCarryOn(Guid.NewGuid(), Now, Now).Should().BeFalse();

    [Fact]
    public void An_in_flight_entry_past_its_own_deadline_gets_no_carve_out()
    {
        // ADR-135's second bound. Opened before the restriction is not enough on its own — a tenant who
        // never closes the session must not keep its carve-out forever.
        OpenSessionRegistry registry = new();
        TimeSpan window = TimeSpan.FromMinutes(30);

        Guid saleId = Guid.NewGuid();
        registry.Open(saleId, Now.AddMinutes(-5), window);

        DateTimeOffset restrictedSince = Now;

        registry.MayCarryOn(saleId, restrictedSince, Now.AddMinutes(24)).Should().BeTrue("still within its 30-minute window");
        registry.MayCarryOn(saleId, restrictedSince, Now.AddMinutes(26)).Should().BeFalse("31 minutes since it opened — past the deadline");
    }

    [Theory]
    [InlineData(LimitKind.Stores, true)]
    [InlineData(LimitKind.Terminals, true)]
    [InlineData(LimitKind.NamedUsers, true)]
    [InlineData(LimitKind.TransactionsPerMonth, false)]
    [InlineData(LimitKind.StorageBytes, false)]
    [InlineData(LimitKind.ApiCallsPerMonth, false)]
    public void The_hard_and_soft_split_is_the_one_LICENSING_md_specifies(LimitKind kind, bool hard)
        => LicenceLimits.IsHard(kind).Should().Be(hard);

    [Fact]
    public void An_emergency_unlock_is_in_force_until_the_instant_it_expires()
    {
        EmergencyUnlock unlock = EmergencyUnlock.Redeem(
            Tenant,
            null,
            Guid.NewGuid(),
            Now,
            Now.AddHours(72),
            "bank reversal");

        unlock.IsInForceAt(Now.AddHours(72).AddSeconds(-1)).Should().BeTrue();
        unlock.IsInForceAt(Now.AddHours(72)).Should().BeFalse();
    }

    [Fact]
    public void A_support_grant_is_active_only_while_it_is_approved_and_unexpired()
    {
        SupportGrant grant = SupportGrant.Request(
            Tenant,
            null,
            Guid.NewGuid(),
            "vendor:support",
            "Investigating a printing fault.",
            "support",
            Now);

        grant.IsActiveAt(Now).Should().BeFalse();

        grant.Approve("user:owner", Now, TimeSpan.FromHours(4));

        grant.IsActiveAt(Now.AddHours(3)).Should().BeTrue();
        grant.IsActiveAt(Now.AddHours(4)).Should().BeFalse();

        grant.Revoke("user:owner", Now.AddHours(1));
        grant.IsActiveAt(Now.AddHours(2)).Should().BeFalse();
    }

    [Fact]
    public void A_support_grant_cannot_be_answered_twice()
    {
        SupportGrant grant = SupportGrant.Request(
            Tenant,
            null,
            Guid.NewGuid(),
            "vendor:support",
            "Investigating a printing fault.",
            "support",
            Now);

        grant.Decline("user:owner", Now);

        Action approve = () => grant.Approve("user:owner", Now, TimeSpan.FromHours(4));

        approve.Should().Throw<SupportGrantStateException>();

        // Revocation stays idempotent though. "Make sure nobody is in my system" is a button somebody
        // presses twice.
        grant.Revoke("user:owner", Now);
        grant.State.Should().Be(SupportGrantState.Declined);
    }

    [Fact]
    public void A_metering_record_that_has_been_sent_is_not_recomputed_underneath_the_vendor()
    {
        MeteringRecord record = MeteringRecord.Queue(Tenant, null, "store:1", new DateOnly(2026, 6, 1), "{}");

        record.Recompute("""{"a":1}""");
        record.Payload.Should().Be("""{"a":1}""");

        record.MarkSent(Now);
        record.Recompute("""{"a":2}""");

        // Re-sending different numbers for a day the vendor has already billed is worse than sending
        // slightly stale ones.
        record.Payload.Should().Be("""{"a":1}""");
    }

    [Fact]
    public void An_activation_records_contact_only_forwards()
    {
        Activation activation = Activation.Open(
            Tenant,
            null,
            LicenceKey.NewKey(),
            Guid.NewGuid(),
            "store:1",
            HardwareFingerprint.Capture(
                HardwareFingerprint.NewSalt(),
                new Dictionary<FingerprintComponent, string> { [FingerprintComponent.MachineGuid] = "abc" }),
            Guid.NewGuid(),
            Now);

        activation.RecordContact(Now.AddHours(1));
        activation.LastContactAt.Should().Be(Now.AddHours(1));

        // The whole Path A ladder is measured from this column. A contact stamp that could move
        // backwards would silently extend a tolerance window that exists to be finite.
        activation.RecordContact(Now.AddDays(-1));
        activation.LastContactAt.Should().Be(Now.AddHours(1));
    }
}
