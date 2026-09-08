using VumaRetail.Application.Registry.Trading;

namespace VumaRetail.UnitTests.Trading;

/// <summary>
/// The trading-session permission catalogue and module manifest: ringing a sister company's
/// line is high-risk and separate from the till's own ring, and the module gates on the
/// multi-company flag plus the SharedTill link.
/// </summary>
public sealed class TradingPermissionsTests
{
    [Fact]
    public void Basket_permissions_are_declared_and_high_risk()
    {
        var permissions = new TradingSessionPermissions();

        permissions.Module.Should().Be("trading");
        permissions.Permissions.Select(permission => permission.Key.Value).Should().BeEquivalentTo(
            TradingSessionPermissions.BasketMixed,
            TradingSessionPermissions.BasketAllocateOverride,
            TradingSessionPermissions.BasketVoid);
        permissions.Permissions.Should().OnlyContain(permission => permission.IsHighRisk);
    }

    [Fact]
    public void Manifest_gates_on_the_multicompany_flag_and_is_not_core()
    {
        var manifest = new TradingModuleManifest();

        manifest.Module.Should().Be("trading");
        manifest.LicenceFlag.Should().Be("multicompany");
        manifest.IsCore.Should().BeFalse();
    }
}
