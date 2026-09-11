using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Inventory;

/// <summary>
/// The reservation chain state machine (ADR-103): a hold plus exactly one terminal row, new rows
/// never edits, and the refusals that keep a closed chain closed.
/// </summary>
public sealed class StockReservationTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid CompanyId = UuidV7.NewGuid();
    private static readonly Guid StoreId = UuidV7.NewGuid();
    private static readonly Guid LocationId = UuidV7.NewGuid();
    private static readonly Guid ItemId = UuidV7.NewGuid();
    private static readonly Guid OrderId = UuidV7.NewGuid();

    private static StockReservation NewHold(
        Quantity? quantity = null,
        DateTimeOffset? expiresAt = null,
        Guid? intentId = null,
        Guid? legId = null)
        => StockReservation.Hold(
            TenantId, StoreId, CompanyId, LocationId, ItemId, null,
            quantity ?? new Quantity(5m, "EA"),
            ReservationSource.Order, OrderId,
            groupDocumentRef: "SO-2026-000412",
            expiresAt: expiresAt,
            intentId: intentId,
            legId: legId,
            reason: "Order line");

    [Fact]
    public void A_hold_preserves_tracking_across_terminal_rows()
    {
        StockReservation hold = StockReservation.Hold(
            TenantId, StoreId, CompanyId, LocationId, ItemId, null,
            new Quantity(1m, "EA"), ReservationSource.Order, OrderId,
            reason: "Tracked order", batchReference: "BATCH-7",
            expiryDate: new DateOnly(2027, 1, 31), serialNumber: "SERIAL-7");

        StockReservation released = hold.Release();

        hold.BatchReference.Should().Be("BATCH-7");
        hold.ExpiryDate.Should().Be(new DateOnly(2027, 1, 31));
        hold.SerialNumber.Should().Be("SERIAL-7");
        released.BatchReference.Should().Be(hold.BatchReference);
        released.ExpiryDate.Should().Be(hold.ExpiryDate);
        released.SerialNumber.Should().Be(hold.SerialNumber);
    }

    [Fact]
    public void A_hold_opens_a_chain_with_sequence_zero()
    {
        StockReservation hold = NewHold();

        hold.State.Should().Be(ReservationState.Held);
        hold.SequenceNumber.Should().Be(0);
        hold.Quantity.Value.Should().Be(5m);
        hold.ReservationId.Should().NotBe(Guid.Empty);
        hold.Source.Should().Be(ReservationSource.Order);
        hold.SourceDocumentId.Should().Be(OrderId);
        hold.GroupDocumentRef.Should().Be("SO-2026-000412");
        hold.CompanyId.Should().Be(CompanyId);
    }

    [Fact]
    public void A_hold_requires_a_positive_quantity()
    {
        Action zero = () => NewHold(new Quantity(0m, "EA"));

        zero.Should().Throw<InventoryRuleException>().WithMessage("*greater than zero*");
    }

    [Fact]
    public void A_hold_requires_exactly_one_of_item_or_variant()
    {
        Action neither = () => StockReservation.Hold(
            TenantId, StoreId, CompanyId, LocationId, null, null,
            new Quantity(1m, "EA"), ReservationSource.Order, OrderId);

        neither.Should().Throw<InventoryRuleException>();
    }

    [Fact]
    public void A_saga_hold_names_both_intent_and_leg_or_neither()
    {
        Action halfKeyed = () => NewHold(intentId: Guid.NewGuid(), legId: null);

        halfKeyed.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Release_appends_a_terminal_row_and_leaves_the_hold_untouched()
    {
        StockReservation hold = NewHold();

        StockReservation released = hold.Release("Customer cancelled");

        released.State.Should().Be(ReservationState.Released);
        released.SequenceNumber.Should().Be(1);
        released.ReservationId.Should().Be(hold.ReservationId);
        released.Quantity.Should().Be(hold.Quantity);
        released.Reason.Should().Be("Customer cancelled");
        hold.State.Should().Be(ReservationState.Held);
    }

    [Fact]
    public void Consume_appends_a_terminal_row_naming_what_consumed_it()
    {
        StockReservation hold = NewHold();
        Guid shipmentId = UuidV7.NewGuid();

        StockReservation consumed = hold.Consume(shipmentId, DateTimeOffset.UtcNow);

        consumed.State.Should().Be(ReservationState.Consumed);
        consumed.SequenceNumber.Should().Be(1);
        consumed.ReservationId.Should().Be(hold.ReservationId);
        consumed.ConsumedByReferenceId.Should().Be(shipmentId);
    }

    [Fact]
    public void Expire_appends_a_terminal_row()
    {
        StockReservation hold = NewHold(expiresAt: DateTimeOffset.UtcNow.AddHours(72));

        StockReservation expired = hold.Expire();

        expired.State.Should().Be(ReservationState.Expired);
        expired.SequenceNumber.Should().Be(1);
        expired.ReservationId.Should().Be(hold.ReservationId);
    }

    [Fact]
    public void A_closed_chain_refuses_a_second_terminal_row()
    {
        StockReservation hold = NewHold();
        StockReservation released = hold.Release();

        Action secondClose = () => released.Release("again");

        secondClose.Should().Throw<InventoryRuleException>().WithMessage("*not Held*");
    }

    [Fact]
    public void A_released_hold_cannot_be_consumed()
    {
        StockReservation hold = NewHold();
        StockReservation released = hold.Release();

        Action consume = () => released.Consume(UuidV7.NewGuid(), DateTimeOffset.UtcNow);

        consume.Should().Throw<InventoryRuleException>();
    }
}
