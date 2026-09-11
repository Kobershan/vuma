using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Connect;
using VumaRetail.Domain.Connect;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Connect;

public sealed class ConnectSettlementTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Successful_payment_captures_posts_both_ledgers_and_issues_remittance()
    {
        Guid retailer = Guid.NewGuid();
        Guid supplier = Guid.NewGuid();
        Guid connectionId = Guid.NewGuid();
        Guid paymentId = Guid.NewGuid();
        TradingConnection connection = TradingConnection.Request(supplier, retailer, "SUP", "RET", Now);
        connection.Accept("ZAR", 1000m, 3, 10m, Now);

        ITradingConnectionRepository connections = Substitute.For<ITradingConnectionRepository>();
        connections.FindForTenantAsync(connectionId, retailer, Arg.Any<CancellationToken>())
            .Returns(connection);
        IPaymentGateway gateway = Substitute.For<IPaymentGateway>();
        gateway.AuthoriseAsync(Arg.Any<ConnectPaymentRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GatewayPaymentResult(ConnectPaymentStatus.Authorised, "AUTH-1"));
        gateway.CaptureAsync(paymentId, "AUTH-1", Arg.Any<CancellationToken>())
            .Returns(new GatewayPaymentResult(ConnectPaymentStatus.Captured, "AUTH-1"));
        ISettlementProvider settlement = Substitute.For<ISettlementProvider>();
        settlement.SettleAsync(Arg.Any<ConnectPaymentRequest>(), "AUTH-1", Arg.Any<CancellationToken>())
            .Returns(new SettlementResult(ConnectPaymentStatus.Captured, "REM-1"));
        IConnectLedgerPoster ledger = Substitute.For<IConnectLedgerPoster>();
        IConnectRemittanceRepository remittances = Substitute.For<IConnectRemittanceRepository>();
        remittances.FindSettlementAsync(paymentId, retailer, Arg.Any<CancellationToken>())
            .Returns((SettlementResult?)null);
        ITenantContext tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(retailer);
        IClock clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);

        SettlementResult result = await new SettleConnectPaymentCommandHandler(
            gateway, settlement, ledger, remittances, connections, tenant, clock)
            .HandleAsync(new SettleConnectPaymentCommand(
                paymentId, connectionId, "INV-1", 100m, "ZAR", ConnectPaymentMethod.Eft));

        result.Status.Should().Be(ConnectPaymentStatus.Captured);
        result.RemittanceReference.Should().Be("REM-1");
        await ledger.Received(1).PostRetailerPayableAsync(Arg.Any<ConnectPaymentRequest>(), "REM-1", Arg.Any<CancellationToken>());
        await ledger.Received(1).PostSupplierReceivableAsync(Arg.Any<ConnectPaymentRequest>(), "REM-1", Arg.Any<CancellationToken>());
        remittances.Received(1).Add(Arg.Is<ConnectRemittanceAdvice>(x => x.PaymentId == paymentId && x.RemittanceReference == "REM-1"));
    }

    [Fact]
    public async Task Replayed_payment_returns_existing_remittance_without_moving_money_again()
    {
        Guid retailer = Guid.NewGuid();
        Guid paymentId = Guid.NewGuid();
        SettlementResult existing = new(ConnectPaymentStatus.Captured, "REM-REPLAY");
        IConnectRemittanceRepository remittances = Substitute.For<IConnectRemittanceRepository>();
        remittances.FindSettlementAsync(paymentId, retailer, Arg.Any<CancellationToken>()).Returns(existing);
        ITenantContext tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(retailer);
        ITradingConnectionRepository connections = Substitute.For<ITradingConnectionRepository>();
        TradingConnection connection = TradingConnection.Request(Guid.NewGuid(), retailer, "SUP", "RET", Now);
        connection.Accept("ZAR", 1000m, 3, 10m, Now);
        connections.FindForTenantAsync(Arg.Any<Guid>(), retailer, Arg.Any<CancellationToken>())
            .Returns(connection);
        IClock clock = Substitute.For<IClock>();
        SettlementResult result = await new SettleConnectPaymentCommandHandler(
            Substitute.For<IPaymentGateway>(), Substitute.For<ISettlementProvider>(),
            Substitute.For<IConnectLedgerPoster>(), remittances, connections, tenant, clock)
            .HandleAsync(new SettleConnectPaymentCommand(
                paymentId, Guid.NewGuid(), "INV-1", 100m, "ZAR", ConnectPaymentMethod.Card));

        result.Should().BeSameAs(existing);
    }
}
