using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ZenithRetail.Domain.Platform;
using ZenithRetail.Domain.Primitives;
using ZenithRetail.IntegrationTests.Harness;

namespace ZenithRetail.IntegrationTests.Persistence;

/// <summary>
/// Requirement R6 — who changed what, when, from which terminal, immutably.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AuditTrailTests(PostgresFixture fixture)
{
    [Fact]
    public async Task An_insert_is_stamped_from_the_clock_and_the_principal()
    {
        string connectionString = await fixture.CreateDatabaseAsync();
        TestClock clock = new();
        TestPrincipalAccessor principal = new("user:alice", Guid.NewGuid());

        await using ZenithRetailDbContext context = TestDbContextFactory.For(connectionString, clock, principal);

        Store store = Store.Create(UuidV7.NewGuid(), "JHB01", "Johannesburg");
        context.Add(store);
        await context.SaveChangesAsync();

        // The clock never moved from its synthetic start, so any real wall-clock read would show.
        store.CreatedAt.Should().Be(TestClock.DefaultStart);
        store.UpdatedAt.Should().Be(TestClock.DefaultStart);
        store.CreatedBy.Should().Be("user:alice");
        store.UpdatedBy.Should().Be("user:alice");
        store.RowVersion.Should().NotBeEmpty();
    }

    [Fact]
    public async Task An_update_leaves_the_creation_stamp_alone()
    {
        string connectionString = await fixture.CreateDatabaseAsync();
        TestClock clock = new();

        await using ZenithRetailDbContext context = TestDbContextFactory.For(connectionString, clock);

        Store store = Store.Create(UuidV7.NewGuid(), "JHB01", "Johannesburg");
        context.Add(store);
        await context.SaveChangesAsync();

        clock.Advance(TimeSpan.FromDays(30));
        store.SetDetails("Johannesburg Central", "12 Main Road");
        await context.SaveChangesAsync();

        store.CreatedAt.Should().Be(TestClock.DefaultStart);
        store.UpdatedAt.Should().Be(TestClock.DefaultStart.AddDays(30));
    }

    [Fact]
    public async Task A_detached_entity_cannot_rewrite_its_own_origin()
    {
        // Business code cannot set an audit field, but EF will write whatever is on the instance.
        // The interceptor unsets IsModified on the creation columns so a round-tripped or crafted
        // entity cannot claim a different author.
        string connectionString = await fixture.CreateDatabaseAsync();
        Guid tenantId = UuidV7.NewGuid();
        Guid storeId;

        await using (ZenithRetailDbContext writer = TestDbContextFactory.For(connectionString, principal: new TestPrincipalAccessor("user:alice")))
        {
            Store store = Store.Create(tenantId, "JHB01", "Johannesburg");
            writer.Add(store);
            await writer.SaveChangesAsync();
            storeId = store.Id;
        }

        await using (ZenithRetailDbContext attacker = TestDbContextFactory.For(connectionString, principal: new TestPrincipalAccessor("user:mallory")))
        {
            Store store = await attacker.Stores.SingleAsync(s => s.Id == storeId);
            store.MarkCreated("user:mallory", TestClock.DefaultStart.AddYears(1));
            store.SetDetails("Renamed", null);
            await attacker.SaveChangesAsync();
        }

        await using ZenithRetailDbContext reader = TestDbContextFactory.For(connectionString);
        Store persisted = await reader.Stores.SingleAsync(s => s.Id == storeId);

        persisted.CreatedBy.Should().Be("user:alice");
        persisted.CreatedAt.Should().Be(TestClock.DefaultStart);
        persisted.UpdatedBy.Should().Be("user:mallory");
    }

    [Fact]
    public async Task An_insert_an_update_and_a_delete_each_write_exactly_one_audit_entry()
    {
        string connectionString = await fixture.CreateDatabaseAsync();
        Guid tenantId = UuidV7.NewGuid();
        Guid terminalId = Guid.NewGuid();

        await using ZenithRetailDbContext context = TestDbContextFactory.For(
            connectionString,
            principal: new TestPrincipalAccessor("user:alice", terminalId));

        Store store = Store.Create(tenantId, "JHB01", "Johannesburg");
        context.Add(store);
        await context.SaveChangesAsync();

        store.SetDetails("Johannesburg Central", null);
        await context.SaveChangesAsync();

        context.Remove(store);
        await context.SaveChangesAsync();

        List<AuditEntry> trail = await context.AuditEntries
            .Where(entry => entry.EntityId == store.Id)
            .OrderBy(entry => entry.CreatedAt)
            .ThenBy(entry => entry.Id)
            .ToListAsync();

        trail.Select(entry => entry.Action)
            .Should().Equal(AuditAction.Created, AuditAction.Updated, AuditAction.Deleted);

        trail.Should().OnlyContain(entry => entry.Principal == "user:alice");
        trail.Should().OnlyContain(entry => entry.TerminalId == terminalId);
        trail.Should().OnlyContain(entry => entry.TableName == "platform.stores");
        trail.Should().OnlyContain(entry => entry.EntityType == nameof(Store));
        trail.Should().OnlyContain(entry => entry.TenantId == tenantId);
    }

    [Fact]
    public async Task An_update_records_only_the_columns_that_changed()
    {
        string connectionString = await fixture.CreateDatabaseAsync();

        await using ZenithRetailDbContext context = TestDbContextFactory.For(connectionString);

        Store store = Store.Create(UuidV7.NewGuid(), "JHB01", "Johannesburg");
        context.Add(store);
        await context.SaveChangesAsync();

        store.SetDetails("Johannesburg Central", null);
        await context.SaveChangesAsync();

        AuditEntry update = await context.AuditEntries
            .SingleAsync(entry => entry.EntityId == store.Id && entry.Action == AuditAction.Updated);

        using JsonDocument changes = JsonDocument.Parse(update.Changes);

        changes.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo([nameof(Store.Name)]);

        changes.RootElement.GetProperty(nameof(Store.Name)).GetProperty("from").GetString()
            .Should().Be("Johannesburg");
        changes.RootElement.GetProperty(nameof(Store.Name)).GetProperty("to").GetString()
            .Should().Be("Johannesburg Central");
    }

    [Fact]
    public async Task An_insert_records_the_values_the_row_was_created_with()
    {
        string connectionString = await fixture.CreateDatabaseAsync();

        await using ZenithRetailDbContext context = TestDbContextFactory.For(connectionString);

        Store store = Store.Create(UuidV7.NewGuid(), "JHB01", "Johannesburg");
        context.Add(store);
        await context.SaveChangesAsync();

        AuditEntry created = await context.AuditEntries
            .SingleAsync(entry => entry.EntityId == store.Id && entry.Action == AuditAction.Created);

        using JsonDocument changes = JsonDocument.Parse(created.Changes);

        changes.RootElement.GetProperty(nameof(Store.Code)).GetString().Should().Be("JHB01");
        changes.RootElement.GetProperty(nameof(Store.Name)).GetString().Should().Be("Johannesburg");
    }

    [Fact]
    public async Task A_rolled_back_transaction_leaves_no_audit_entry()
    {
        // An audit entry written outside the transaction it describes would report changes that
        // never happened, which is worse than no trail at all.
        string connectionString = await fixture.CreateDatabaseAsync();
        Guid tenantId = UuidV7.NewGuid();

        await using (ZenithRetailDbContext context = TestDbContextFactory.For(connectionString))
        {
            await using var transaction = await context.Database.BeginTransactionAsync();

            context.Add(Store.Create(tenantId, "JHB01", "Johannesburg"));
            await context.SaveChangesAsync();

            await transaction.RollbackAsync();
        }

        await using ZenithRetailDbContext reader = TestDbContextFactory.For(connectionString);

        (await reader.Stores.ToListAsync()).Should().BeEmpty();
        (await reader.AuditEntries.ToListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task An_audit_entry_does_not_audit_itself()
    {
        string connectionString = await fixture.CreateDatabaseAsync();

        await using ZenithRetailDbContext context = TestDbContextFactory.For(connectionString);

        context.Add(Store.Create(UuidV7.NewGuid(), "JHB01", "Johannesburg"));
        await context.SaveChangesAsync();

        (await context.AuditEntries.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task An_audit_entry_cannot_be_changed_or_removed()
    {
        // R6 and ADR-012. A trail somebody can rewrite is not evidence of anything.
        string connectionString = await fixture.CreateDatabaseAsync();

        await using ZenithRetailDbContext context = TestDbContextFactory.For(connectionString);

        context.Add(Store.Create(UuidV7.NewGuid(), "JHB01", "Johannesburg"));
        await context.SaveChangesAsync();

        AuditEntry entry = await context.AuditEntries.SingleAsync();

        entry.MarkSyncState(SyncState.Synced);

        await context.Invoking(c => c.SaveChangesAsync())
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*immutable*");

        context.ChangeTracker.Clear();

        AuditEntry again = await context.AuditEntries.SingleAsync();
        context.Remove(again);

        await context.Invoking(c => c.SaveChangesAsync())
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*immutable*");
    }

    [Fact]
    public async Task A_system_action_is_distinguishable_from_a_persons()
    {
        string connectionString = await fixture.CreateDatabaseAsync();

        await using ZenithRetailDbContext context = TestDbContextFactory.For(
            connectionString,
            principal: new TestPrincipalAccessor("system:sync-receiver", isSystem: true));

        context.Add(Store.Create(UuidV7.NewGuid(), "JHB01", "Johannesburg"));
        await context.SaveChangesAsync();

        AuditEntry entry = await context.AuditEntries.SingleAsync();

        entry.IsSystemAction.Should().BeTrue();
        entry.Principal.Should().Be("system:sync-receiver");
        entry.TerminalId.Should().BeNull();
    }
}
