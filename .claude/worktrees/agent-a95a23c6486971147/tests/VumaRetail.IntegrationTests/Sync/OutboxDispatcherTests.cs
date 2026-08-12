using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using VumaRetail.Application.Abstractions.Sync;
using VumaRetail.Application.Identity.Commands;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Sync;
using VumaRetail.Infrastructure.Persistence.Repositories;
using VumaRetail.Infrastructure.Sync;
using VumaRetail.IntegrationTests.Harness;
using VumaRetail.Sync.Dispatch;

namespace VumaRetail.IntegrationTests.Sync;

/// <summary>
/// The outbox dispatcher: batching, backoff, cursor movement, and what happens when a peer answers
/// badly.
/// </summary>
/// <remarks>
/// <para>
/// Stepped one pass at a time rather than run as a loop. Sync behaviour that can only be observed by
/// waiting is sync behaviour nobody asserts — and the interesting cases here are precisely the ones a
/// running loop would paper over: an unreachable peer, a peer that acknowledges only half a batch, a
/// peer that refuses an operation outright.
/// </para>
/// <para>
/// The transport is <c>InProcessSyncTransport</c>, a production class. R1 is what is really under
/// test: <b>none of these failures may reach a till.</b> The dispatcher absorbs every one of them and
/// the sale path never learns the answer.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class OutboxDispatcherTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Ships_what_is_pending_and_marks_it_dispatched()
    {
        await using SyncHarness node = await SyncHarness.CreateAsync(fixture, "cloud", NodeKind.Cloud);

        await node.SendAsync(new CreateRoleCommand("Cashier", ["identity.user.view"]));

        int pending = await node.Context.OutboxMessages.CountAsync();
        pending.Should().BeGreaterThan(0);

        RecordingTransport transport = RecordingTransport.Accepting("store:jhb01");
        OutboxDispatcher dispatcher = Build(node, transport);

        int settled = await dispatcher.DispatchOnceAsync();

        settled.Should().Be(pending);
        transport.Batches.Should().ContainSingle();

        node.Context.ChangeTracker.Clear();

        (await node.Context.OutboxMessages.CountAsync(message => message.Status != OutboxStatus.Dispatched))
            .Should().Be(0);
    }

    [Fact]
    public async Task An_empty_outbox_ships_nothing()
    {
        await using SyncHarness node = await SyncHarness.CreateAsync(fixture, "cloud", NodeKind.Cloud);

        RecordingTransport transport = RecordingTransport.Accepting("store:jhb01");

        (await Build(node, transport).DispatchOnceAsync()).Should().Be(0);

        transport.Batches.Should().BeEmpty("a poll that found nothing must not talk to the peer at all");
    }

    [Fact]
    public async Task Ships_in_causal_order()
    {
        // By stamp, not by created_at. Shipping out of causal order would make the receiver's
        // conflict resolution reach a different answer than the sender's own history.
        await using SyncHarness node = await SyncHarness.CreateAsync(fixture, "cloud", NodeKind.Cloud);

        for (int index = 0; index < 4; index++)
        {
            await node.SendAsync(new CreateRoleCommand($"Role {index}", ["identity.user.view"]));
        }

        RecordingTransport transport = RecordingTransport.Accepting("store:jhb01");

        await Build(node, transport).DispatchOnceAsync();

        IReadOnlyList<HlcStamp> shipped = [.. transport.Batches[0].Operations.Select(operation => operation.Stamp)];

        shipped.Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task An_unreachable_peer_backs_the_rows_off_rather_than_losing_them()
    {
        // A shop's ADSL dropping is the normal case, not the exception. The rows stay, the reason is
        // recorded, and the next pass is scheduled — and nothing about this reaches a till (R1).
        await using SyncHarness node = await SyncHarness.CreateAsync(fixture, "cloud", NodeKind.Cloud);

        await node.SendAsync(new CreateRoleCommand("Cashier", ["identity.user.view"]));

        OutboxDispatcher dispatcher = Build(node, RecordingTransport.Unreachable("store:jhb01"));

        (await dispatcher.DispatchOnceAsync()).Should().Be(0);

        node.Context.ChangeTracker.Clear();

        List<OutboxMessage> failed = await node.Context.OutboxMessages.ToListAsync();

        failed.Should().OnlyContain(message => message.Status == OutboxStatus.Failed);
        failed.Should().OnlyContain(message => message.NextAttemptAt != null);
        failed[0].LastError.Should().Contain("unreachable");
        failed[0].AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task A_failed_row_is_not_retried_before_its_backoff_expires()
    {
        await using SyncHarness node = await SyncHarness.CreateAsync(fixture, "cloud", NodeKind.Cloud);

        await node.SendAsync(new CreateRoleCommand("Cashier", ["identity.user.view"]));

        await Build(node, RecordingTransport.Unreachable("store:jhb01")).DispatchOnceAsync();

        node.Context.ChangeTracker.Clear();

        RecordingTransport recovered = RecordingTransport.Accepting("store:jhb01");

        // The clock has not moved, so nothing is due yet.
        (await Build(node, recovered).DispatchOnceAsync()).Should().Be(0);
        recovered.Batches.Should().BeEmpty();

        // Past the backoff, the same rows go out — the store catches up on its own, which is the
        // whole reason MarkFailed never gives up.
        node.Clock.Advance(TimeSpan.FromMinutes(30));
        node.Context.ChangeTracker.Clear();

        (await Build(node, recovered).DispatchOnceAsync()).Should().BeGreaterThan(0);
        recovered.Batches.Should().ContainSingle();
    }

    [Fact]
    public async Task Backoff_grows_with_each_attempt_and_stops_at_the_cap()
    {
        await using SyncHarness node = await SyncHarness.CreateAsync(fixture, "cloud", NodeKind.Cloud);

        await node.SendAsync(new CreateRoleCommand("Cashier", ["identity.user.view"]));

        OutboxDispatcherOptions options = new()
        {
            Enabled = true,
            InitialBackoff = TimeSpan.FromSeconds(5),
            MaxBackoff = TimeSpan.FromMinutes(10),
        };

        List<TimeSpan> waits = [];

        for (int attempt = 0; attempt < 12; attempt++)
        {
            node.Context.ChangeTracker.Clear();

            DateTimeOffset before = node.Clock.UtcNow;
            await Build(node, RecordingTransport.Unreachable("store:jhb01"), options).DispatchOnceAsync();

            node.Context.ChangeTracker.Clear();
            OutboxMessage row = await node.Context.OutboxMessages.FirstAsync();

            waits.Add(row.NextAttemptAt!.Value - before);

            node.Clock.Advance(TimeSpan.FromHours(1));
        }

        waits[0].Should().Be(options.InitialBackoff);
        waits[1].Should().Be(TimeSpan.FromSeconds(10));
        waits[2].Should().Be(TimeSpan.FromSeconds(20));

        // Capped, not unbounded. An uncapped exponential backoff would have a store that was offline
        // for a fortnight waiting days to retry by the time anybody noticed.
        waits[^1].Should().Be(options.MaxBackoff);
        waits.Should().OnlyContain(wait => wait <= options.MaxBackoff);
    }

    [Fact]
    public async Task An_operation_the_peer_did_not_acknowledge_is_retried_rather_than_assumed()
    {
        // An incomplete acknowledgement must never be read as a complete one — that is a change the
        // sender believes is at the far end and is not.
        await using SyncHarness node = await SyncHarness.CreateAsync(fixture, "cloud", NodeKind.Cloud);

        await node.SendAsync(new CreateRoleCommand("Cashier", ["identity.user.view"]));

        OutboxDispatcher dispatcher = Build(node, RecordingTransport.AcknowledgingFirstOnly("store:jhb01"));

        await dispatcher.DispatchOnceAsync();

        node.Context.ChangeTracker.Clear();

        List<OutboxMessage> rows = await node.Context.OutboxMessages
            .OrderBy(message => message.OperationStamp)
            .ToListAsync();

        rows[0].Status.Should().Be(OutboxStatus.Dispatched);
        rows.Skip(1).Should().OnlyContain(message => message.Status == OutboxStatus.Failed);
        rows.Skip(1).Should().OnlyContain(message => message.LastError!.Contains("did not acknowledge"));
    }

    [Fact]
    public async Task A_rejected_operation_waits_at_the_cap_rather_than_being_dropped()
    {
        // A rejection is the peer saying "I will never accept this", so retrying is pointless — but
        // it is not dropped either. The row keeps the reason, which is what an investigation needs.
        await using SyncHarness node = await SyncHarness.CreateAsync(fixture, "cloud", NodeKind.Cloud);

        await node.SendAsync(new CreateRoleCommand("Cashier", ["identity.user.view"]));

        OutboxDispatcherOptions options = new() { Enabled = true, MaxBackoff = TimeSpan.FromMinutes(10) };
        DateTimeOffset before = node.Clock.UtcNow;

        await Build(node, RecordingTransport.Rejecting("store:jhb01"), options).DispatchOnceAsync();

        node.Context.ChangeTracker.Clear();

        OutboxMessage row = await node.Context.OutboxMessages.FirstAsync();

        row.Status.Should().Be(OutboxStatus.Failed);
        row.LastError.Should().Be("This node does not replicate that.", "the peer's reason is kept verbatim");
        row.NextAttemptAt.Should().Be(before + options.MaxBackoff);
    }

    [Fact]
    public async Task The_outbound_cursor_advances_on_a_successful_pass()
    {
        await using SyncHarness node = await SyncHarness.CreateAsync(fixture, "cloud", NodeKind.Cloud);

        await node.SendAsync(new CreateRoleCommand("Cashier", ["identity.user.view"]));

        RecordingTransport transport = RecordingTransport.Accepting("store:jhb01");

        await Build(node, transport).DispatchOnceAsync();

        node.Context.ChangeTracker.Clear();

        SyncCursor cursor = await node.Context.SyncCursors
            .SingleAsync(entry => entry.PeerNode == "store:jhb01" && entry.Direction == SyncDirection.Outbound);

        cursor.AcknowledgedCount.Should().BeGreaterThan(0);
        cursor.LastContactAt.Should().NotBeNull();
        cursor.Acknowledged.Should().NotBe(HlcStamp.MinValue);
    }

    [Fact]
    public async Task Rows_bound_for_a_tier_this_transport_does_not_serve_are_settled_not_re_read()
    {
        // A cloud node has StoreToCloud rows in its outbox that a cloud→store transport cannot ship.
        // Left alone they would be re-read on every pass for ever, which looks exactly like a stuck
        // queue on the health surface.
        await using SyncHarness cloud = await SyncHarness.CreateAsync(fixture, "cloud", NodeKind.Cloud);

        // Terminal is [Replicated(StoreToCloud, StoreWins)] — nothing a cloud node can send onward.
        await cloud.SendAsync(new EnrolTerminalCommand(cloud.StoreId, "T01", "Till 1"));

        RecordingTransport transport = RecordingTransport.Accepting("store:jhb01");

        (await Build(cloud, transport).DispatchOnceAsync()).Should().Be(0);

        transport.Batches.Should().BeEmpty();

        cloud.Context.ChangeTracker.Clear();

        (await cloud.Context.OutboxMessages.CountAsync(message => message.Status != OutboxStatus.Dispatched))
            .Should().Be(0);
    }

    private static OutboxDispatcher Build(
        SyncHarness node,
        ISyncTransport transport,
        OutboxDispatcherOptions? options = null)
        => new(
            new OutboxRepository(node.Context),
            new SyncCursorRepository(node.Context),
            transport,
            new NodeIdentity(node.Node),
            node.TenantContext,
            node.Context,
            node.Clock,
            options ?? new OutboxDispatcherOptions { Enabled = true },
            NullLogger<OutboxDispatcher>.Instance);

    /// <summary>A peer that records what it was sent and answers however the test needs.</summary>
    private sealed class RecordingTransport(
        string peerNode,
        Func<SyncBatch, SyncAcknowledgement> answer) : ISyncTransport
    {
        private readonly List<SyncBatch> _batches = [];

        public string PeerNode { get; } = peerNode;

        public IReadOnlyList<SyncBatch> Batches => _batches;

        public Task<SyncAcknowledgement> SendAsync(SyncBatch batch, CancellationToken cancellationToken = default)
        {
            _batches.Add(batch);

            return Task.FromResult(answer(batch));
        }

        public static RecordingTransport Accepting(string peerNode)
            => new(peerNode, batch => new SyncAcknowledgement(
                peerNode,
                [.. batch.Operations.Select(operation =>
                    new SyncOperationResult(operation.OperationId, InboxOutcome.Applied))],
                batch.Operations.Count == 0
                    ? HlcStamp.MinValue
                    : batch.Operations.Max(operation => operation.Stamp)));

        public static RecordingTransport Unreachable(string peerNode)
            => new(peerNode, _ => throw new SyncTransportException($"{peerNode} is unreachable."));

        public static RecordingTransport Rejecting(string peerNode)
            => new(peerNode, batch => new SyncAcknowledgement(
                peerNode,
                [.. batch.Operations.Select(operation => new SyncOperationResult(
                    operation.OperationId,
                    InboxOutcome.Rejected,
                    null,
                    "This node does not replicate that."))],
                HlcStamp.MinValue));

        /// <summary>A peer that answers for the first operation and silently omits the rest.</summary>
        public static RecordingTransport AcknowledgingFirstOnly(string peerNode)
            => new(peerNode, batch => new SyncAcknowledgement(
                peerNode,
                batch.Operations.Count == 0
                    ? []
                    : [new SyncOperationResult(batch.Operations[0].OperationId, InboxOutcome.Applied)],
                batch.Operations.Count == 0 ? HlcStamp.MinValue : batch.Operations[0].Stamp));
    }
}
