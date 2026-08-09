using ZenithRetail.Domain.Entities;
using ZenithRetail.Domain.Platform;
using ZenithRetail.Domain.Primitives;

namespace ZenithRetail.UnitTests.Platform;

public sealed class AuditEntryTests
{
    private static readonly DateTimeOffset Occurred = new(2026, 3, 4, 9, 15, 0, TimeSpan.Zero);

    [Fact]
    public void An_audit_entry_records_who_did_what_where_and_when()
    {
        Guid tenantId = UuidV7.NewGuid();
        Guid storeId = UuidV7.NewGuid();
        Guid entityId = UuidV7.NewGuid();
        Guid terminalId = UuidV7.NewGuid();

        AuditEntry entry = AuditEntry.Record(
            tenantId, storeId, nameof(Store), "platform.stores", entityId,
            AuditAction.Updated, "user:alice", terminalId, isSystemAction: false, Occurred,
            """{"Name":{"from":"A","to":"B"}}""");

        entry.TenantId.Should().Be(tenantId);
        entry.StoreId.Should().Be(storeId);
        entry.EntityId.Should().Be(entityId);
        entry.EntityType.Should().Be(nameof(Store));
        entry.TableName.Should().Be("platform.stores");
        entry.Action.Should().Be(AuditAction.Updated);
        entry.Principal.Should().Be("user:alice");
        entry.TerminalId.Should().Be(terminalId);
        entry.IsSystemAction.Should().BeFalse();
        entry.OccurredAt.Should().Be(Occurred);
    }

    [Fact]
    public void An_audit_entry_is_marked_immutable_so_the_persistence_layer_refuses_to_change_it()
    {
        // The guarantee is enforced in one place — the interceptor — rather than by each entity, so
        // what matters here is that the marker is present. AuditTrailTests proves the refusal.
        AuditEntry entry = AuditEntry.Record(
            UuidV7.NewGuid(), null, nameof(Store), "platform.stores", UuidV7.NewGuid(),
            AuditAction.Created, "system:seed", null, isSystemAction: true, Occurred, "{}");

        entry.Should().BeAssignableTo<IImmutableRecord>();
    }

    [Fact]
    public void An_audit_entry_replicates_append_only()
    {
        // ADR-007. Two nodes both writing audit history must merge, never overwrite — losing an
        // audit entry to a conflict resolution would defeat the point of having one.
        ReplicatedAttribute? replication = typeof(AuditEntry)
            .GetCustomAttributes(typeof(ReplicatedAttribute), inherit: false)
            .Cast<ReplicatedAttribute>()
            .SingleOrDefault();

        replication.Should().NotBeNull();
        replication!.ConflictPolicy.Should().Be(ConflictPolicy.AppendOnly);
        replication.Scope.Should().Be(ReplicationScope.StoreToCloud);
    }
}
