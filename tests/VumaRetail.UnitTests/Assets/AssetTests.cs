using FluentAssertions;
using VumaRetail.Domain.Assets;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Assets;

public sealed class AssetTests
{
    [Fact]
    public void Depreciation_stops_at_residual_after_useful_life()
    {
        FixedAsset asset = FixedAsset.Create(Guid.NewGuid(), null, Guid.NewGuid(), "A-1", "Till",
            new DateOnly(2026, 1, 1), new Money(12000m, "ZAR"));
        AssetBook book = AssetBook.Create(asset.TenantId, null, asset.CompanyId!.Value, asset.Id, "Local",
            new DateOnly(2026, 1, 1), Money.Zero("ZAR"), 12);
        DepreciationCharge charge = DepreciationCalculator.Calculate(asset, book, new DateOnly(2027, 1, 1));
        charge.Amount.Amount.Should().Be(0m);
        charge.ClosingNetBookValue.Amount.Should().Be(0m);
    }
}
