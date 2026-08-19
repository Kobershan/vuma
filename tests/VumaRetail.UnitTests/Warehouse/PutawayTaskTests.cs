using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Warehouse;

namespace VumaRetail.UnitTests.Warehouse;

/// <summary>
/// The putaway task state machine: a task may be confirmed in more than one call, split across bins,
/// and never touches Stage 08's location-level ledger (business rule 2 — that is the caller's job,
/// not this aggregate's).
/// </summary>
public sealed class PutawayTaskTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid StoreId = UuidV7.NewGuid();
    private static readonly Guid LocationId = UuidV7.NewGuid();
    private static readonly Guid ItemId = UuidV7.NewGuid();
    private static readonly Guid BinId = UuidV7.NewGuid();
    private static readonly Guid OtherBinId = UuidV7.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static PutawayTask NewTask(decimal quantity = 10m) => PutawayTask.Create(
        TenantId, StoreId, LocationId, ItemId, null, new Quantity(quantity, "EA"),
        PutawaySourceReferenceType.GoodsReceipt, UuidV7.NewGuid());

    [Fact]
    public void A_new_task_is_pending_with_nothing_confirmed()
    {
        PutawayTask task = NewTask();

        task.Status.Should().Be(PutawayStatus.Pending);
        task.ConfirmedQuantity.Value.Should().Be(0m);
        task.Remaining.Value.Should().Be(10m);
    }

    [Fact]
    public void Confirming_the_full_quantity_completes_the_task()
    {
        PutawayTask task = NewTask();

        task.Confirm(BinId, new Quantity(10m, "EA"), Now);

        task.Status.Should().Be(PutawayStatus.Confirmed);
        task.ConfirmedBinId.Should().Be(BinId);
        task.ConfirmedQuantity.Value.Should().Be(10m);
        task.Remaining.Value.Should().Be(0m);
        task.ConfirmedAt.Should().Be(Now);
    }

    [Fact]
    public void A_partial_confirm_leaves_a_remainder_pending()
    {
        PutawayTask task = NewTask();

        task.Confirm(BinId, new Quantity(4m, "EA"), Now);

        task.Status.Should().Be(PutawayStatus.Pending);
        task.Remaining.Value.Should().Be(6m);
    }

    [Fact]
    public void A_task_can_be_split_across_two_bins_by_two_confirmations()
    {
        PutawayTask task = NewTask();

        task.Confirm(BinId, new Quantity(4m, "EA"), Now);
        task.Confirm(OtherBinId, new Quantity(6m, "EA"), Now.AddMinutes(1));

        task.Status.Should().Be(PutawayStatus.Confirmed);
        task.ConfirmedBinId.Should().Be(OtherBinId, "the last bin confirmed into");
        task.ConfirmedQuantity.Value.Should().Be(10m);
    }

    [Fact]
    public void Confirming_more_than_remains_is_refused()
    {
        PutawayTask task = NewTask();
        task.Confirm(BinId, new Quantity(4m, "EA"), Now);

        Action confirming = () => task.Confirm(BinId, new Quantity(7m, "EA"), Now);

        confirming.Should().Throw<WarehouseRuleException>()
            .Which.Code.Should().Be("WAREHOUSE_PUTAWAY_EXCEEDS_REMAINING");
    }

    [Fact]
    public void A_confirmed_task_cannot_be_confirmed_again()
    {
        PutawayTask task = NewTask();
        task.Confirm(BinId, new Quantity(10m, "EA"), Now);

        Action confirming = () => task.Confirm(BinId, new Quantity(1m, "EA"), Now);

        confirming.Should().Throw<WarehouseRuleException>()
            .Which.Code.Should().Be("WAREHOUSE_PUTAWAY_NOT_PENDING");
    }

    [Fact]
    public void A_task_can_be_cancelled_while_pending()
    {
        PutawayTask task = NewTask();

        task.Cancel();

        task.Status.Should().Be(PutawayStatus.Cancelled);
    }

    [Fact]
    public void A_confirmed_task_cannot_be_cancelled()
    {
        PutawayTask task = NewTask();
        task.Confirm(BinId, new Quantity(10m, "EA"), Now);

        Action cancelling = () => task.Cancel();

        cancelling.Should().Throw<WarehouseRuleException>()
            .Which.Code.Should().Be("WAREHOUSE_PUTAWAY_NOT_PENDING");
    }

    [Fact]
    public void The_allocators_suggestion_is_advisory_and_may_be_overridden()
    {
        PutawayTask task = NewTask();
        task.SuggestBin(BinId);

        task.Confirm(OtherBinId, new Quantity(10m, "EA"), Now);

        task.SuggestedBinId.Should().Be(BinId);
        task.ConfirmedBinId.Should().Be(OtherBinId);
    }

    [Fact]
    public void A_task_names_exactly_one_of_an_item_or_a_variant()
    {
        Action both = () => PutawayTask.Create(
            TenantId, StoreId, LocationId, ItemId, UuidV7.NewGuid(), new Quantity(1m, "EA"),
            PutawaySourceReferenceType.ManualReceipt, null);

        both.Should().Throw<WarehouseRuleException>().Which.Code.Should().Be("WAREHOUSE_EXACTLY_ONE_ITEM_OR_VARIANT");
    }
}
