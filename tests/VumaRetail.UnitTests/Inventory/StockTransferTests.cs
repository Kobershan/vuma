using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Inventory;

/// <summary>The transfer document's invariants — a transfer moves value, it does not create it.</summary>
public sealed class StockTransferTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid Source = UuidV7.NewGuid();
    private static readonly Guid Destination = UuidV7.NewGuid();
    private static readonly Guid ItemId = UuidV7.NewGuid();

    private static StockTransfer Record(
        Guid? id = null,
        Guid? source = null,
        Guid? destination = null,
        decimal quantity = 5m,
        string? note = null)
        => StockTransfer.Record(
            id ?? UuidV7.NewGuid(),
            TenantId,
            source ?? Source,
            destination ?? Destination,
            ItemId,
            null,
            new Quantity(quantity, "EA"),
            new Money(20m, "ZAR"),
            UuidV7.NewGuid(),
            UuidV7.NewGuid(),
            note);

    [Fact]
    public void A_transfer_is_an_immutable_record()
        => Record().Should().BeAssignableTo<IImmutableRecord>();

    [Fact]
    public void A_transfer_takes_the_pre_minted_id_both_its_ledger_entries_already_carry()
    {
        // The id is minted before either entry is posted so both can reference it — the document is
        // found by that shared id rather than by a correlation column of its own.
        Guid transferId = UuidV7.NewGuid();

        Record(id: transferId).Id.Should().Be(transferId);
    }

    [Fact]
    public void A_transfer_carries_the_value_that_moved()
    {
        StockTransfer transfer = Record(quantity: 5m);

        transfer.Value.Amount.Should().Be(100m);
        transfer.Quantity.Value.Should().Be(5m);
    }

    [Fact]
    public void A_transfer_records_a_positive_quantity_and_lets_the_entries_carry_the_sign()
    {
        Action zero = () => Record(quantity: 0m);
        Action negative = () => Record(quantity: -1m);

        zero.Should().Throw<InventoryRuleException>()
            .Which.Code.Should().Be("INVENTORY_QUANTITY_MUST_BE_POSITIVE");
        negative.Should().Throw<InventoryRuleException>()
            .Which.Code.Should().Be("INVENTORY_QUANTITY_MUST_BE_POSITIVE");
    }

    [Fact]
    public void A_transfer_to_the_same_location_is_refused()
    {
        Action recording = () => Record(source: Source, destination: Source);

        recording.Should().Throw<InventoryRuleException>()
            .Which.Code.Should().Be("INVENTORY_TRANSFER_SAME_LOCATION");
    }

    [Fact]
    public void A_transfer_must_be_given_a_pre_minted_id()
    {
        Action recording = () => Record(id: Guid.Empty);

        recording.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_transfer_must_name_both_ends()
    {
        Action noSource = () => Record(source: Guid.Empty);
        Action noDestination = () => Record(destination: Guid.Empty);

        noSource.Should().Throw<ArgumentException>();
        noDestination.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_blank_note_is_stored_as_no_note()
    {
        Record(note: "  ").Note.Should().BeNull();
        Record(note: " restock ").Note.Should().Be("restock");
    }
}

/// <summary>The stock location's small surface — code normalisation and retirement.</summary>
public sealed class StockLocationTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();

    [Fact]
    public void A_location_code_is_upper_cased_and_trimmed()
    {
        StockLocation location = StockLocation.Create(TenantId, null, " main ", " Sandton back room ", StockLocationType.Warehouse);

        location.Code.Should().Be("MAIN");
        location.Name.Should().Be("Sandton back room");
    }

    [Fact]
    public void A_location_may_be_tenant_wide_with_no_owning_store()
    {
        // A central distribution warehouse serving several stores is a legitimate tenant-wide
        // location (R8), not a modelling accident.
        StockLocation location = StockLocation.Create(TenantId, null, "DC", "Central warehouse", StockLocationType.Warehouse);

        location.StoreId.Should().BeNull();
    }

    [Fact]
    public void Deactivating_retires_a_location_without_deleting_it()
    {
        StockLocation location = StockLocation.Create(TenantId, null, "MAIN", "Back room", StockLocationType.Warehouse);

        location.Deactivate();

        location.IsActive.Should().BeFalse();
        location.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void A_retired_location_can_be_brought_back()
    {
        StockLocation location = StockLocation.Create(TenantId, null, "MAIN", "Back room", StockLocationType.Warehouse);
        location.Deactivate();

        location.Activate();

        location.IsActive.Should().BeTrue();
    }

    [Fact]
    public void A_location_needs_a_tenant_a_code_and_a_name()
    {
        Action noTenant = () => StockLocation.Create(Guid.Empty, null, "MAIN", "Back room", StockLocationType.Warehouse);
        Action noCode = () => StockLocation.Create(TenantId, null, "  ", "Back room", StockLocationType.Warehouse);
        Action noName = () => StockLocation.Create(TenantId, null, "MAIN", "  ", StockLocationType.Warehouse);

        noTenant.Should().Throw<ArgumentException>();
        noCode.Should().Throw<ArgumentException>();
        noName.Should().Throw<ArgumentException>();
    }
}
