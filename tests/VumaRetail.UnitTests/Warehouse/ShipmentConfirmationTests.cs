using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Warehouse;

namespace VumaRetail.UnitTests.Warehouse;

/// <summary>
/// The shipment document itself: pre-minted id, free-text carrier fields, and immutable once recorded.
/// What it <em>issues</em> — one ledger entry per distinct SKU across the wave — is the handler's, and
/// is asserted end to end in <c>WarehouseCommandTests</c>.
/// </summary>
public sealed class ShipmentConfirmationTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid StoreId = UuidV7.NewGuid();
    private static readonly Guid PickWaveId = UuidV7.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public void A_shipment_records_its_wave_carrier_and_time()
    {
        Guid id = UuidV7.NewGuid();

        ShipmentConfirmation shipment = ShipmentConfirmation.Ship(
            id, TenantId, StoreId, PickWaveId, "Courier Co", "TRK-001", Now);

        shipment.Id.Should().Be(id, "the id is minted before the document so the ledger issue can reference it");
        shipment.PickWaveId.Should().Be(PickWaveId);
        shipment.Carrier.Should().Be("Courier Co");
        shipment.TrackingNumber.Should().Be("TRK-001");
        shipment.ShippedAt.Should().Be(Now);
    }

    [Fact]
    public void Blank_carrier_details_are_stored_as_null_rather_than_as_whitespace()
    {
        ShipmentConfirmation shipment = ShipmentConfirmation.Ship(
            UuidV7.NewGuid(), TenantId, StoreId, PickWaveId, "   ", "  ", Now);

        shipment.Carrier.Should().BeNull();
        shipment.TrackingNumber.Should().BeNull();
    }

    [Fact]
    public void Carrier_details_are_trimmed()
    {
        ShipmentConfirmation shipment = ShipmentConfirmation.Ship(
            UuidV7.NewGuid(), TenantId, StoreId, PickWaveId, "  Courier Co  ", "  TRK-002  ", Now);

        shipment.Carrier.Should().Be("Courier Co");
        shipment.TrackingNumber.Should().Be("TRK-002");
    }

    [Fact]
    public void A_shipment_without_a_pre_minted_id_a_tenant_or_a_wave_is_refused()
    {
        Action noId = () => ShipmentConfirmation.Ship(
            Guid.Empty, TenantId, StoreId, PickWaveId, null, null, Now);

        Action noTenant = () => ShipmentConfirmation.Ship(
            UuidV7.NewGuid(), Guid.Empty, StoreId, PickWaveId, null, null, Now);

        Action noWave = () => ShipmentConfirmation.Ship(
            UuidV7.NewGuid(), TenantId, StoreId, Guid.Empty, null, null, Now);

        noId.Should().Throw<ArgumentException>();
        noTenant.Should().Throw<ArgumentException>();
        noWave.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_shipment_is_an_immutable_record()
    {
        // Business rule: a correction is a new shipment, not an edit — the same shape
        // StockLedgerEntry and StockTransfer already use.
        typeof(ShipmentConfirmation).Should().BeAssignableTo<IImmutableRecord>();

        typeof(ShipmentConfirmation).GetProperties()
            .Where(property => property.CanWrite)
            .Should().OnlyContain(property => !property.SetMethod!.IsPublic,
                "nothing outside the aggregate may rewrite a shipped document");
    }
}
