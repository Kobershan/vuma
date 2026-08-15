using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Identity.Commands;
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Sync;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Sync;

/// <summary>
/// The transactional outbox (ADR-006), against a real database and the real pipeline.
/// </summary>
/// <remarks>
/// The claim under test is atomicity: the outbox row and the change it describes commit together or
/// not at all. That cannot be asserted against a mock — a mocked unit of work commits whenever the
/// test says so — so every one of these runs a command through the real dispatcher, with the outbox
/// behaviour in slot 300 inside the transaction behaviour in slot 200.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class OutboxTests(PostgresFixture fixture)
{
    [Fact]
    public async Task A_command_that_changes_a_replicated_entity_writes_one_outbox_row()
    {
        await using SyncHarness node = await SyncHarness.CreateAsync(fixture);

        await node.SendAsync(new CreateRoleCommand("Cashier", ["identity.user.view"]));

        List<OutboxMessage> captured = await node.Context.OutboxMessages
            .Where(message => message.EntityType == "Role")
            .ToListAsync();

        captured.Should().ContainSingle();
        captured[0].Operation.Should().Be(SyncOperationKind.Upsert);
        captured[0].SourceNode.Should().Be(node.Node.NodeId);
        captured[0].Status.Should().Be(OutboxStatus.Pending);
        // The scope and the policy come from the entity's own [Replicated] declaration, never from
        // the caller. Role is CloudToStore/CloudWins — head office decides who may do what — and the
        // outbox row records that faithfully rather than assuming the change is heading upward.
        captured[0].Scope.Should().Be(ReplicationScope.CloudToStore);
        captured[0].ConflictPolicy.Should().Be(ConflictPolicy.CloudWins);
    }

    [Fact]
    public async Task The_captured_payload_carries_the_row_and_round_trips()
    {
        await using SyncHarness node = await SyncHarness.CreateAsync(fixture);

        await node.SendAsync(new CreateRoleCommand("Supervisor", ["identity.user.view"], "Floor supervisor"));

        OutboxMessage message = await node.Context.OutboxMessages
            .SingleAsync(entry => entry.EntityType == "Role");

        message.Payload.Should().Contain("Supervisor").And.Contain("Floor supervisor");
        message.Payload.Should().NotContain("RowVersion", "the concurrency token is node-local (ADR-035)");
    }

    [Fact]
    public async Task The_row_and_its_outbox_entry_carry_the_same_stamp()
    {
        // ADR-007. Without the stamp on the row, the receiving node has nothing to compare an
        // inbound change against, and every order-dependent conflict policy degenerates into
        // "whatever arrived last".
        await using SyncHarness node = await SyncHarness.CreateAsync(fixture);

        Guid roleId = await node.SendAsync(new CreateRoleCommand("Manager", ["identity.role.view"]));

        OutboxMessage message = await node.Context.OutboxMessages
            .SingleAsync(entry => entry.EntityType == "Role" && entry.EntityId == roleId);

        VumaRetail.Domain.Identity.Role role = await node.Context.Roles.SingleAsync(entry => entry.Id == roleId);

        role.SyncStamp.Should().Be(message.OperationStamp).And.NotBeEmpty();
        role.SyncState.Should().Be(VumaRetail.Domain.Primitives.SyncState.Pending);
    }

    [Fact]
    public async Task A_handler_that_throws_leaves_no_outbox_row()
    {
        // The atomicity claim, from the failing side. If the outbox row survived a rolled-back
        // command the cloud would be told about a change that never happened.
        await using SyncHarness node = await SyncHarness.CreateAsync(fixture);

        await node.SendAsync(new CreateRoleCommand("Duplicate", ["identity.user.view"]));

        int before = await node.Context.OutboxMessages.CountAsync();

        Func<Task> conflicting = () => node.SendAsync(new CreateRoleCommand("Duplicate", ["identity.user.view"]));

        await conflicting.Should().ThrowAsync<VumaRetail.Domain.Primitives.DomainException>();

        node.Context.ChangeTracker.Clear();

        (await node.Context.OutboxMessages.CountAsync()).Should().Be(before);
    }

    [Fact]
    public async Task A_node_local_entity_writes_no_outbox_row()
    {
        // The outbox, the inbox and the cursors are themselves NodeLocal. If they were not, the
        // system would replicate its own replication for ever.
        await using SyncHarness node = await SyncHarness.CreateAsync(fixture);

        await node.SendAsync(new CreateRoleCommand("Cashier", ["identity.user.view"]));

        List<string> capturedTypes = await node.Context.OutboxMessages
            .Select(message => message.EntityType)
            .Distinct()
            .ToListAsync();

        capturedTypes.Should().NotContain(nameof(OutboxMessage));
        capturedTypes.Should().NotContain(nameof(InboxMessage));
        capturedTypes.Should().NotContain(nameof(SyncCursor));
    }

    [Fact]
    public async Task A_soft_delete_is_captured_as_a_delete_operation()
    {
        await using SyncHarness node = await SyncHarness.CreateAsync(fixture);

        Guid roleId = await node.SendAsync(new CreateRoleCommand("Temporary", ["identity.user.view"]));
        await node.SendAsync(new DeleteRoleCommand(roleId));

        List<OutboxMessage> forRole = await node.Context.OutboxMessages
            .Where(message => message.EntityId == roleId && message.EntityType == "Role")
            .OrderBy(message => message.OperationStamp)
            .ToListAsync();

        forRole.Should().HaveCount(2);
        forRole[0].Operation.Should().Be(SyncOperationKind.Upsert);
        // The interceptor rewrites a hard delete into a soft delete at SaveChanges, which is after
        // the outbox behaviour runs — so the behaviour has to detect a delete the same way the
        // interceptor does, by deleted_at being newly set. The two must agree.
        forRole[1].Operation.Should().Be(SyncOperationKind.Delete);
    }

    [Fact]
    public async Task Every_captured_operation_id_is_unique()
    {
        // The receiver deduplicates on (source_node, operation_id). Two captures sharing an id would
        // make it discard a genuine second change.
        await using SyncHarness node = await SyncHarness.CreateAsync(fixture);

        for (int index = 0; index < 10; index++)
        {
            await node.SendAsync(new CreateRoleCommand($"Role {index}", ["identity.user.view"]));
        }

        List<Guid> operationIds = await node.Context.OutboxMessages
            .Select(message => message.OperationId)
            .ToListAsync();

        operationIds.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Captured_stamps_increase_with_every_change()
    {
        await using SyncHarness node = await SyncHarness.CreateAsync(fixture);

        for (int index = 0; index < 5; index++)
        {
            await node.SendAsync(new CreateRoleCommand($"Role {index}", ["identity.user.view"]));
        }

        List<string> stamps = await node.Context.OutboxMessages
            .Where(message => message.EntityType == "Role")
            .OrderBy(message => message.CreatedAt)
            .ThenBy(message => message.OperationStamp)
            .Select(message => message.OperationStamp)
            .ToListAsync();

        stamps.Should().BeInAscendingOrder(StringComparer.Ordinal);
        stamps.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task A_query_captures_nothing()
    {
        // A report must not pay for replication, and enumerating the tracked entities of a context
        // that has just materialised ten thousand rows is not free.
        await using SyncHarness node = await SyncHarness.CreateAsync(fixture);

        Guid roleId = await node.SendAsync(new CreateRoleCommand("Cashier", ["identity.user.view"]));
        int afterCommand = await node.Context.OutboxMessages.CountAsync();

        await node.QueryAsync(new VumaRetail.Application.Identity.Queries.GetEffectivePermissionsQuery(
            roleId,
            node.StoreId));

        (await node.Context.OutboxMessages.CountAsync()).Should().Be(afterCommand);
    }
}
