using ZenithRetail.Application.Identity.Commands;
using ZenithRetail.Application.Identity.Permissions;
using ZenithRetail.IntegrationTests.Harness;

namespace ZenithRetail.IntegrationTests.Identity;

/// <summary>
/// What a user may do, resolved per store (ADR-013).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class EffectivePermissionsTests(PostgresFixture fixture)
{
    private static readonly Guid OtherStore = Guid.Parse("01900000-0000-7000-8000-0000000000ee");

    [Fact]
    public async Task Is_the_union_of_tenant_wide_and_this_stores_roles()
    {
        await using IdentityHarness harness = await IdentityHarness.CreateAsync(fixture);

        Guid everywhere = await harness.CreateRoleAsync("Everywhere", PlatformPermissions.StoreView);
        Guid hereOnly = await harness.CreateRoleAsync("Here only", IdentityPermissions.TerminalEnrol);

        Guid userId = await harness.CreateUserAsync("supervisor", roleId: everywhere);
        await new AssignRoleCommandHandler(harness.Users, harness.Roles, harness.TenantContext, harness.Context)
            .HandleAsync(new AssignRoleCommand(userId, hereOnly, harness.StoreId));

        IReadOnlyCollection<string> here = await harness.PermissionsAsync(userId, harness.StoreId);

        here.Should().BeEquivalentTo([PlatformPermissions.StoreView, IdentityPermissions.TerminalEnrol]);
    }

    [Fact]
    public async Task Excludes_a_role_scoped_to_a_different_store()
    {
        // A supervisor who may enrol a terminal at the airport branch does not thereby enrol one at
        // head office.
        await using IdentityHarness harness = await IdentityHarness.CreateAsync(fixture);

        Guid hereOnly = await harness.CreateRoleAsync("Here only", IdentityPermissions.TerminalEnrol);
        Guid userId = await harness.CreateUserAsync("supervisor", roleId: hereOnly, storeId: harness.StoreId);

        (await harness.PermissionsAsync(userId, OtherStore)).Should().BeEmpty();
        (await harness.PermissionsAsync(userId, harness.StoreId))
            .Should().ContainSingle().Which.Should().Be(IdentityPermissions.TerminalEnrol);
    }

    [Fact]
    public async Task A_tenant_wide_role_applies_in_every_store_and_tenant_wide()
    {
        await using IdentityHarness harness = await IdentityHarness.CreateAsync(fixture);

        Guid everywhere = await harness.CreateRoleAsync("Everywhere", PlatformPermissions.StoreView);
        Guid userId = await harness.CreateUserAsync("owner", roleId: everywhere);

        (await harness.PermissionsAsync(userId, harness.StoreId)).Should().Contain(PlatformPermissions.StoreView);
        (await harness.PermissionsAsync(userId, OtherStore)).Should().Contain(PlatformPermissions.StoreView);
        (await harness.PermissionsAsync(userId, null)).Should().Contain(PlatformPermissions.StoreView);
    }

    [Fact]
    public async Task Deduplicates_a_permission_two_roles_both_grant()
    {
        await using IdentityHarness harness = await IdentityHarness.CreateAsync(fixture);

        Guid first = await harness.CreateRoleAsync("First", PlatformPermissions.StoreView);
        Guid second = await harness.CreateRoleAsync("Second", PlatformPermissions.StoreView, IdentityPermissions.UserView);

        Guid userId = await harness.CreateUserAsync("supervisor", roleId: first);
        await new AssignRoleCommandHandler(harness.Users, harness.Roles, harness.TenantContext, harness.Context)
            .HandleAsync(new AssignRoleCommand(userId, second));

        IReadOnlyCollection<string> held = await harness.PermissionsAsync(userId, harness.StoreId);

        held.Should().BeEquivalentTo([PlatformPermissions.StoreView, IdentityPermissions.UserView]);
    }

    [Fact]
    public async Task A_user_with_no_roles_holds_nothing()
    {
        await using IdentityHarness harness = await IdentityHarness.CreateAsync(fixture);
        Guid userId = await harness.CreateUserAsync("newcomer");

        (await harness.PermissionsAsync(userId, harness.StoreId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Drops_a_grant_the_catalogue_no_longer_declares()
    {
        // A module switched off, or a permission renamed between releases, leaves stale grants in a
        // customer's database. Returning them would let a client show a button the server refuses.
        await using IdentityHarness harness = await IdentityHarness.CreateAsync(fixture);

        Guid roleId = await harness.CreateRoleAsync("Cashier", PlatformPermissions.StoreView);
        Guid userId = await harness.CreateUserAsync("cashier1", roleId: roleId);

        harness.Roles.Add(Domain.Identity.RolePermission.Grant(
            harness.TenantId, roleId, Domain.Identity.PermissionKey.Parse("retired.module.action")));
        await harness.Context.CommitAsync();

        IReadOnlyCollection<string> held = await harness.PermissionsAsync(userId, harness.StoreId);

        held.Should().ContainSingle().Which.Should().Be(PlatformPermissions.StoreView);
    }

    [Fact]
    public async Task Is_returned_in_a_stable_order()
    {
        // The Android app diffs this list to decide what changed; an unstable order makes every
        // fetch look like a change.
        await using IdentityHarness harness = await IdentityHarness.CreateAsync(fixture);

        Guid roleId = await harness.CreateRoleAsync(
            "Manager",
            IdentityPermissions.UserView,
            PlatformPermissions.StoreView,
            IdentityPermissions.TerminalEnrol);

        Guid userId = await harness.CreateUserAsync("manager", roleId: roleId);

        IReadOnlyCollection<string> held = await harness.PermissionsAsync(userId, harness.StoreId);

        held.Should().BeInAscendingOrder(StringComparer.Ordinal);
    }
}
