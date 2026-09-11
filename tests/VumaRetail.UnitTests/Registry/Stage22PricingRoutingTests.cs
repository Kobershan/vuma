using VumaRetail.Domain.Registry;

namespace VumaRetail.UnitTests.Registry;

public sealed class Stage22PricingRoutingTests
{
    [Fact]
    public void Barcode_collision_between_companies_is_rejected()
    {
        var a = PremisesSkuRouting.Create(Guid.NewGuid(), Guid.NewGuid(), "6001", Guid.NewGuid(), true);
        var b = PremisesSkuRouting.Create(a.TenantId, a.PremisesId, "6001", Guid.NewGuid(), true);
        FluentActions.Invoking(() => PremisesSkuRouting.EnsureUnique(new[] { a }, b)).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Pricing_domains_have_independent_fallbacks()
    {
        var group = new GroupRetailPrice(Guid.NewGuid(), Guid.NewGuid(), 10m, 12m);
        var franchise = new FranchiseWholesalePrice(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 8m, null);
        group.EffectivePrice.Should().Be(12m);
        franchise.EffectivePrice.Should().Be(8m);
    }

    [Fact]
    public void Persisted_pricing_rows_validate_price_boundaries_and_fallbacks()
    {
        Guid tenant = Guid.NewGuid();
        GroupRetailPriceRow group = GroupRetailPriceRow.Define(
            tenant, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 10m, 12m, "zar");
        FranchiseWholesalePriceRow franchise = FranchiseWholesalePriceRow.Define(
            tenant, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 8m, null, "ZAR");
        SharedPremisesRetailPriceRow shared = SharedPremisesRetailPriceRow.Define(
            tenant, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 15m, "ZAR");

        group.EffectivePrice.Should().Be(12m);
        group.Currency.Should().Be("ZAR");
        franchise.EffectivePrice.Should().Be(8m);
        shared.RetailPrice.Should().Be(15m);
        FluentActions.Invoking(() => group.Replace(-1m, null, "ZAR"))
            .Should().Throw<ArgumentOutOfRangeException>();
    }
}
