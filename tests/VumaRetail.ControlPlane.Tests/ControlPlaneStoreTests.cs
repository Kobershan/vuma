using FluentAssertions;
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
        store.AcceptMetering(metering);
        FluentActions.Invoking(() => store.AcceptMetering(metering with
            { Counts = metering.Counts with { Transactions = -1 } }))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    private sealed class TestSigner : ILicenseSigner
    {
        public int Calls { get; private set; }
        public Task<string> SignAsync(string payload, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult($"sig:{payload}"); }
    }

    private sealed class FailingSigner : ILicenseSigner
    {
        public Task<string> SignAsync(string payload, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("signer unavailable");
    }
}
