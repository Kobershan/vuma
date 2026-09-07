using NSubstitute;
using VumaRetail.Application.Catalog;
using VumaRetail.Domain.Catalog;
using VumaRetail.Domain.Primitives;
using VumaRetail.Infrastructure.Sales;

namespace VumaRetail.UnitTests.Sales;

/// <summary>
/// Pack size resolution (ADR-112): base units read bare, converting units read counted, and an
/// unknown code never stops a trade — it reads back verbatim.
/// </summary>
public sealed class PackSizeResolverTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();

    [Fact]
    public async Task A_base_unit_reads_bare()
    {
        var units = Substitute.For<IUnitOfMeasureRepository>();
        units.FindByCodeAsync("EA", Arg.Any<CancellationToken>())
            .Returns(UnitOfMeasure.CreateBase(TenantId, "EA", "Each", UnitOfMeasureType.Count));

        var resolver = new PackSizeResolver(units);

        Application.Abstractions.Sales.PackSizeSnapshot snapshot =
            await resolver.ResolveAsync(null, null, "EA", 40m);

        snapshot.Description.Should().Be("Each");
    }

    [Fact]
    public async Task A_converting_unit_reads_counted_with_its_pack_size()
    {
        var units = Substitute.For<IUnitOfMeasureRepository>();
        UnitOfMeasure each = UnitOfMeasure.CreateBase(TenantId, "EA", "Each", UnitOfMeasureType.Count);
        UnitOfMeasure box = UnitOfMeasure.CreateDerived(TenantId, "BOX10", "Case of 10", each, 10m);
        units.FindByCodeAsync("BOX10", Arg.Any<CancellationToken>()).Returns(box);

        var resolver = new PackSizeResolver(units);

        Application.Abstractions.Sales.PackSizeSnapshot snapshot =
            await resolver.ResolveAsync(null, null, "BOX10", 6m);

        snapshot.Description.Should().Be("6 x Case of 10");
        snapshot.UnitsPerPack.Should().Be(10m);
    }

    [Fact]
    public async Task An_unknown_unit_reads_back_verbatim_rather_than_refusing_the_sale()
    {
        var units = Substitute.For<IUnitOfMeasureRepository>();
        units.FindByCodeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((UnitOfMeasure?)null);

        var resolver = new PackSizeResolver(units);

        Application.Abstractions.Sales.PackSizeSnapshot snapshot =
            await resolver.ResolveAsync(null, null, "BAG", 3m);

        snapshot.Description.Should().Be("BAG");
    }
}
