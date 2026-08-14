using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Contracts.Licensing;
using VumaRetail.Domain.Licensing;
using VumaRetail.IntegrationTests.Api;
using VumaRetail.IntegrationTests.Harness;
using VumaRetail.Licensing.Commands;
using VumaRetail.Licensing.Control;
using VumaRetail.Licensing.Queries;

namespace VumaRetail.IntegrationTests.Licensing;

/// <summary>
/// Activation end to end, on clean hardware, against a control plane (<c>LICENSING.md</c> §3).
/// </summary>
/// <remarks>
/// The control plane is <c>InProcessControlPlane</c>, which signs real documents with a real Ed25519
/// key — so the signature verification, the counter checks and the tenant checks under test here are
/// the production ones, with a stand-in for exactly one thing: the network (ADR-022).
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class ActivationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Activates_from_a_licence_key_on_clean_hardware()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture, activate: false);

        LicenceStatusResponse before = await harness.QueryAsync(new GetLicenceStatusQuery());
        before.Activated.Should().BeFalse();
        before.EnforcementLevel.Should().Be(nameof(EnforcementLevel.ReadOnly));
        before.EnforcementReason.Should().Be(nameof(EnforcementReason.NotActivated));

        await harness.ActivateAsync();

        LicenceStatusResponse after = await harness.QueryAsync(new GetLicenceStatusQuery());

        after.Activated.Should().BeTrue();
        after.EnforcementLevel.Should().Be(nameof(EnforcementLevel.Normal));
        after.PlanCode.Should().Be("test");
        after.LicenceExpiresAt.Should().NotBeNull();
        after.LeaseExpiresAt.Should().NotBeNull();
        after.LastContactAt.Should().NotBeNull();
    }

    [Fact]
    public async Task A_second_activation_is_refused_rather_than_silently_rebinding()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);

        Func<Task> again = () => harness.ActivateAsync();

        (await again.Should().ThrowAsync<AlreadyActivatedException>())
            .Which.Code.Should().Be("LICENCE_ALREADY_ACTIVATED");
    }

    [Fact]
    public async Task A_key_the_vendor_does_not_know_is_refused_before_anything_is_written()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture, activate: false);

        Func<Task> activate = () => harness.SendAsync(new ActivateInstallationCommand(
            LicenceKey.NewKey().Value,
            "Nowhere",
            "nobody@example.com"));

        (await activate.Should().ThrowAsync<ControlPlaneRefusedException>())
            .Which.Refusal.Should().Be(ControlPlaneRefusal.LicenceKeyInvalid);

        (await harness.QueryAsync(new GetLicenceStatusQuery())).Activated.Should().BeFalse();
    }

    [Fact]
    public async Task A_key_belonging_to_another_business_is_refused()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture, activate: false);

        LicenceKey foreign = harness.ControlPlane.Register(new ControlPlaneTenant
        {
            TenantId = Guid.NewGuid(),
            PlanCode = "someone-else",
        });

        Func<Task> activate = () => harness.SendAsync(
            new ActivateInstallationCommand(foreign.Value, "Wrong shop", "owner@example.com"));

        (await activate.Should().ThrowAsync<LicenceTenantMismatchException>())
            .Which.Code.Should().Be("LICENCE_TENANT_MISMATCH");
    }

    [Fact]
    public async Task A_subscription_that_is_not_active_cannot_be_activated_in_the_first_place()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture, activate: false);

        LicenceKey key = harness.ControlPlane.Register(new ControlPlaneTenant
        {
            TenantId = harness.TenantId,
            StoreId = harness.StoreId,
            SubscriptionCurrent = false,
        });

        Func<Task> activate = () => harness.SendAsync(
            new ActivateInstallationCommand(key.Value, "API Sandton", "owner@api.example"));

        (await activate.Should().ThrowAsync<ControlPlaneRefusedException>())
            .Which.Refusal.Should().Be(ControlPlaneRefusal.SubscriptionNotActive);
    }

    [Fact]
    public async Task A_disaster_recovery_rebind_is_never_blocked()
    {
        // R4 and LICENSING.md §3's non-negotiable exception. If a store has burned down, the last
        // thing anybody needs is an activation error — so this path is auto-approved, always.
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);

        ActivationResult result = await harness.SendAsync(
            new RebindActivationCommand(RebindReason.DisasterRecovery, "restored from snapshot"));

        result.Level.Should().Be(EnforcementLevel.Normal);

        Activation activation = await harness.InScopeAsync(async provider =>
            (await provider.GetRequiredService<IActivationRepository>().FindCurrentAsync())!);

        activation.RebindCount.Should().Be(1);
        activation.State.Should().Be(ActivationState.Active);
    }

    [Fact]
    public async Task A_replayed_old_lease_cannot_extend_anything()
    {
        // The restored-backup attack: keep last month's documents, restore them alongside the
        // original, and run on entitlements the tenant no longer pays for. The monotonic issuance
        // counter is what makes that a rejected document and a flag rather than a free month.
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);

        long before = await harness.InScopeAsync(provider =>
            provider.GetRequiredService<ILicenceRepository>().HighestIssuanceCounterAsync());

        // Wind the vendor's counter back below the highest this store has stored, which is exactly
        // what a document kept from last month carries.
        harness.ControlPlane[harness.LicenceKey].IssuanceCounter = before - 1;

        await harness.SendAsync(new RefreshLeaseCommand());

        long after = await harness.InScopeAsync(provider =>
            provider.GetRequiredService<ILicenceRepository>().HighestIssuanceCounterAsync());

        after.Should().Be(before);

        IReadOnlyList<TamperFlag> flags = await harness.InScopeAsync(provider =>
            provider.GetRequiredService<ILicenceStateRepository>().ListUnreportedFlagsAsync());

        flags.Should().Contain(flag => flag.Kind == TamperKind.CounterRollback);
    }
}
