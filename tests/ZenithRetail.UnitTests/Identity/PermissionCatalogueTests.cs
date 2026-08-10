using ZenithRetail.Application.Identity.Permissions;
using ZenithRetail.Domain.Identity;

namespace ZenithRetail.UnitTests.Identity;

/// <summary>
/// <see cref="PermissionCatalogue"/> — the closed set of permissions (ADR-013).
/// </summary>
public sealed class PermissionCatalogueTests
{
    private sealed class FakeModule(string module, params string[] permissions) : IModulePermissions
    {
        public string Module { get; } = module;

        public IReadOnlyCollection<PermissionDescriptor> Permissions { get; } =
            [.. permissions.Select(value => new PermissionDescriptor(PermissionKey.Parse(value), value))];
    }

    [Fact]
    public void Assembles_every_modules_declarations()
    {
        PermissionCatalogue catalogue = new(
        [
            new FakeModule("sales", "sales.refund.approve", "sales.sale.void"),
            new FakeModule("inventory", "inventory.stocktake.approve"),
        ]);

        catalogue.All.Should().HaveCount(3);
        catalogue.Contains(PermissionKey.Parse("sales.refund.approve")).Should().BeTrue();
    }

    [Fact]
    public void Orders_by_key_so_the_admin_UI_and_the_docs_agree()
    {
        PermissionCatalogue catalogue = new(
        [
            new FakeModule("sales", "sales.sale.void", "sales.refund.approve"),
        ]);

        catalogue.All.Select(descriptor => descriptor.Key.Value)
            .Should().ContainInOrder("sales.refund.approve", "sales.sale.void");
    }

    [Fact]
    public void Refuses_a_module_declaring_someone_elses_permission()
    {
        // A permission owned by nobody survives its module being extracted, and there is then no
        // module to ask what it means.
        Action build = () => _ = new PermissionCatalogue([new FakeModule("sales", "inventory.stocktake.approve")]);

        build.Should().Throw<PermissionCatalogueException>()
            .Which.Code.Should().Be("PERMISSION_CATALOGUE_INVALID");
    }

    [Fact]
    public void Refuses_the_same_permission_declared_twice()
    {
        Action build = () => _ = new PermissionCatalogue(
        [
            new FakeModule("sales", "sales.refund.approve"),
            new FakeModule("sales", "sales.refund.approve"),
        ]);

        build.Should().Throw<PermissionCatalogueException>();
    }

    [Fact]
    public void Require_refuses_a_permission_no_module_declares()
    {
        PermissionCatalogue catalogue = new([new FakeModule("sales", "sales.refund.approve")]);

        Action require = () => catalogue.Require("sales.refund.reject");

        require.Should().Throw<UndeclaredPermissionException>()
            .Which.Code.Should().Be("PERMISSION_NOT_DECLARED");
    }

    [Fact]
    public void Require_refuses_a_malformed_permission_before_it_looks_it_up()
    {
        PermissionCatalogue catalogue = new([new FakeModule("sales", "sales.refund.approve")]);

        Action require = () => catalogue.Require("Sales.Refund");

        require.Should().Throw<InvalidPermissionKeyException>();
    }

    [Fact]
    public void The_shipped_declarations_are_valid_and_owned_by_their_module()
    {
        // The real declarations, checked the same way a module stage's will be. A malformed constant
        // in IdentityPermissions would otherwise only surface at startup on a customer's server.
        PermissionCatalogue catalogue = new([new PlatformPermissions(), new IdentityPermissions()]);

        catalogue.All.Should().NotBeEmpty();
        catalogue.All.Should().OnlyContain(descriptor => !string.IsNullOrWhiteSpace(descriptor.Description));
        catalogue.Contains(PermissionKey.Parse(IdentityPermissions.RoleAssign)).Should().BeTrue();
        catalogue.Contains(PermissionKey.Parse(PlatformPermissions.AuditView)).Should().BeTrue();
    }

    [Fact]
    public void The_permission_that_can_grant_every_other_one_is_marked_high_risk()
    {
        PermissionCatalogue catalogue = new([new IdentityPermissions()]);

        catalogue.Find(PermissionKey.Parse(IdentityPermissions.RoleAssign))!.IsHighRisk.Should().BeTrue();
    }
}
