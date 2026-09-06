using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Inventory;

/// <summary>
/// Available-to-promise arithmetic (ADR-103): available is on-hand less reserved less staging,
/// incoming is informational, and a negative can be refused but never constructed.
/// </summary>
public sealed class AvailableToPromiseTests
{
    [Fact]
    public void Available_is_on_hand_less_reserved_less_staging()
    {
        var promise = new AvailableToPromise(
            new Quantity(30m, "EA"),
            new Quantity(12m, "EA"),
            new Quantity(3m, "EA"),
            new Quantity(100m, "EA"),
            DateTimeOffset.UtcNow);

        promise.Available.Value.Should().Be(15m);
        promise.Available.UnitOfMeasure.Should().Be("EA");
    }

    [Fact]
    public void Incoming_is_carried_not_added()
    {
        // 100 units on a purchase order do not make stock sellable today.
        var promise = new AvailableToPromise(
            new Quantity(10m, "EA"),
            Quantity.Zero("EA"),
            Quantity.Zero("EA"),
            new Quantity(100m, "EA"),
            DateTimeOffset.UtcNow);

        promise.Available.Value.Should().Be(10m);
        promise.Incoming.Value.Should().Be(100m);
    }

    [Fact]
    public void A_negative_available_cannot_be_constructed()
    {
        Action build = () => new AvailableToPromise(
            new Quantity(5m, "EA"),
            new Quantity(4m, "EA"),
            new Quantity(3m, "EA"),
            Quantity.Zero("EA"),
            DateTimeOffset.UtcNow);

        build.Should().Throw<InventoryRuleException>().WithMessage("*never goes negative*");
    }

    [Fact]
    public void Mixed_units_are_refused()
    {
        Action build = () => new AvailableToPromise(
            new Quantity(5m, "EA"),
            new Quantity(1m, "KG"),
            Quantity.Zero("EA"),
            Quantity.Zero("EA"),
            DateTimeOffset.UtcNow);

        build.Should().Throw<InventoryRuleException>();
    }

    [Fact]
    public void Fully_reserved_stock_promises_zero_not_negative()
    {
        var promise = new AvailableToPromise(
            new Quantity(5m, "EA"),
            new Quantity(5m, "EA"),
            Quantity.Zero("EA"),
            Quantity.Zero("EA"),
            DateTimeOffset.UtcNow);

        promise.Available.Value.Should().Be(0m);
    }
}

/// <summary>
/// The <c>AvailableBalance</c> projection's own arithmetic: holds grow reserved, closes shrink it
/// in full, and closing more than held is a defect rather than a clamp.
/// </summary>
public sealed class AvailableBalanceTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid CompanyId = UuidV7.NewGuid();
    private static readonly Guid LocationId = UuidV7.NewGuid();
    private static readonly Guid ItemId = UuidV7.NewGuid();

    private static AvailableBalance NewPosition()
        => AvailableBalance.Open(TenantId, null, CompanyId, LocationId, ItemId, null, "EA");

    [Fact]
    public void A_position_opens_at_zero()
    {
        AvailableBalance position = NewPosition();

        position.Reserved.Value.Should().Be(0m);
        position.InStaging.Value.Should().Be(0m);
        position.Incoming.Value.Should().Be(0m);
    }

    [Fact]
    public void Hold_then_close_returns_to_zero()
    {
        AvailableBalance position = NewPosition();

        position.ApplyHold(new Quantity(5m, "EA"));
        position.ApplyClose(new Quantity(5m, "EA"));

        position.Reserved.Value.Should().Be(0m);
    }

    [Fact]
    public void Closing_more_than_reserved_throws_rather_than_clamping()
    {
        // A terminal row always carries its hold's own quantity. Anything else means the
        // projection has drifted from the ledger, and clamping would hide the drift.
        AvailableBalance position = NewPosition();
        position.ApplyHold(new Quantity(2m, "EA"));

        Action close = () => position.ApplyClose(new Quantity(3m, "EA"));

        close.Should().Throw<InventoryRuleException>().WithMessage("*drifted from the ledger*");
    }

    [Fact]
    public void A_hold_in_another_unit_is_refused()
    {
        AvailableBalance position = NewPosition();

        Action hold = () => position.ApplyHold(new Quantity(1m, "KG"));

        hold.Should().Throw<InventoryRuleException>();
    }
}
