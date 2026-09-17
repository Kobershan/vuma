using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using VumaRetail.ControlPlane;

namespace VumaRetail.ControlPlane.Tests;

public sealed class ControlPlaneStoreTests
{
    [Fact]
    public async Task Activation_is_idempotent_and_changed_replay_is_rejected()
    {
        var store = new ControlPlaneStore();
        var signer = new TestSigner();
        ActivationRequest request = new(Guid.NewGuid(), "licence-1", "install-1", "fp-1", "1.0");

        DeviceResponse first = await store.ActivateAsync(request, signer, CancellationToken.None);
        DeviceResponse replay = await store.ActivateAsync(request, signer, CancellationToken.None);

        replay.Should().BeEquivalentTo(first);
        await FluentActions.Invoking(() => store.ActivateAsync(request with { Fingerprint = "fp-2" }, signer, CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
        signer.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Signer_failure_happens_before_activation_is_recorded()
    {
        var store = new ControlPlaneStore();
        ActivationRequest request = new(Guid.NewGuid(), "licence-1", "install-1", "fp-1", "1.0");
        var signer = new FailingSigner();

        await FluentActions.Invoking(() => store.ActivateAsync(request, signer, CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Invoking(() => store.ActivateAsync(request, new TestSigner(), CancellationToken.None))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task Metering_accepts_allowlisted_nonnegative_counts_only_for_known_nodes()
    {
        var store = new ControlPlaneStore();
        DeviceResponse node = await store.ActivateAsync(
            new ActivationRequest(Guid.NewGuid(), "licence-1", "install-1", "fp-1", "1.0"),
            new TestSigner(), CancellationToken.None);

        var metering = new MeteringRequest(Guid.NewGuid(), node.NodeId, new DateOnly(2026, 9, 15),
            new MeteringCounts(1, 2, 3, 1, 1, 10, 4, 0, 0),
            new Dictionary<string, long> { ["sales"] = 1 }, new MeteringHealth(0, 0, 0, 0));
        await store.AcceptMeteringAsync(metering);
        await FluentActions.Invoking(() => store.AcceptMeteringAsync(metering with
            { Counts = metering.Counts with { Transactions = -1 } }))
            .Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Lease_and_heartbeat_replays_return_the_original_response()
    {
        var store = new ControlPlaneStore();
        var signer = new TestSigner();
        DeviceResponse node = await store.ActivateAsync(
            new ActivationRequest(Guid.NewGuid(), "licence-1", "install-2", "fp-2", "1.0"), signer, CancellationToken.None);
        LeaseRequest lease = new(Guid.NewGuid(), node.NodeId, node.LeaseId!, "fp-2", 1,
            DateTimeOffset.UtcNow, "boot-1", "1.0");
        DeviceResponse firstLease = await store.RefreshLeaseAsync(lease, signer, CancellationToken.None);
        (await store.RefreshLeaseAsync(lease, signer, CancellationToken.None)).Should().BeEquivalentTo(firstLease);
        HeartbeatRequest heartbeat = new(Guid.NewGuid(), node.NodeId, DateTimeOffset.UtcNow, 10, "1.0",
            1, 1, 0, 0, null, null, 1, "boot-1", false, "ok", 0);
        (await store.HeartbeatAsync(heartbeat)).Should().BeEquivalentTo(await store.HeartbeatAsync(heartbeat));
    }

    [Fact]
    public async Task Device_state_and_request_replays_survive_a_new_store_instance()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        DbContextOptions<ControlPlaneDbContext> options = new DbContextOptionsBuilder<ControlPlaneDbContext>()
            .UseSqlite(connection).Options;
        ActivationRequest request = new(Guid.NewGuid(), "licence-1", "persistent-install", "fp", "1.0");
        await using (ControlPlaneDbContext database = new(options))
        {
            await database.Database.EnsureCreatedAsync();
            var store = new ControlPlaneStore(database);
            await store.ActivateAsync(request, new TestSigner(), CancellationToken.None);
        }

        await using (ControlPlaneDbContext database = new(options))
        {
            var store = new ControlPlaneStore(database);
            DeviceResponse first = await store.ActivateAsync(request, new TestSigner(), CancellationToken.None);
            first.Should().NotBeNull();
            database.AuditEntries.Should().ContainSingle(x => x.Action == "device.activation");
        }
    }

    [Fact]
    public async Task Metering_is_deduplicated_by_node_and_period_across_store_instances()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        DbContextOptions<ControlPlaneDbContext> options = new DbContextOptionsBuilder<ControlPlaneDbContext>()
            .UseSqlite(connection).Options;
        string nodeId;
        await using (ControlPlaneDbContext database = new(options))
        {
            await database.Database.EnsureCreatedAsync();
            nodeId = (await new ControlPlaneStore(database).ActivateAsync(
                new ActivationRequest(Guid.NewGuid(), "licence-1", "metering-install", "fp", "1.0"),
                new TestSigner(), CancellationToken.None)).NodeId;
        }
        MeteringRequest metering = new(Guid.NewGuid(), nodeId, new DateOnly(2026, 9, 15),
            new MeteringCounts(1, 1, 1, 1, 1, 1, 1, 1, 1), new Dictionary<string, long>(),
            new MeteringHealth(0, 0, 0, 0));
        await using (ControlPlaneDbContext database = new(options))
            await new ControlPlaneStore(database).AcceptMeteringAsync(metering);
        await using (ControlPlaneDbContext database = new(options))
        {
            var store = new ControlPlaneStore(database);
            await store.AcceptMeteringAsync(metering);
            await FluentActions.Invoking(() => store.AcceptMeteringAsync(metering with { RequestId = Guid.NewGuid() }))
                .Should().ThrowAsync<InvalidOperationException>();
        }
    }

    private sealed class TestSigner : ILicenseSigner
    {
        public int Calls { get; private set; }
        public Task<string> SignAsync(string payload, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult($"sig:{payload}"); }
    }

    [Fact]
    public async Task Rebind_rotates_fingerprint_and_lease_and_rejects_unknown_nodes()
    {
        var store = new ControlPlaneStore();
        var signer = new TestSigner();
        DeviceResponse node = await store.ActivateAsync(
            new ActivationRequest(Guid.NewGuid(), "licence-1", "rebind-install", "fp-1", "1.0"),
            signer, CancellationToken.None);
        RebindRequest rebind = new(Guid.NewGuid(), node.NodeId, "fp-2", "1.1");

        DeviceResponse first = await store.RebindAsync(rebind, signer, CancellationToken.None);
        first.LeaseId.Should().NotBeNullOrWhiteSpace();
        first.LeaseId.Should().NotBe(node.LeaseId);
        (await store.RebindAsync(rebind, signer, CancellationToken.None)).Should().BeEquivalentTo(first);
        signer.Calls.Should().Be(2);

        var unknown = () => store.RebindAsync(
            new RebindRequest(Guid.NewGuid(), "no-such-node", "fp-x", "1.1"), signer, CancellationToken.None);
        await unknown.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Revoke_removes_the_node_but_stays_idempotent()
    {
        var store = new ControlPlaneStore();
        var signer = new TestSigner();
        DeviceResponse node = await store.ActivateAsync(
            new ActivationRequest(Guid.NewGuid(), "licence-1", "revoke-install", "fp-1", "1.0"),
            signer, CancellationToken.None);
        Guid revokeId = Guid.NewGuid();

        await store.RevokeAsync(revokeId, node.NodeId);
        await store.RevokeAsync(revokeId, node.NodeId);
        var heartbeat = new HeartbeatRequest(Guid.NewGuid(), node.NodeId, DateTimeOffset.UtcNow, 10, "1.0",
            1, 1, 0, 0, null, null, 1, "boot-1", false, "ok", 0);
        await FluentActions.Invoking(() => store.HeartbeatAsync(heartbeat)).Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Diagnostics_and_telemetry_errors_are_idempotent_and_require_known_nodes()
    {
        var store = new ControlPlaneStore();
        DeviceResponse node = await store.ActivateAsync(
            new ActivationRequest(Guid.NewGuid(), "licence-1", "diag-install", "fp-1", "1.0"),
            new TestSigner(), CancellationToken.None);
        DiagnosticsRequest diagnostics = new(Guid.NewGuid(), node.NodeId, DateTimeOffset.UtcNow, "printer", "paper low");
        await store.RecordDiagnosticsAsync(diagnostics);
        await store.RecordDiagnosticsAsync(diagnostics);
        TelemetryErrorRequest error = new(Guid.NewGuid(), node.NodeId, DateTimeOffset.UtcNow, "sync", "timeout");
        await store.RecordTelemetryErrorAsync(error);
        await store.RecordTelemetryErrorAsync(error);

        var unknown = () => store.RecordDiagnosticsAsync(
            new DiagnosticsRequest(Guid.NewGuid(), "no-such-node", DateTimeOffset.UtcNow, "printer", "x"));
        await unknown.Should().ThrowAsync<KeyNotFoundException>();
    }

    private sealed class FailingSigner : ILicenseSigner
    {
        public Task<string> SignAsync(string payload, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("signer unavailable");
    }
}
