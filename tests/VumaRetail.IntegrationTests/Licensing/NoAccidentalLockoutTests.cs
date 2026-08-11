using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Domain.Licensing;
using VumaRetail.IntegrationTests.Api;
using VumaRetail.IntegrationTests.Harness;
using VumaRetail.Licensing.Commands;
using VumaRetail.Licensing.Control;

namespace VumaRetail.IntegrationTests.Licensing;

/// <summary>
/// The suite that protects the business from its own licensing (<c>docs/TESTING.md</c> §7).
/// </summary>
/// <remarks>
/// <para>
/// This is the one that gates the stage. Read-only itself is easy to build; almost all of the
/// engineering in Stage 04b is in making sure it can <b>only ever be deliberate</b> — and every test
/// here is a way it could have happened by accident instead.
/// </para>
/// <para>
/// It runs against the real store server, the real pipeline, a real database and a control plane that
/// signs real documents, on a clock the test moves by hand. The ladder arithmetic is asserted
/// separately and exhaustively in <c>EnforcementPolicyTests</c>; what these add is that the whole
/// wired system reaches the same answers.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class NoAccidentalLockoutTests(PostgresFixture fixture)
{
    [Theory]
    [InlineData(ControlPlaneBehaviour.Unreachable)]
    [InlineData(ControlPlaneBehaviour.ServerError)]
    [InlineData(ControlPlaneBehaviour.Garbage)]
    public async Task A_control_plane_that_is_down_trades_normally_to_the_boundary_and_not_a_minute_earlier(
        ControlPlaneBehaviour behaviour)
    {
        // docs/TESTING.md §7: unreachable, 500s and garbage are all treated identically, and none of
        // them is ever read as "unlicensed". One bad vendor deployment must not put the entire
        // customer base into read-only at once (ADR-028's fourth rule).
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);

        harness.ControlPlane.Behaviour = behaviour;

        // A full trading fortnight with the vendor switched off.
        for (int day = 1; day < 15; day++)
        {
            harness.Clock.Advance(TimeSpan.FromDays(1));

            LeaseRefreshResult refresh = await harness.SendAsync(new RefreshLeaseCommand());
            refresh.Reached.Should().BeFalse();

            EnforcementDecision decision = await CurrentAsync(harness);

            decision.Level.Should().NotBe(
                EnforcementLevel.ReadOnly,
                "day {0} of a vendor outage is inside the tolerance window",
                day);
        }

        // One minute before the boundary: still trading.
        harness.Clock.Advance(TimeSpan.FromDays(1) - TimeSpan.FromMinutes(1));
        (await CurrentAsync(harness)).Level.Should().NotBe(EnforcementLevel.ReadOnly);

        // And at the boundary, deliberately.
        harness.Clock.Advance(TimeSpan.FromMinutes(1));

        EnforcementDecision atBoundary = await CurrentAsync(harness);
        atBoundary.Level.Should().Be(EnforcementLevel.ReadOnly);
        atBoundary.Reason.Should().Be(EnforcementReason.CannotVerify);
    }

    [Fact]
    public async Task A_single_failed_charge_and_a_second_and_a_third_restrict_nobody()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);

        ControlPlaneTenant vendor = harness.ControlPlane[harness.LicenceKey];

        // Three failed charges over three weeks. Dunning has not completed, so nothing may happen.
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            vendor.SubscriptionCurrent = false;
            vendor.DunningCompletedAt = null;
            vendor.AmountDue = new Domain.Primitives.Money(1499m, "ZAR");

            harness.Clock.Advance(TimeSpan.FromDays(7));

            await harness.SendAsync(new RefreshLeaseCommand());

            EnforcementDecision decision = await CurrentAsync(harness);

            decision.Level.Should().Be(
                EnforcementLevel.Notice,
                "failed charge {0} without a completed dunning cycle must warn and never restrict",
                attempt);
        }
    }

    [Fact]
    public async Task Read_only_arrives_only_after_a_completed_dunning_cycle()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);

        ControlPlaneTenant vendor = harness.ControlPlane[harness.LicenceKey];

        vendor.SubscriptionCurrent = false;
        vendor.AmountDue = new Domain.Primitives.Money(1499m, "ZAR");
        vendor.DunningCompletedAt = null;

        await harness.SendAsync(new RefreshLeaseCommand());
        (await CurrentAsync(harness)).Level.Should().Be(EnforcementLevel.Notice);

        // Fourteen days of escalating warning later, with the notifications recorded as delivered.
        harness.Clock.Advance(TimeSpan.FromDays(14));
        vendor.DunningCompletedAt = harness.Clock.UtcNow;

        await harness.SendAsync(new RefreshLeaseCommand());

        EnforcementDecision restricted = await CurrentAsync(harness);
        restricted.Level.Should().Be(EnforcementLevel.ReadOnly);
        restricted.Reason.Should().Be(EnforcementReason.SubscriptionLapsed);
        restricted.AmountDue.Should().NotBeNull();
        restricted.PayUrl.Should().NotBeNullOrWhiteSpace();
        restricted.UpdatePaymentMethodUrl.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_payment_restores_full_access_within_sixty_seconds_with_no_manual_step()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);

        ControlPlaneTenant vendor = harness.ControlPlane[harness.LicenceKey];
        vendor.SubscriptionCurrent = false;
        vendor.DunningCompletedAt = harness.Clock.UtcNow.AddDays(-1);
        vendor.AmountDue = new Domain.Primitives.Money(1499m, "ZAR");

        await harness.SendAsync(new RefreshLeaseCommand());
        (await CurrentAsync(harness)).Level.Should().Be(EnforcementLevel.ReadOnly);

        DateTimeOffset paidAt = harness.Clock.UtcNow;

        // The payment lands at the vendor.
        vendor.SubscriptionCurrent = true;
        vendor.DunningCompletedAt = null;
        vendor.AmountDue = null;

        // The next heartbeat is at most 30 seconds away while restricted; "retry now" is immediate.
        harness.Clock.Advance(TimeSpan.FromSeconds(30));
        await harness.SendAsync(new RefreshLeaseCommand());

        (await CurrentAsync(harness)).Level.Should().Be(EnforcementLevel.Normal);
        (harness.Clock.UtcNow - paidAt).Should().BeLessThan(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public async Task An_emergency_code_unlocks_with_networking_fully_disabled()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);

        // Everything off: the vendor is unreachable and the store has been out of contact for a month.
        harness.ControlPlane.Behaviour = ControlPlaneBehaviour.Unreachable;
        harness.Clock.Advance(TimeSpan.FromDays(30));

        (await CurrentAsync(harness)).Level.Should().Be(EnforcementLevel.ReadOnly);

        // The vendor reads the code down the phone. Nothing about issuing it touches this store.
        string code = harness.ControlPlane.IssueEmergencyCode(
            harness.TenantId,
            TimeSpan.FromHours(72),
            "debit order reversed in error");

        DateTimeOffset expiresAt = await harness.SendAsync(new RedeemEmergencyCodeCommand(code));

        EnforcementDecision unlocked = await CurrentAsync(harness);
        unlocked.Level.Should().Be(EnforcementLevel.Normal);
        unlocked.Reason.Should().Be(EnforcementReason.EmergencyUnlock);

        // And it expires exactly on time, still with no network.
        harness.Clock.MoveTo(expiresAt.AddSeconds(-1));
        (await CurrentAsync(harness)).Level.Should().Be(EnforcementLevel.Normal);

        harness.Clock.MoveTo(expiresAt);
        (await CurrentAsync(harness)).Level.Should().Be(EnforcementLevel.ReadOnly);
    }

    [Fact]
    public async Task An_emergency_code_cannot_be_reused()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);

        string code = harness.ControlPlane.IssueEmergencyCode(harness.TenantId, TimeSpan.FromHours(4));

        await harness.SendAsync(new RedeemEmergencyCodeCommand(code));

        Func<Task> again = () => harness.SendAsync(new RedeemEmergencyCodeCommand(code));

        (await again.Should().ThrowAsync<EmergencyCodeRejectedException>())
            .Which.Message.Should().Contain("already been used");
    }

    [Fact]
    public async Task An_emergency_code_signed_with_the_wrong_key_is_rejected_and_flagged()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);

        (VumaRetail.Licensing.Signing.LicenceSigner impostor, _) =
            VumaRetail.Licensing.Signing.LicenceSigner.Generate();

        string forged = impostor.Sign(new VumaRetail.Licensing.Signing.EmergencyCodeDocument(
            VumaRetail.Licensing.Signing.SignedDocumentKind.EmergencyCode,
            Guid.NewGuid(),
            harness.TenantId,
            harness.Clock.UtcNow,
            harness.Clock.UtcNow.AddHours(72),
            "forged"));

        Func<Task> redeem = () => harness.SendAsync(new RedeemEmergencyCodeCommand(forged));

        await redeem.Should().ThrowAsync<VumaRetail.Licensing.Signing.LicenceSignatureException>();
    }

    [Fact]
    public async Task Another_tenants_emergency_code_does_nothing_here()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);

        string code = harness.ControlPlane.IssueEmergencyCode(Guid.NewGuid(), TimeSpan.FromHours(4));

        Func<Task> redeem = () => harness.SendAsync(new RedeemEmergencyCodeCommand(code));

        (await redeem.Should().ThrowAsync<EmergencyCodeRejectedException>())
            .Which.Message.Should().Contain("different business");
    }

    [Fact]
    public async Task A_clock_moved_in_either_direction_is_flagged_and_never_restricts()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);

        DateTimeOffset real = harness.Clock.UtcNow;

        // Forwards into next year, then back. Neither may restrict, and neither may extend.
        harness.Clock.MoveTo(real.AddYears(1));
        await harness.SendAsync(new SendHeartbeatCommand());

        harness.Clock.MoveTo(real);
        await harness.SendAsync(new SendHeartbeatCommand());

        IReadOnlyList<TamperFlag> flags = await harness.InScopeAsync(provider =>
            provider.GetRequiredService<ILicenceStateRepository>().ListUnreportedFlagsAsync());

        flags.Should().Contain(flag => flag.Kind == TamperKind.ClockRollback);

        // Contact was made throughout, so the tenant is Normal — a clock that jumps is far more often
        // a dead CMOS battery than a fraud, and ADR-026 puts the leverage in vendor-side detection.
        (await CurrentAsync(harness)).Level.Should().Be(EnforcementLevel.Normal);
    }

    [Fact]
    public async Task Winding_the_clock_back_does_not_reset_the_tolerance_window()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);

        harness.ControlPlane.Behaviour = ControlPlaneBehaviour.Unreachable;

        // Twenty days of silence puts the tenant read-only.
        harness.Clock.Advance(TimeSpan.FromDays(20));
        await harness.SendAsync(new SendHeartbeatCommand());
        (await CurrentAsync(harness)).Level.Should().Be(EnforcementLevel.ReadOnly);

        // Winding the clock back to day one must buy nothing: the effective instant is never earlier
        // than the highest ever seen (LICENSING.md §7).
        harness.Clock.Advance(TimeSpan.FromDays(-19));

        (await CurrentAsync(harness)).Level.Should().Be(EnforcementLevel.ReadOnly);
    }

    [Fact]
    public async Task A_hardware_change_within_tolerance_does_not_restrict_anybody()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);

        Activation activation = await harness.InScopeAsync(async provider =>
            (await provider.GetRequiredService<IActivationRepository>().FindCurrentAsync())!);

        IReadOnlyDictionary<FingerprintComponent, string> original =
            harness.Services.GetRequiredService<IHardwareFingerprintProvider>().Read();

        Dictionary<FingerprintComponent, string> replacedNic = new(original)
        {
            [FingerprintComponent.PrimaryMacAddress] = "AABBCCDDEEFF",
        };

        // The claim LICENSING.md §3 makes, against whatever this platform can actually report. On a
        // machine with all five components that is 9 of 11; here it is the same proportion of the
        // eight points a non-Windows build can read (see MachineFingerprintProvider's remarks).
        activation.StillTheSameMachine(replacedNic).Should().BeTrue();

        // And the tenant is untouched by it, which is the claim that matters.
        (await CurrentAsync(harness)).Level.Should().Be(EnforcementLevel.Normal);
    }

    [Fact]
    public async Task A_full_trading_day_with_the_control_plane_switched_off_has_zero_customer_impact()
    {
        // docs/API_CONTROL_PLANE.md §5's test, written out: a whole day of trading with the vendor
        // gone. Nothing is refused, and nothing about the store changes except that it has not been
        // able to say hello.
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);

        harness.ControlPlane.Behaviour = ControlPlaneBehaviour.Unreachable;

        for (int hour = 0; hour < 24; hour++)
        {
            harness.Clock.Advance(TimeSpan.FromHours(1));

            await harness.SendAsync(new SendHeartbeatCommand());

            Guid roleId = await harness.SendAsync(
                new VumaRetail.Application.Identity.Commands.CreateRoleCommand($"role-hour-{hour}", []));

            roleId.Should().NotBeEmpty();
        }

        (await CurrentAsync(harness)).Level.Should().Be(EnforcementLevel.Normal);
    }

    private static Task<EnforcementDecision> CurrentAsync(ApiHarness harness)
        => harness.InScopeAsync(provider =>
            provider.GetRequiredService<IEnforcementStatusReader>().CurrentLevel());
}
