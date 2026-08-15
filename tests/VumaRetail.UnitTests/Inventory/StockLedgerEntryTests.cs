using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Inventory;

/// <summary>
/// The append-only ledger entry's structural invariants — the ones that must hold at construction,
/// because the table is append-only and a bad row can never be corrected by an update.
/// </summary>
public sealed class StockLedgerEntryTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid StoreId = UuidV7.NewGuid();
    private static readonly Guid LocationId = UuidV7.NewGuid();
    private static readonly Guid ItemId = UuidV7.NewGuid();

    private static StockLedgerEntry Post(
        Quantity? quantity = null,
        StockMovementType movementType = StockMovementType.Receipt,
        StockReferenceType referenceType = StockReferenceType.Manual,
        Guid? referenceId = null,
        AdjustmentReasonCode? reasonCode = null,
        string? note = null)
        => StockLedgerEntry.Post(
            TenantId, StoreId, LocationId, ItemId, null, movementType,
            quantity ?? new Quantity(5m, "EA"), new Money(20m, "ZAR"),
            referenceType, referenceId, reasonCode, note);

    [Fact]
    public void A_ledger_entry_is_an_immutable_record()
    {
        // Marked so the persistence layer refuses it in the Modified or Deleted state (§7 rule 7) —
        // the append-only guarantee is structural rather than a convention somebody remembers.
        Post().Should().BeAssignableTo<IImmutableRecord>();
    }

    [Fact]
    public void A_receipt_carries_its_value_as_quantity_extended_by_unit_cost()
    {
        StockLedgerEntry entry = Post(new Quantity(5m, "EA"));

        entry.Value.Amount.Should().Be(100m);
        entry.Value.Currency.Should().Be("ZAR");
    }

    [Fact]
    public void An_outbound_movement_carries_a_negative_quantity_and_so_a_negative_value()
    {
        // The sign lives on the quantity, not on a separate direction flag — which is what makes
        // "sum the ledger" equal "what is on hand" without a case statement.
        StockLedgerEntry entry = Post(new Quantity(-5m, "EA"), StockMovementType.SaleIssue, StockReferenceType.Sale, UuidV7.NewGuid());

        entry.Quantity.IsNegative.Should().BeTrue();
        entry.Value.Amount.Should().Be(-100m);
    }

    [Fact]
    public void A_zero_quantity_is_not_a_movement()
    {
        Action posting = () => Post(new Quantity(0m, "EA"));

        posting.Should().Throw<InventoryRuleException>()
            .Which.Code.Should().Be("INVENTORY_QUANTITY_MUST_BE_NON_ZERO");
    }

    [Fact]
    public void An_adjustment_must_carry_a_reason_code()
    {
        // §7 rule 6: a correction is a documented ledger entry, and "documented" means somebody had
        // to say why.
        Action posting = () => Post(movementType: StockMovementType.Adjustment);

        posting.Should().Throw<InventoryRuleException>()
            .Which.Code.Should().Be("INVENTORY_ADJUSTMENT_REQUIRES_REASON");
    }

    [Fact]
    public void An_adjustment_with_a_reason_code_is_accepted()
    {
        StockLedgerEntry entry = Post(
            movementType: StockMovementType.Adjustment,
            reasonCode: AdjustmentReasonCode.Damage);

        entry.ReasonCode.Should().Be(AdjustmentReasonCode.Damage);
    }

    [Theory]
    [InlineData(StockReferenceType.Transfer)]
    [InlineData(StockReferenceType.Stocktake)]
    [InlineData(StockReferenceType.Sale)]
    public void A_movement_belonging_to_a_document_must_name_it(StockReferenceType referenceType)
    {
        Action posting = () => Post(referenceType: referenceType, referenceId: null);

        posting.Should().Throw<InventoryRuleException>()
            .Which.Code.Should().Be("INVENTORY_REFERENCE_ID_REQUIRED");
    }

    [Fact]
    public void A_manual_movement_needs_no_document()
    {
        StockLedgerEntry entry = Post(referenceType: StockReferenceType.Manual, referenceId: null);

        entry.ReferenceType.Should().Be(StockReferenceType.Manual);
        entry.ReferenceId.Should().BeNull();
    }

    [Fact]
    public void A_movement_names_exactly_one_of_an_item_or_a_variant()
    {
        Action both = () => StockLedgerEntry.Post(
            TenantId, StoreId, LocationId, ItemId, UuidV7.NewGuid(), StockMovementType.Receipt,
            new Quantity(1m, "EA"), new Money(1m, "ZAR"), StockReferenceType.Manual, null, null, null);

        both.Should().Throw<InventoryRuleException>()
            .Which.Code.Should().Be("INVENTORY_EXACTLY_ONE_ITEM_OR_VARIANT");
    }

    [Fact]
    public void A_blank_note_is_stored_as_no_note_rather_than_whitespace()
    {
        Post(note: "   ").Note.Should().BeNull();
        Post(note: "  damaged in transit  ").Note.Should().Be("damaged in transit");
    }

    [Fact]
    public void A_movement_must_belong_to_a_tenant_and_name_a_location()
    {
        Action noTenant = () => StockLedgerEntry.Post(
            Guid.Empty, StoreId, LocationId, ItemId, null, StockMovementType.Receipt,
            new Quantity(1m, "EA"), new Money(1m, "ZAR"), StockReferenceType.Manual, null, null, null);

        Action noLocation = () => StockLedgerEntry.Post(
            TenantId, StoreId, Guid.Empty, ItemId, null, StockMovementType.Receipt,
            new Quantity(1m, "EA"), new Money(1m, "ZAR"), StockReferenceType.Manual, null, null, null);

        noTenant.Should().Throw<ArgumentException>();
        noLocation.Should().Throw<ArgumentException>();
    }
}
