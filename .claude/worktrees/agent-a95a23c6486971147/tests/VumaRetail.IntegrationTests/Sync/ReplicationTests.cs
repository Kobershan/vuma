using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Abstractions.Sync;
using VumaRetail.Application.Identity.Commands;
using VumaRetail.Domain.Identity;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Sync;
using VumaRetail.IntegrationTests.Harness;
using VumaRetail.Sync.Commands;
using VumaRetail.Sync.Protocol;

namespace VumaRetail.IntegrationTests.Sync;

/// <summary>
/// A cloud and a store, two real databases, one protocol (ADR-006, ADR-007).
/// </summary>
/// <remarks>
/// <para>
/// The direction is <b>cloud → store</b> because that is the direction the entities under test
/// actually declare. Roles, grants and assignments are <c>[Replicated(CloudToStore, CloudWins)]</c>:
/// head office decides who may do what and the shops receive it. Testing them the other way round
/// would be testing a scenario the system is designed to refuse — which the first draft of this file
/// did, and which is how the direction came to be checked at all.
/// </para>
/// <para>
/// Everything runs through the real command pipeline at both ends, so the outbox behaviour in slot
/// 300 is exercised inside the transaction behaviour in slot 200. The assertions that matter are the
/// ones about repetition: an at-least-once transport delivers twice, and every claim Vuma makes about
/// exactly-once <em>effect</em> rests on the second delivery changing nothing.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class ReplicationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task A_change_captured_on_the_cloud_lands_on_the_store()
    {
        await using SyncHarness cloud = await CloudAsync();
        await using SyncHarness store = await StoreFor(cloud);

        await cloud.SendAsync(new CreateRoleCommand("Cashier", ["identity.user.view"], "Till operator"));

        SyncAcknowledgement ack = await ShipAsync(cloud, store);

        ack.Results.Should().OnlyContain(result => result.Outcome == InboxOutcome.Applied);

        Role landed = await store.Context.Roles.SingleAsync(role => role.Name == "Cashier");
        landed.Description.Should().Be("Till operator");
        landed.TenantId.Should().Be(cloud.TenantId);
    }

    [Fact]
    public async Task The_applied_row_keeps_the_originating_stamp_and_origin()
    {
        // Two properties in one. The stamp must be the *sender's*, or the receiving node's copy would
        // look newer than the original and the next comparison would undo it. And created_by must be
        // the person who really made the change, not "system:sync" — R6's "who" survives replication.
        await using SyncHarness cloud = await CloudAsync();
        await using SyncHarness store = await StoreFor(cloud);

        Guid roleId = await cloud.SendAsync(new CreateRoleCommand("Manager", ["identity.role.view"]));

        await ShipAsync(cloud, store);

        Role onCloud = await cloud.Context.Roles.SingleAsync(role => role.Id == roleId);
        Role onStore = await store.Context.Roles.SingleAsync(role => role.Id == roleId);

        onStore.SyncStamp.Should().Be(onCloud.SyncStamp);
        onStore.SyncState.Should().Be(SyncState.Synced);

        // Asserted against the literal, not just against the other node's value. The two nodes act
        // as different principals precisely so this cannot pass by both being blank — which is how
        // it passed before the outbox was made to stamp before it serialises.
        onCloud.CreatedBy.Should().Be("user:cloud");
        onStore.CreatedBy.Should().Be("user:cloud", "the origin survives replication (R6)");
        onStore.CreatedAt.Should().Be(onCloud.CreatedAt).And.NotBe(default(DateTimeOffset));
    }

    [Fact]
    public async Task Replaying_a_batch_applies_it_once()
    {
        // The whole reason the inbox exists. The transport is at-least-once by design, so a replay is
        // the normal way a resumed upload finishes — not an error path.
        await using SyncHarness cloud = await CloudAsync();
        await using SyncHarness store = await StoreFor(cloud);

        await cloud.SendAsync(new CreateRoleCommand("Cashier", ["identity.user.view"]));

        SyncBatch batch = await BuildBatchAsync(cloud);

        SyncAcknowledgement first = await store.SendAsync(new ReceiveSyncBatchCommand(batch));
        SyncAcknowledgement second = await store.SendAsync(new ReceiveSyncBatchCommand(batch));

        first.Results.Should().OnlyContain(result => result.Outcome == InboxOutcome.Applied);
        second.Results.Should().OnlyContain(result => result.Outcome == InboxOutcome.Duplicate);

        (await store.Context.Roles.CountAsync(role => role.Name == "Cashier")).Should().Be(1);
        (await store.Context.InboxMessages.CountAsync()).Should().Be(batch.Operations.Count);
    }

    [Fact]
    public async Task A_batch_carrying_the_same_operation_twice_applies_it_once()
    {
        // Not the same as a replay: this is one delivery with a duplicate inside it, which is what a
        // sender with a retry bug produces. The change tracker has to catch it, because neither copy
        // has reached the database yet for the unique index to reject.
        await using SyncHarness cloud = await CloudAsync();
        await using SyncHarness store = await StoreFor(cloud);

        await cloud.SendAsync(new CreateRoleCommand("Cashier", ["identity.user.view"]));

        SyncBatch original = await BuildBatchAsync(cloud);
        SyncBatch doubled = original with { Operations = [.. original.Operations, .. original.Operations] };

        SyncAcknowledgement ack = await store.SendAsync(new ReceiveSyncBatchCommand(doubled));

        ack.Results.Count(result => result.Outcome == InboxOutcome.Applied)
            .Should().Be(original.Operations.Count);
        ack.Results.Count(result => result.Outcome == InboxOutcome.Duplicate)
            .Should().Be(original.Operations.Count);

        (await store.Context.Roles.CountAsync(role => role.Name == "Cashier")).Should().Be(1);
    }

    [Fact]
    public async Task A_replicated_delete_soft_deletes_the_row_at_the_far_end()
    {
        await using SyncHarness cloud = await CloudAsync();
        await using SyncHarness store = await StoreFor(cloud);

        Guid roleId = await cloud.SendAsync(new CreateRoleCommand("Temporary", ["identity.user.view"]));
        await ShipAsync(cloud, store);

        await cloud.SendAsync(new DeleteRoleCommand(roleId));
        await ShipAsync(cloud, store);

        store.Context.ChangeTracker.Clear();

        // Filtered out of the ordinary query, because §7 rule 8 holds on every tier — a replicated
        // hard delete would be a way to erase a row the audit trail says still exists.
        (await store.Context.Roles.AnyAsync(role => role.Id == roleId)).Should().BeFalse();

        Role tombstone = await store.Context.Roles
            .IgnoreQueryFilters()
            .SingleAsync(role => role.Id == roleId);

        tombstone.IsDeleted.Should().BeTrue();

        // The person who deleted the record, not the machine that replicated it. The sending node
        // stamps before it serialises, so the deletion arrives with its origin already on it — which
        // is R6's answer to "who deleted this", and it is the same answer on every tier.
        tombstone.DeletedBy.Should().Be("user:cloud");
    }

    [Fact]
    public async Task A_late_upsert_does_not_resurrect_a_deleted_row()
    {
        // The failure this guards against is a deleted product coming back on the shelf after every
        // reconnect.
        await using SyncHarness cloud = await CloudAsync();
        await using SyncHarness store = await StoreFor(cloud);

        Guid roleId = await cloud.SendAsync(new CreateRoleCommand("Temporary", ["identity.user.view"]));

        SyncBatch creation = await BuildBatchAsync(cloud);

        await cloud.SendAsync(new DeleteRoleCommand(roleId));
        await ShipAsync(cloud, store);

        // The creation batch arrives again, late — the classic resumed-upload replay. Its operation
        // ids are already in the inbox, so it settles as a duplicate and touches nothing.
        await store.SendAsync(new ReceiveSyncBatchCommand(creation));

        store.Context.ChangeTracker.Clear();

        Role role = await store.Context.Roles.IgnoreQueryFilters().SingleAsync(entry => entry.Id == roleId);
        role.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task A_conflicting_change_is_settled_by_the_entitys_own_policy()
    {
        // Role is [Replicated(CloudToStore, CloudWins)]. The store has its own version of the row and
        // the cloud sends a different one, so the cloud's wins — whatever the stamps say, and without
        // the sender getting any say in it.
        await using SyncHarness cloud = await CloudAsync();
        await using SyncHarness store = await StoreFor(cloud);

        Guid roleId = await cloud.SendAsync(new CreateRoleCommand("Supervisor", ["identity.user.view"], "v1"));
        await ShipAsync(cloud, store);

        // The store edits its copy, then the cloud edits and re-sends. CloudWins settles it.
        Role local = await store.Context.Roles.SingleAsync(role => role.Id == roleId);
        local.SetDetails("Supervisor", "edited on the store");
        await store.Context.CommitAsync();

        SyncBatch update = new(
            cloud.Node.NodeId,
            NodeKind.Cloud,
            cloud.TenantId,
            cloud.StoreId,
            [
                new SyncOperation(
                    UuidV7.NewGuid(),
                    nameof(Role),
                    roleId,
                    SyncOperationKind.Upsert,
                    cloud.HybridClock.Next(),
                    """{"Name":"Supervisor","NormalizedName":"SUPERVISOR","Description":"v2 from head office","Id":"REPLACE","TenantId":"TENANT","StoreId":null,"CreatedAt":"2026-01-01T08:00:00+00:00","CreatedBy":"user:test","UpdatedAt":"2026-01-01T08:00:00+00:00","UpdatedBy":"user:test","DeletedAt":null,"DeletedBy":null}"""
                        .Replace("REPLACE", roleId.ToString(), StringComparison.Ordinal)
                        .Replace("TENANT", cloud.TenantId.ToString(), StringComparison.Ordinal),
                    cloud.Clock.UtcNow),
            ]);

        SyncAcknowledgement ack = await store.SendAsync(new ReceiveSyncBatchCommand(update));

        ack.Results.Should().ContainSingle().Which.Outcome.Should().Be(InboxOutcome.Applied);

        store.Context.ChangeTracker.Clear();

        Role settled = await store.Context.Roles.SingleAsync(role => role.Id == roleId);
        settled.Description.Should().Be("v2 from head office");
    }

    [Fact]
    public async Task An_entity_the_node_does_not_replicate_is_refused_rather_than_ignored()
    {
        // The sender has a bug and silence would keep it. It is also the check that stops a peer
        // naming an arbitrary CLR type and having the receiver materialise it — a deserialisation
        // gadget with an HTTP endpoint in front of it.
        await using SyncHarness cloud = await CloudAsync();
        await using SyncHarness store = await StoreFor(cloud);

        SyncBatch batch = OneOperation(cloud, "System.IO.FileInfo", """{"Name":"/etc/passwd"}""");

        SyncAcknowledgement ack = await store.SendAsync(new ReceiveSyncBatchCommand(batch));

        ack.Results.Should().ContainSingle().Which.Outcome.Should().Be(InboxOutcome.Rejected);
    }

    [Fact]
    public async Task A_node_local_entity_is_refused()
    {
        await using SyncHarness cloud = await CloudAsync();
        await using SyncHarness store = await StoreFor(cloud);

        SyncBatch batch = OneOperation(cloud, nameof(OutboxMessage), "{}");

        SyncAcknowledgement ack = await store.SendAsync(new ReceiveSyncBatchCommand(batch));

        ack.Results.Should().ContainSingle().Which.Outcome.Should().Be(InboxOutcome.Rejected);
    }

    [Fact]
    public async Task One_poisoned_operation_does_not_take_down_the_rest_of_the_batch()
    {
        // Without per-operation isolation, one unparseable payload fails the whole batch — and
        // because the dispatcher re-selects the same oldest-stamped rows on every pass, that
        // operation is in every subsequent batch too. The store would stop replicating entirely,
        // indefinitely, because of one bad row, and the only symptom would be a queue depth that
        // climbs and never falls.
        await using SyncHarness cloud = await CloudAsync();
        await using SyncHarness store = await StoreFor(cloud);

        await cloud.SendAsync(new CreateRoleCommand("Cashier", ["identity.user.view"]));

        SyncBatch good = await BuildBatchAsync(cloud);

        SyncBatch poisoned = good with
        {
            Operations =
            [
                new SyncOperation(
                    UuidV7.NewGuid(),
                    nameof(Role),
                    UuidV7.NewGuid(),
                    SyncOperationKind.Upsert,
                    new HlcStamp(1, 0, cloud.Node.NodeId),
                    "this is not json",
                    cloud.Clock.UtcNow),
                .. good.Operations,
            ],
        };

        SyncAcknowledgement ack = await store.SendAsync(new ReceiveSyncBatchCommand(poisoned));

        ack.Results.Count(result => result.Outcome == InboxOutcome.Rejected).Should().Be(1);
        ack.Results.Count(result => result.Outcome == InboxOutcome.Applied).Should().Be(good.Operations.Count);

        // The good operations landed, which is the whole point.
        (await store.Context.Roles.CountAsync(role => role.Name == "Cashier")).Should().Be(1);

        // And the rejection is recorded with its reason, so the poisoned row is diagnosable rather
        // than merely stuck.
        InboxMessage rejected = await store.Context.InboxMessages
            .SingleAsync(message => message.Outcome == InboxOutcome.Rejected);

        rejected.Detail.Should().Contain("SYNC_PAYLOAD_MALFORMED");
    }

    [Fact]
    public async Task A_batch_for_another_tenant_is_refused_outright()
    {
        // The one check between "a node may replicate its own tenant's data" and "a node may write
        // any row in the estate". A refusal rather than a filter: a batch that named the wrong tenant
        // is a bug or an attack, and neither improves by having part of it silently dropped.
        await using SyncHarness cloud = await CloudAsync();
        await using SyncHarness store = await StoreFor(cloud);

        await cloud.SendAsync(new CreateRoleCommand("Cashier", ["identity.user.view"]));

        SyncBatch batch = await BuildBatchAsync(cloud) with { TenantId = Guid.NewGuid() };

        Func<Task> foreign = () => store.SendAsync(new ReceiveSyncBatchCommand(batch));

        (await foreign.Should().ThrowAsync<SyncTenantMismatchException>())
            .Which.Code.Should().Be("SYNC_TENANT_MISMATCH");
    }

    [Fact]
    public async Task The_inbound_cursor_advances_to_what_actually_settled()
    {
        await using SyncHarness cloud = await CloudAsync();
        await using SyncHarness store = await StoreFor(cloud);

        await cloud.SendAsync(new CreateRoleCommand("Cashier", ["identity.user.view"]));

        SyncBatch batch = await BuildBatchAsync(cloud);
        SyncAcknowledgement ack = await store.SendAsync(new ReceiveSyncBatchCommand(batch));

        SyncCursor cursor = await store.Context.SyncCursors
            .SingleAsync(entry => entry.PeerNode == cloud.Node.NodeId && entry.Direction == SyncDirection.Inbound);

        cursor.Acknowledged.Should().Be(ack.AcknowledgedStamp);
        cursor.AcknowledgedCount.Should().Be(batch.Operations.Count);
        cursor.LastContactAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Applying_a_remote_batch_captures_nothing_for_replication()
    {
        // Loop prevention. Without it, the store applies a change from the cloud, captures it, sends
        // it back, the cloud applies it and captures it, and two machines pass one row back and
        // forth until somebody notices the disk filling.
        await using SyncHarness cloud = await CloudAsync();
        await using SyncHarness store = await StoreFor(cloud);

        await cloud.SendAsync(new CreateRoleCommand("Cashier", ["identity.user.view"]));
        await ShipAsync(cloud, store);

        (await store.Context.OutboxMessages.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_local_change_after_a_remote_batch_is_still_captured()
    {
        // The other half of loop prevention, and the half that is easy to break: suppressing capture
        // for a whole request would silently stop the node recording its *own* subsequent changes.
        await using SyncHarness cloud = await CloudAsync();
        await using SyncHarness store = await StoreFor(cloud);

        await cloud.SendAsync(new CreateRoleCommand("Cashier", ["identity.user.view"]));
        await ShipAsync(cloud, store);

        await store.SendAsync(new EnrolTerminalCommand(store.StoreId, "T01", "Till 1"));

        (await store.Context.OutboxMessages.CountAsync(message => message.EntityType == nameof(Terminal)))
            .Should().Be(1);
    }

    [Fact]
    public async Task Receiving_a_stamp_from_the_future_moves_the_receivers_clock_past_it()
    {
        // The hybrid half of the clock, end to end. A node whose own next stamp ordered *before* a
        // change it had already applied would undo that change on the next comparison.
        await using SyncHarness cloud = await CloudAsync();
        await using SyncHarness store = await StoreFor(cloud);

        cloud.Clock.Advance(TimeSpan.FromHours(5));

        await cloud.SendAsync(new CreateRoleCommand("Cashier", ["identity.user.view"]));

        SyncBatch batch = await BuildBatchAsync(cloud);
        HlcStamp highest = batch.Operations.Max(operation => operation.Stamp);

        await store.SendAsync(new ReceiveSyncBatchCommand(batch));

        (store.HybridClock.Next() > highest).Should().BeTrue();
    }

    private Task<SyncHarness> CloudAsync()
        => SyncHarness.CreateAsync(fixture, "cloud", NodeKind.Cloud);

    /// <summary>
    /// A store node over its own database, holding the same tenant and store rows as the cloud.
    /// </summary>
    /// <remarks>
    /// Two databases, not two contexts over one. A shared database would let a test pass because the
    /// row was already there rather than because it replicated, which is the one thing these tests
    /// exist to tell apart.
    /// </remarks>
    private Task<SyncHarness> StoreFor(SyncHarness cloud)
        => SyncHarness.CreateAsync(fixture, "store:jhb01", NodeKind.Store, cloud.TenantId, cloud.StoreId);

    private static SyncBatch OneOperation(SyncHarness sender, string entityType, string payload)
        => new(
            sender.Node.NodeId,
            sender.Node.Kind,
            sender.TenantId,
            sender.StoreId,
            [
                new SyncOperation(
                    UuidV7.NewGuid(),
                    entityType,
                    Guid.NewGuid(),
                    SyncOperationKind.Upsert,
                    sender.HybridClock.Next(),
                    payload,
                    sender.Clock.UtcNow),
            ]);

    /// <summary>Builds a batch from everything currently pending in the node's outbox.</summary>
    private static async Task<SyncBatch> BuildBatchAsync(SyncHarness node)
    {
        List<OutboxMessage> pending = await node.Context.OutboxMessages
            .Where(message => message.Status != OutboxStatus.Dispatched)
            .OrderBy(message => message.OperationStamp)
            .ToListAsync();

        return new SyncBatch(
            node.Node.NodeId,
            node.Node.Kind,
            node.TenantId,
            node.StoreId,
            [.. pending.Select(message => new SyncOperation(
                message.OperationId,
                message.EntityType,
                message.EntityId,
                message.Operation,
                message.OperationHlc,
                message.Payload,
                message.OccurredAt))]);
    }

    /// <summary>Ships everything pending and settles the sender's outbox, as the dispatcher would.</summary>
    private static async Task<SyncAcknowledgement> ShipAsync(SyncHarness from, SyncHarness to)
    {
        SyncBatch batch = await BuildBatchAsync(from);

        SyncAcknowledgement ack = await to.SendAsync(new ReceiveSyncBatchCommand(batch));

        foreach (OutboxMessage message in await from.Context.OutboxMessages
            .Where(entry => entry.Status != OutboxStatus.Dispatched)
            .ToListAsync())
        {
            message.MarkDispatched(from.Clock.UtcNow);
        }

        await from.Context.CommitAsync();

        return ack;
    }
}
