using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Warehouse;

namespace VumaRetail.UnitTests.Warehouse;

/// <summary>The pick wave state machine: open → released → picked → packed → shipped, or cancelled.</summary>
public sealed class PickWaveTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid StoreId = UuidV7.NewGuid();
    private static readonly Guid LocationId = UuidV7.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static PickWave NewWave() => PickWave.Open(TenantId, StoreId, LocationId);

    [Fact]
    public void A_new_wave_opens_in_the_open_state()
    {
        PickWave wave = NewWave();

        wave.Status.Should().Be(PickWaveStatus.Open);
        wave.ReleasedAt.Should().BeNull();
    }

    [Fact]
    public void The_wave_moves_through_every_state_in_order()
    {
        PickWave wave = NewWave();

        wave.Release(Now);
        wave.Status.Should().Be(PickWaveStatus.Released);
        wave.ReleasedAt.Should().Be(Now);

        wave.MarkPicked(Now.AddMinutes(1));
        wave.Status.Should().Be(PickWaveStatus.Picked);

        wave.MarkPacked(Now.AddMinutes(2));
        wave.Status.Should().Be(PickWaveStatus.Packed);

        wave.MarkShipped(Now.AddMinutes(3));
        wave.Status.Should().Be(PickWaveStatus.Shipped);
        wave.ShippedAt.Should().Be(Now.AddMinutes(3));
    }

    [Fact]
    public void A_state_cannot_be_skipped()
    {
        PickWave wave = NewWave();

        Action packing = () => wave.MarkPacked(Now);

        packing.Should().Throw<WarehouseRuleException>()
            .Which.Code.Should().Be("WAREHOUSE_PICK_WAVE_WRONG_STATUS");
    }

    [Fact]
    public void An_open_or_released_wave_can_be_cancelled()
    {
        PickWave open = NewWave();
        open.Cancel();
        open.Status.Should().Be(PickWaveStatus.Cancelled);

        PickWave released = NewWave();
        released.Release(Now);
        released.Cancel();
        released.Status.Should().Be(PickWaveStatus.Cancelled);
    }

    [Fact]
    public void A_shipped_wave_cannot_be_cancelled()
    {
        PickWave wave = NewWave();
        wave.Release(Now);
        wave.MarkPicked(Now);
        wave.MarkPacked(Now);
        wave.MarkShipped(Now);

        Action cancelling = () => wave.Cancel();

        cancelling.Should().Throw<WarehouseRuleException>();
    }
}

/// <summary>One demand line: allocate, confirm in full or short, or cancel.</summary>
public sealed class PickTaskTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid StoreId = UuidV7.NewGuid();
    private static readonly Guid PickWaveId = UuidV7.NewGuid();
    private static readonly Guid ItemId = UuidV7.NewGuid();
    private static readonly Guid BinId = UuidV7.NewGuid();

    private static PickTask NewTask(decimal requested = 10m) => PickTask.Create(
        TenantId, StoreId, PickWaveId, ItemId, null, new Quantity(requested, "EA"), "SO-1001");

    [Fact]
    public void A_new_task_is_pending_and_unallocated()
    {
        PickTask task = NewTask();

        task.Status.Should().Be(PickTaskStatus.Pending);
        task.AllocatedBinId.Should().BeNull();
        task.OutboundReference.Should().Be("SO-1001");
    }

    [Fact]
    public void Allocating_moves_the_task_to_allocated()
    {
        PickTask task = NewTask();

        task.Allocate(BinId, new Quantity(10m, "EA"));

        task.Status.Should().Be(PickTaskStatus.Allocated);
        task.AllocatedBinId.Should().Be(BinId);
        task.AllocatedQuantity!.Value.Value.Should().Be(10m);
    }

    [Fact]
    public void Confirming_the_full_allocated_quantity_marks_the_task_picked()
    {
        PickTask task = NewTask();
        task.Allocate(BinId, new Quantity(10m, "EA"));

        task.ConfirmPick(new Quantity(10m, "EA"));

        task.Status.Should().Be(PickTaskStatus.Picked);
        task.PickedQuantity!.Value.Value.Should().Be(10m);
    }

    [Fact]
    public void Confirming_less_than_allocated_is_a_short_pick_not_a_refusal()
    {
        // Business rule 6: a bin emptier than the system believed is recorded, not refused.
        PickTask task = NewTask();
        task.Allocate(BinId, new Quantity(10m, "EA"));

        task.ConfirmPick(new Quantity(6m, "EA"));

        task.Status.Should().Be(PickTaskStatus.ShortPicked);
        task.PickedQuantity!.Value.Value.Should().Be(6m);
    }

    [Fact]
    public void Confirming_more_than_allocated_is_refused()
    {
        PickTask task = NewTask();
        task.Allocate(BinId, new Quantity(10m, "EA"));

        Action confirming = () => task.ConfirmPick(new Quantity(11m, "EA"));

        confirming.Should().Throw<WarehouseRuleException>()
            .Which.Code.Should().Be("WAREHOUSE_PICK_EXCEEDS_ALLOCATED");
    }

    [Fact]
    public void A_pending_task_cannot_be_confirmed_before_allocation()
    {
        PickTask task = NewTask();

        Action confirming = () => task.ConfirmPick(new Quantity(1m, "EA"));

        confirming.Should().Throw<WarehouseRuleException>()
            .Which.Code.Should().Be("WAREHOUSE_PICK_TASK_WRONG_STATUS");
    }

    [Fact]
    public void A_pending_or_allocated_task_can_be_cancelled()
    {
        PickTask pending = NewTask();
        pending.Cancel();
        pending.Status.Should().Be(PickTaskStatus.Cancelled);

        PickTask allocated = NewTask();
        allocated.Allocate(BinId, new Quantity(10m, "EA"));
        allocated.Cancel();
        allocated.Status.Should().Be(PickTaskStatus.Cancelled);
    }

    [Fact]
    public void A_picked_task_cannot_be_cancelled()
    {
        PickTask task = NewTask();
        task.Allocate(BinId, new Quantity(10m, "EA"));
        task.ConfirmPick(new Quantity(10m, "EA"));

        Action cancelling = () => task.Cancel();

        cancelling.Should().Throw<WarehouseRuleException>();
    }

    [Fact]
    public void An_outbound_reference_is_required()
    {
        Action creating = () => PickTask.Create(
            TenantId, StoreId, PickWaveId, ItemId, null, new Quantity(1m, "EA"), "   ");

        creating.Should().Throw<ArgumentException>();
    }
}
