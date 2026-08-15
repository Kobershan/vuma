using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Abstractions.Sync;
using VumaRetail.Application.Identity.Commands;
using VumaRetail.Domain.Licensing;
using VumaRetail.Domain.Platform;
using VumaRetail.Domain.Primitives;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.IntegrationTests.Api;
using VumaRetail.IntegrationTests.Harness;
using VumaRetail.Licensing.Commands;
using VumaRetail.Licensing.Metering;

namespace VumaRetail.IntegrationTests.Licensing;

/// <summary>
/// The two Stage 04b tests the stage brief named and left outstanding (<c>docs/PROGRESS.md</c> §5).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class MeteringTests(PostgresFixture fixture)
{
    /// <summary>
    /// The whitelist schema the serialised payload may contain, and nothing else
    /// (<c>docs/TESTING.md</c> §7, <c>LICENSING.md</c> §9).
    /// </summary>
    private static readonly HashSet<string> AllowedTopLevelProperties = new(StringComparer.Ordinal)
    {
        "NodeId", "Period", "Version", "Counts", "ModuleUsage", "Health",
    };

    private static readonly HashSet<string> AllowedCountsProperties = new(StringComparer.Ordinal)
    {
        "Stores", "Terminals", "TerminalsOnline", "RegisteredUsers", "ActiveUsers", "Writes", "StorageBytes",
    };

    private static readonly HashSet<string> AllowedHealthProperties = new(StringComparer.Ordinal)
    {
        "OutboxDepth", "SyncFailures", "ConflictsOpen", "SnapshotsTaken", "SnapshotsVerified",
    };

    [Fact]
    public async Task The_metering_payload_contains_nothing_but_the_whitelisted_counters()
    {
        // A fully seeded tenant, so the assertion is meaningful rather than vacuous: real names, a
        // real email, a real store address and a day of real audited writes across the identity and
        // platform modules, all of which must be structurally unreachable from the payload rather than
        // merely absent by chance.
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);

        const string OwnerName = "Thandiwe Nkosi";
        const string SecondName = "Sipho Dlamini";
        const string SecondEmail = "sipho.dlamini@example.co.za";
        Address storeAddress = Address.Create("12 Rivonia Road", "Sandton", "ZA", "care of Thandiwe Nkosi", null, "2196");

        await harness.CreateUserAsync(OwnerName.Replace(' ', '.'));

        await harness.InScopeAsync(async provider =>
        {
            IDispatcher dispatcher = provider.GetRequiredService<IDispatcher>();

            await dispatcher.SendAsync(new CreateUserCommand(
                "sipho.dlamini", SecondName, "CorrectHorseBattery1", SecondEmail));

            return Unit.Value;
        });

        await harness.InScopeAsync(async provider =>
        {
            VumaRetailDbContext context = provider.GetRequiredService<VumaRetailDbContext>();
            Store store = await context.Stores.SingleAsync();

            store.SetDetails(store.Name, storeAddress);
            await context.CommitAsync();

            return Unit.Value;
        });

        DateOnly period = DateOnly.FromDateTime(harness.Clock.UtcNow.UtcDateTime);
        harness.Clock.Advance(TimeSpan.FromDays(1));

        await harness.SendAsync(new RollUpMeteringCommand(period));

        MeteringPayload payload = await harness.InScopeAsync(async provider =>
        {
            IMeteringRepository repository = provider.GetRequiredService<IMeteringRepository>();
            string nodeId = provider.GetRequiredService<INodeIdentity>().NodeId;

            MeteringRecord record = (await repository.FindAsync(nodeId, period))!;

            return MeteringPayload.FromJson(record.Payload)!;
        });

        string json = payload.ToJson();

        // Nothing a human wrote leaves the premises: not the names, not the email, not the address,
        // not even fragments of them.
        json.Should().NotContain("Thandiwe");
        json.Should().NotContain("Nkosi");
        json.Should().NotContain("Sipho");
        json.Should().NotContain("Dlamini");
        json.Should().NotContain(SecondEmail);
        json.Should().NotContain("Rivonia");
        json.Should().NotContain("Sandton");
        json.Should().NotContain("@");

        // Structurally: every top-level property is on the whitelist, and so is everything nested.
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        foreach (JsonProperty property in root.EnumerateObject())
        {
            AllowedTopLevelProperties.Should().Contain(property.Name);
        }

        foreach (JsonProperty property in root.GetProperty("Counts").EnumerateObject())
        {
            AllowedCountsProperties.Should().Contain(property.Name);
            property.Value.ValueKind.Should().Be(JsonValueKind.Number);
        }

        foreach (JsonProperty property in root.GetProperty("Health").EnumerateObject())
        {
            AllowedHealthProperties.Should().Contain(property.Name);
            property.Value.ValueKind.Should().Be(JsonValueKind.Number);
        }

        // Module usage keys are module names from the declared manifests — never a business-table
        // name, a document id or free text — and every value is a plain count.
        HashSet<string> declaredModules = harness.Services
            .GetServices<IModuleManifest>()
            .Select(manifest => manifest.Module)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (JsonProperty usage in root.GetProperty("ModuleUsage").EnumerateObject())
        {
            declaredModules.Should().Contain(usage.Name);
            usage.Value.ValueKind.Should().Be(JsonValueKind.Number);
        }

        // NodeId, Period and Version are the only strings in the document, and none of them is free
        // text a person wrote.
        root.GetProperty("NodeId").GetString().Should().NotBeNullOrWhiteSpace();
        root.GetProperty("Period").GetString().Should().Be(period.ToString("yyyy-MM-dd"));
    }

    [Fact]
    public async Task Offline_for_forty_five_days_then_reconnected_sends_every_missed_day_exactly_once()
    {
        // Modelled as forty-five days of undelivered backlog rather than as forty-five days of an
        // unreachable control plane: the latter would itself cross the Path A read-only boundary at
        // its default of fifteen days (LicensingOptions.OfflineReadOnlyAfterDays) partway through, and
        // that boundary — and the guarantee that no vendor-side outage restricts anyone — is already
        // exhaustively covered by NoAccidentalLockoutTests. What this test is about is narrower and is
        // exactly what docs/stages/STAGE-04b-licensing.md and PROGRESS.md §5 ask for: a rollup queued
        // every day for forty-five days, none of it delivered yet, and one catch-up pass afterwards
        // that sends every single day exactly once. A daily heartbeat keeps the lease and the
        // last-contact timestamp current, exactly as a store trading normally would.
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);

        DateOnly firstPeriod = DateOnly.FromDateTime(harness.Clock.UtcNow.UtcDateTime);
        const int OfflineDays = 45;

        for (int day = 0; day < OfflineDays; day++)
        {
            DateOnly period = firstPeriod.AddDays(day);

            await harness.SendAsync(new SendHeartbeatCommand());
            await harness.SendAsync(new RollUpMeteringCommand(period));

            harness.Clock.Advance(TimeSpan.FromDays(1));
        }

        harness.ControlPlane.Metering.Should().BeEmpty("nothing has been delivered yet");
        harness.ControlPlane.MeteringDeliveries.Should().Be(0);

        // One delivery pass catches up every missed day — LicensingOptions's default batch size is
        // 45, matching this scenario exactly.
        MeteringDeliveryResult caughtUp = await harness.SendAsync(new DeliverMeteringCommand());

        caughtUp.Sent.Should().Be(OfflineDays);
        caughtUp.Outstanding.Should().Be(0);

        harness.ControlPlane.MeteringDeliveries.Should().Be(OfflineDays);

        IReadOnlyCollection<DateOnly> deliveredPeriods = [.. harness.ControlPlane.Metering.Keys.Select(key => key.Period)];

        deliveredPeriods.Should().OnlyHaveUniqueItems("each day must arrive exactly once, never twice");
        deliveredPeriods.Should().HaveCount(OfflineDays);

        for (int day = 0; day < OfflineDays; day++)
        {
            deliveredPeriods.Should().Contain(firstPeriod.AddDays(day));
        }

        // Redelivering after the fact does not resend what has already been accepted — idempotent by
        // (node, period), not merely "did not crash".
        MeteringDeliveryResult redelivered = await harness.SendAsync(new DeliverMeteringCommand());
        redelivered.Sent.Should().Be(0);
        harness.ControlPlane.MeteringDeliveries.Should().Be(OfflineDays);
    }
}
