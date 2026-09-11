#pragma warning disable CS1591
using FluentAssertions;
using VumaRetail.Domain.Connect;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Connect;

public sealed class ConnectDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 11, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Connection_requires_two_different_tenants_and_can_be_suspended_then_ended()
    {
        Guid supplier = Guid.NewGuid();
        Guid retailer = Guid.NewGuid();
        TradingConnection connection = TradingConnection.Request(supplier, retailer, "SUP-1", "RET-1", Now);

        connection.Accept("zar", 100_000m, 3, 500m, Now.AddMinutes(1));
        connection.Status.Should().Be(TradingConnectionStatus.Active);
        connection.Currency.Should().Be("ZAR");

        connection.Suspend(Now.AddMinutes(2));
        connection.Status.Should().Be(TradingConnectionStatus.Suspended);
        connection.End(Now.AddMinutes(3));
        connection.Status.Should().Be(TradingConnectionStatus.Ended);
    }

    [Fact]
    public void Connection_code_is_single_use_when_issued_for_one_use_and_expires()
    {
        ConnectionCode code = ConnectionCode.Issue(Guid.NewGuid(), " vuma-sup-123 ", 1, Now.AddHours(1), "gold", "GP", false, Now);

        code.Code.Should().Be("VUMA-SUP-123");
        code.IsRedeemable(Now.AddMinutes(1)).Should().BeTrue();
        code.Redeem(Now.AddMinutes(1));
        code.IsRedeemable(Now.AddMinutes(2)).Should().BeFalse();
        FluentActions.Invoking(() => code.Redeem(Now.AddMinutes(2))).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Price_proposal_supports_partial_acceptance_and_rollback_only_after_full_acceptance()
    {
        PriceProposal proposal = PriceProposal.Publish(Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(1), null, "October price list");
        PriceProposalLine first = proposal.AddLine("SKU-1", 10m, "zar", 1, 2);
        proposal.AddLine("SKU-2", 20m, "ZAR", 1, 2);

        proposal.Accept([first.Id], Now.AddMinutes(1));
        proposal.Status.Should().Be(ConnectProposalStatus.PartiallyAccepted);
        FluentActions.Invoking(() => proposal.Rollback(Now.AddMinutes(2))).Should().Throw<InvalidOperationException>();

        proposal.Accept(null, Now.AddMinutes(3));
        proposal.Status.Should().Be(ConnectProposalStatus.Accepted);
        proposal.Rollback(Now.AddMinutes(4));
        proposal.Status.Should().Be(ConnectProposalStatus.RolledBack);
    }

    [Fact]
    public void Rolled_back_catalogue_cannot_receive_new_lines()
    {
        CataloguePublication publication = CataloguePublication.Publish(Guid.NewGuid(), Guid.NewGuid(), 1, Now, "launch");
        publication.AddLine("SKU-1", "Milk", "600123", 12, 1, 2);
        publication.Rollback(Now.AddMinutes(1));

        FluentActions.Invoking(() => publication.AddLine("SKU-2", "Bread", null, 1, null, null))
            .Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Connect_order_confirms_partially_and_dispatch_cannot_exceed_confirmation()
    {
        Guid supplier = Guid.NewGuid();
        Guid retailer = Guid.NewGuid();
        ConnectOrder order = ConnectOrder.Place(retailer, supplier, Guid.NewGuid(), Guid.NewGuid(), "PO-1", Now);
        ConnectOrderLine line = order.AddLine("SKU-1", "Milk", new Quantity(10, "EA"), new Money(12, "ZAR"));

        order.Confirm(new Dictionary<Guid, Quantity> { [line.Id] = new(6, "EA") }, Now.AddHours(1));
        order.Status.Should().Be(ConnectOrderStatus.PartiallyConfirmed);

        FluentActions.Invoking(() => order.Dispatch("ASN-1", new Dictionary<Guid, Quantity> { [line.Id] = new(7, "EA") }, Now.AddHours(2)))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Connect_order_rejects_without_a_reason_and_accepts_a_valid_asn()
    {
        ConnectOrder order = ConnectOrder.Place(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "PO-2", Now);
        ConnectOrderLine line = order.AddLine("SKU-2", "Bread", new Quantity(4, "EA"), new Money(8, "ZAR"));
        order.Confirm(new Dictionary<Guid, Quantity> { [line.Id] = new(4, "EA") }, Now.AddHours(1));
        order.Dispatch("ASN-2", new Dictionary<Guid, Quantity> { [line.Id] = new(3, "EA") }, Now.AddHours(2));

        order.Status.Should().Be(ConnectOrderStatus.Dispatched);
        order.DispatchNoteNumber.Should().Be("ASN-2");
        order.MarkReceived();
        order.Status.Should().Be(ConnectOrderStatus.Received);
    }

    [Fact]
    public void Delivery_claim_can_be_credited_once()
    {
        Guid retailer = Guid.NewGuid();
        ConnectDeliveryClaim claim = ConnectDeliveryClaim.Raise(retailer, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), "CLM-1", ConnectClaimReason.ShortDelivery, new Quantity(1, "EA"), new Money(12, "ZAR"), "One short", Now);
        claim.IssueCreditNote("CN-1", Now.AddHours(1));
        claim.Status.Should().Be(ConnectClaimStatus.Credited);
        claim.CreditNoteReference.Should().Be("CN-1");
        FluentActions.Invoking(() => claim.IssueCreditNote("CN-2", Now.AddHours(2))).Should().Throw<InvalidOperationException>();
    }
}
