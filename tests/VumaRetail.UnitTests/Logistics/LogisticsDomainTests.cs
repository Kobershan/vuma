using VumaRetail.Domain.Logistics;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Logistics;

public sealed class LogisticsDomainTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public void Shipment_transitions_are_forward_only()
    {
        Shipment shipment = Shipment.Create(TenantId, null, "S-1", null, null, null, "T-1", "1 Main", null, "Durban", "4001", "ZA");
        shipment.Dispatch(Now);
        shipment.MarkDelivered(Now.AddMinutes(10));
        shipment.Status.Should().Be(LogisticsShipmentStatus.Delivered);
        Action dispatch = () => shipment.Dispatch(Now.AddMinutes(20));
        dispatch.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Proof_of_delivery_rejects_invalid_coordinates_and_blank_recipient()
    {
        Action blankRecipient = () => ProofOfDelivery.Record(TenantId, null, UuidV7.NewGuid(), null, ProofOfDeliveryOutcome.Delivered, " ", null, null, null, null, Now, null);
        Action invalidLatitude = () => ProofOfDelivery.Record(TenantId, null, UuidV7.NewGuid(), null, ProofOfDeliveryOutcome.Delivered, "A Person", null, null, 91, 20, Now, null);
        blankRecipient.Should().Throw<ArgumentException>();
        invalidLatitude.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Proof_of_delivery_is_immutable()
    {
        typeof(ProofOfDelivery).Should().BeAssignableTo<VumaRetail.Domain.Entities.IImmutableRecord>();
        typeof(ProofOfDelivery).GetProperties().Where(x => x.CanWrite).Should().OnlyContain(x => !x.SetMethod!.IsPublic);
    }
}
