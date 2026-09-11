using FluentAssertions;
using VumaRetail.Application.Connect;

namespace VumaRetail.UnitTests.Connect;

public sealed class ConnectPermissionTests
{
    [Fact]
    public void Connect_manifest_exposes_the_network_module_and_all_write_permissions_are_high_risk()
    {
        ConnectModuleManifest manifest = new();
        ConnectPermissions permissions = new();

        manifest.Module.Should().Be("connect");
        manifest.LicenceFlag.Should().Be("connect");
        permissions.Module.Should().Be("connect");
        permissions.Permissions.Select(permission => permission.Key.Value).Should().BeEquivalentTo(
            [
                ConnectPermissions.View,
                ConnectPermissions.Manage,
                ConnectPermissions.Publish,
                ConnectPermissions.Decide,
                ConnectPermissions.Order
            ]);
        permissions.Permissions
            .Where(permission => permission.Key.Value != ConnectPermissions.View)
            .Should().OnlyContain(permission => permission.IsHighRisk);
    }
}
