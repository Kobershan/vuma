using System.Text.Json;
using VumaRetail.Licensing.Metering;

namespace VumaRetail.UnitTests.Licensing;

public sealed class MeteringPayloadTests
{
    [Fact]
    public void Allowed_aggregate_payload_round_trips()
    {
        var original = new MeteringPayload(
            "node-1", "2026-09-13", "1.2.3",
            new MeteringCounts(2, 4, 3, 10, 5, 20, 4096),
            new Dictionary<string, long> { ["inventory"] = 12 },
            new MeteringHealth(1, 0, 0, 1, 1));

        MeteringPayload parsed = MeteringPayload.FromJson(original.ToJson())!;
        parsed.NodeId.Should().Be(original.NodeId);
        parsed.Period.Should().Be(original.Period);
        parsed.Version.Should().Be(original.Version);
        parsed.Counts.Should().Be(original.Counts);
        parsed.ModuleUsage.Should().Equal(original.ModuleUsage);
        parsed.Health.Should().Be(original.Health);
    }

    [Fact]
    public void Unknown_business_field_is_rejected()
    {
        string json = """
            {"NodeId":"node-1","Period":"2026-09-13","Version":"1.2.3",
             "Counts":{"Stores":1,"Terminals":1,"TerminalsOnline":1,"RegisteredUsers":1,"ActiveUsers":1,"Writes":1,"StorageBytes":1},
             "ModuleUsage":{},"Health":{"OutboxDepth":0,"SyncFailures":0,"ConflictsOpen":0,"SnapshotsTaken":0,"SnapshotsVerified":0},
             "CustomerName":"must-not-leave-the-store"}
            """;

        FluentActions.Invoking(() => MeteringPayload.FromJson(json))
            .Should().Throw<JsonException>();
    }
}
