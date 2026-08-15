using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Entities;

/// <summary>
/// CLAUDE.md §7 rules 3, 8 and 9 — the mandatory columns, soft delete, and UTC timestamps.
/// </summary>
public sealed class EntityTests
{
    private static readonly Guid Tenant = UuidV7.NewGuid();
    private static readonly Guid Store = UuidV7.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 8, 9, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Gets_a_sortable_identity_on_construction()
    {
        var entity = new TestEntity(Tenant, Store);

        entity.Id.Should().NotBe(Guid.Empty);
        UuidV7.GetTimestamp(entity.Id).Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Is_scoped_to_a_tenant_and_optionally_a_store()
    {
        new TestEntity(Tenant, Store).StoreId.Should().Be(Store);
        new TestEntity(Tenant).StoreId.Should().BeNull("tenant-wide records such as the chart of accounts have no store");
    }

    [Fact]
    public void Starts_local_and_undeleted()
    {
        var entity = new TestEntity(Tenant);

        entity.SyncState.Should().Be(SyncState.Local);
        entity.IsDeleted.Should().BeFalse();
        entity.DeletedAt.Should().BeNull();
    }

    [Fact]
    public void Stamps_both_created_and_updated_on_insert()
    {
        var entity = new TestEntity(Tenant);

        entity.MarkCreated("till-03/anna", Now);

        entity.CreatedBy.Should().Be("till-03/anna");
        entity.CreatedAt.Should().Be(Now);
        entity.UpdatedBy.Should().Be("till-03/anna");
        entity.UpdatedAt.Should().Be(Now, "a row that has never been updated was last touched when it was created");
    }

    [Fact]
    public void Leaves_the_creation_stamp_alone_on_update()
    {
        var entity = new TestEntity(Tenant);
        entity.MarkCreated("till-03/anna", Now);

        entity.MarkUpdated("backoffice/sipho", Now.AddHours(2));

        entity.CreatedBy.Should().Be("till-03/anna", "who created a row is not rewritten by whoever edits it");
        entity.CreatedAt.Should().Be(Now);
        entity.UpdatedBy.Should().Be("backoffice/sipho");
        entity.UpdatedAt.Should().Be(Now.AddHours(2));
    }

    [Fact]
    public void Soft_deletes_rather_than_disappearing()
    {
        // Rule 8: nothing is hard-deleted. An audit trail with a hole in it is not an audit trail.
        var entity = new TestEntity(Tenant);
        entity.MarkCreated("till-03/anna", Now);

        entity.MarkDeleted("manager/thabo", Now.AddDays(1));

        entity.IsDeleted.Should().BeTrue();
        entity.DeletedBy.Should().Be("manager/thabo");
        entity.DeletedAt.Should().Be(Now.AddDays(1));
        entity.UpdatedBy.Should().Be("manager/thabo", "a delete is a change and shows in the audit fields");
    }

    [Fact]
    public void Records_progress_through_replication()
    {
        var entity = new TestEntity(Tenant);

        entity.MarkSyncState(SyncState.Pending);
        entity.SyncState.Should().Be(SyncState.Pending);

        entity.MarkSyncState(SyncState.Synced);
        entity.SyncState.Should().Be(SyncState.Synced);
    }

    [Fact]
    public void An_entity_whose_store_is_not_nullable_still_lands_on_the_right_constructor()
    {
        // Regression, Stage 08. Entity gained a second constructor taking a caller-minted id, for
        // StockTransfer. Written as (Guid id, Guid tenantId, Guid? storeId = null) it silently stole
        // every two-argument base(tenantId, storeId) call from an entity whose own storeId parameter
        // is a non-nullable Guid — Terminal is the one in this solution — because two exact Guid
        // matches beat one exact match plus a Guid-to-Guid? conversion. The row came out with its
        // tenant id as its primary key, the store id in TenantId, and no store at all, which puts it
        // outside its own tenant's global query filter.
        //
        // The fix is that the id-taking constructor has no default for storeId, so a two-argument
        // call cannot bind to it. This test is here because the failure is invisible at the call
        // site: it compiles, and only an assertion on the resulting values catches it.
        var entity = new StoreScopedEntity(Tenant, Store);

        entity.TenantId.Should().Be(Tenant);
        entity.StoreId.Should().Be(Store);
        entity.Id.Should().NotBe(Tenant);
    }

    [Fact]
    public void An_entity_may_be_created_with_an_identity_the_caller_minted()
    {
        Guid minted = UuidV7.NewGuid();

        var entity = new PreMintedEntity(minted, Tenant, Store);

        entity.Id.Should().Be(minted);
        entity.TenantId.Should().Be(Tenant);
        entity.StoreId.Should().Be(Store);
    }

    private sealed class TestEntity : Entity
    {
        public TestEntity(Guid tenantId, Guid? storeId = null)
            : base(tenantId, storeId)
        {
        }
    }

    /// <summary>Shaped like <c>Terminal</c>: a store-scoped entity whose store is not optional.</summary>
    private sealed class StoreScopedEntity : Entity
    {
        public StoreScopedEntity(Guid tenantId, Guid storeId)
            : base(tenantId, storeId)
        {
        }
    }

    /// <summary>Shaped like <c>StockTransfer</c>: the identity is minted before the row exists.</summary>
    private sealed class PreMintedEntity : Entity
    {
        public PreMintedEntity(Guid id, Guid tenantId, Guid? storeId)
            : base(id, tenantId, storeId)
        {
        }
    }
}
