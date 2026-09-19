using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Ecommerce;
using VumaRetail.Application.Orders.Commands;
using VumaRetail.Application.Pos;
using VumaRetail.Domain.Ecommerce;
using VumaRetail.Domain.Orders;

namespace VumaRetail.UnitTests.Ecommerce;

public sealed class EcommerceDomainTests
{
    private static readonly Guid TenantId = Guid.Parse("01900000-0000-7000-8000-000000000001");
    private static readonly Guid CompanyId = Guid.Parse("01900000-0000-7000-8000-000000000002");
    private static readonly Guid ChannelId = Guid.Parse("01900000-0000-7000-8000-000000000003");
    private static readonly Guid BasketId = Guid.Parse("01900000-0000-7000-8000-000000000004");

    [Fact]
    public void Checkout_expires_after_24_hours_and_cannot_be_decided_afterward()
    {
        DateTimeOffset created = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);
        CheckoutIntent intent = CheckoutIntent.Submit(TenantId, CompanyId, ChannelId, BasketId, "customer-1", "key-1", "fingerprint", created);

        intent.ExpiresAtUtc.Should().Be(created.AddHours(24));
        intent.Expire(created.AddHours(24));
        intent.Status.Should().Be(CheckoutIntentStatus.Expired);
        FluentActions.Invoking(() => intent.Confirm(created.AddHours(24))).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Checkout_decision_boundary_expires_pending_intent_without_a_background_job()
    {
        DateTimeOffset created = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);
        CheckoutIntent intent = CheckoutIntent.Submit(TenantId, CompanyId, ChannelId, BasketId, "customer-1", "key-1", "fingerprint", created);

        FluentActions.Invoking(() => intent.Reject("store offline", created.AddHours(24)))
            .Should().Throw<InvalidOperationException>();
        intent.Status.Should().Be(CheckoutIntentStatus.Expired);
        intent.DecidedAtUtc.Should().Be(created.AddHours(24));
    }

    [Fact]
    public void Checkout_authoritative_order_attachment_is_idempotent_and_rejects_replacement()
    {
        CheckoutIntent intent = CheckoutIntent.Submit(TenantId, CompanyId, ChannelId, BasketId, "customer-1", "key-1", "fingerprint", DateTimeOffset.UtcNow);
        Guid orderId = Guid.NewGuid();

        intent.AttachAuthoritativeOrder(orderId);
        intent.AttachAuthoritativeOrder(orderId);

        intent.AuthoritativeOrderId.Should().Be(orderId);
        FluentActions.Invoking(() => intent.AttachAuthoritativeOrder(Guid.NewGuid()))
            .Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task Confirmed_paid_checkout_creates_and_confirms_one_authoritative_order()
    {
        Guid checkoutId = Guid.NewGuid();
        Guid itemId = Guid.NewGuid();
        Guid locationId = Guid.NewGuid();
        Guid orderId = Guid.NewGuid();
        DateTimeOffset created = DateTimeOffset.UtcNow;
        CheckoutIntent checkout = CheckoutIntent.Submit(TenantId, CompanyId, ChannelId, BasketId, "owner", "key-1", "fingerprint", created);
        checkout.Confirm(created.AddMinutes(1));
        CommerceBasket basket = CommerceBasket.Open(TenantId, CompanyId, ChannelId, "owner", created);
        PublishedProduct product = PublishedProduct.Publish(TenantId, CompanyId, ChannelId, itemId, null, "SKU-1", "Item 1", null, 100m, "ZAR", 10m, created, 1);
        CommerceBasketLine line = CommerceBasketLine.Add(TenantId, CompanyId, BasketId, product.Id, 2m, 90m, 100m, "ZAR");

        var checkouts = Substitute.For<ICheckoutIntentRepository>();
        checkouts.FindAsync(checkoutId, Arg.Any<CancellationToken>()).Returns(checkout);
        var baskets = Substitute.For<ICommerceBasketRepository>();
        baskets.FindAsync(BasketId, Arg.Any<CancellationToken>()).Returns(basket);
        var basketLines = Substitute.For<ICommerceBasketLineRepository>();
        basketLines.ListForBasketAsync(basket.Id, Arg.Any<CancellationToken>()).Returns(new[] { line });
        var products = Substitute.For<IPublishedProductRepository>();
        products.ListAsync(ChannelId, 200, Arg.Any<CancellationToken>()).Returns(new[] { product });
        var attempts = Substitute.For<IPaymentAttemptRepository>();
        attempts.HasCapturedForCheckoutAsync(checkout.Id, Arg.Any<CancellationToken>()).Returns(true);
        var catalog = Substitute.For<ISellableItemResolver>();
        catalog.ResolveAsync(itemId, null, Arg.Any<CancellationToken>())
            .Returns(new SellableItem(itemId, null, "Item 1", "EA", "STANDARD"));
        var createOrder = Substitute.For<ICommandHandler<CreateOrderCommand, Guid>>();
        createOrder.HandleAsync(Arg.Any<CreateOrderCommand>(), Arg.Any<CancellationToken>()).Returns(orderId);
        var addOrderLine = Substitute.For<ICommandHandler<AddOrderLineCommand, Guid>>();
        addOrderLine.HandleAsync(Arg.Any<AddOrderLineCommand>(), Arg.Any<CancellationToken>()).Returns(Guid.NewGuid());
        var confirmOrder = Substitute.For<ICommandHandler<ConfirmOrderCommand, Unit>>();
        confirmOrder.HandleAsync(Arg.Any<ConfirmOrderCommand>(), Arg.Any<CancellationToken>()).Returns(Unit.Value);
        var company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(CompanyId);

        var handler = new CreateAuthoritativeOrderFromCheckoutCommandHandler(
            checkouts, baskets, basketLines, products, attempts, catalog, createOrder, addOrderLine,
            confirmOrder, company);

        Guid result = await handler.HandleAsync(new CreateAuthoritativeOrderFromCheckoutCommand(
            checkoutId, CompanyId, "owner", locationId, OrderFulfilmentType.ClickAndCollect));

        result.Should().Be(orderId);
        checkout.AuthoritativeOrderId.Should().Be(orderId);
        await createOrder.Received(1).HandleAsync(Arg.Is<CreateOrderCommand>(value => value.CompanyId == CompanyId), Arg.Any<CancellationToken>());
        await addOrderLine.Received(1).HandleAsync(Arg.Any<AddOrderLineCommand>(), Arg.Any<CancellationToken>());
        await confirmOrder.Received(1).HandleAsync(Arg.Is<ConfirmOrderCommand>(value => value.SalesOrderId == orderId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Checkout_authorization_is_replay_safe_and_persists_one_provider_attempt()
    {
        CheckoutIntent checkout = CheckoutIntent.Submit(TenantId, CompanyId, ChannelId, BasketId,
            "owner", "key", "fingerprint", DateTimeOffset.UtcNow);
        CommerceBasketLine line = CommerceBasketLine.Add(TenantId, CompanyId, BasketId,
            Guid.NewGuid(), 2m, 1m, 100m, "ZAR");
        var checkouts = Substitute.For<ICheckoutIntentRepository>();
        checkouts.FindAsync(checkout.Id, Arg.Any<CancellationToken>()).Returns(checkout);
        var lines = Substitute.For<ICommerceBasketLineRepository>();
        lines.ListForBasketAsync(BasketId, Arg.Any<CancellationToken>()).Returns([line]);
        var attempts = Substitute.For<IPaymentAttemptRepository>();
        var gateway = Substitute.For<IPaymentGateway>();
        PaymentGatewayAuthorization authorization = new("tj-payment-auth", new Uri("https://tj.example/pay"), "Authorised", "TJ-AUTH");
        gateway.AuthorizeAsync(Arg.Any<PaymentAuthorizationRequest>(), Arg.Any<CancellationToken>()).Returns(authorization);
        var company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(CompanyId);
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var handler = new BeginCheckoutPaymentCommandHandler(checkouts, lines, attempts, gateway, company, tenant, clock);
        BeginCheckoutPaymentCommand command = new(checkout.Id, CompanyId, "owner",
            new Uri("https://merchant.example/return"), new Uri("https://merchant.example/cancel"),
            new Uri("https://merchant.example/notify"));

        PaymentGatewayAuthorization first = await handler.HandleAsync(command);
        first.Should().Be(authorization);
        attempts.Received(1).Add(Arg.Is<PaymentAttempt>(value =>
            value.EventId == $"payment-authorization:{checkout.Id:N}" && value.Status == PaymentAttemptStatus.Authorised));

        string fingerprintInput = string.Join('|', checkout.Id, $"VUMA-{checkout.Id:N}", 200m, "ZAR",
            command.ReturnUrl, command.CancelUrl, command.NotificationUrl);
        PaymentAttempt replay = PaymentAttempt.Record(TenantId, CompanyId, checkout.Id,
            $"payment-authorization:{checkout.Id:N}",
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintInput))),
            authorization.ProviderPaymentId, PaymentAttemptStatus.Authorised, authorization.ProviderReference, clock.UtcNow);
        attempts.FindByEventIdAsync($"payment-authorization:{checkout.Id:N}", Arg.Any<CancellationToken>()).Returns(replay);

        PaymentGatewayAuthorization second = await handler.HandleAsync(command);
        second.ProviderPaymentId.Should().Be(authorization.ProviderPaymentId);
        await gateway.Received(1).AuthorizeAsync(Arg.Any<PaymentAuthorizationRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Payment_webhook_signature_is_constant_time_verified_and_tamper_safe()
    {
        const string body = "{\"eventId\":\"evt-1\"}";
        const string secret = "test-secret-with-sufficient-entropy";
        string signature = "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body)));

        PaymentWebhookSecurity.Verify(body, signature, secret).Should().BeTrue();
        PaymentWebhookSecurity.Verify(body + " ", signature, secret).Should().BeFalse();
        PaymentWebhookSecurity.Verify(body, signature, "wrong-secret").Should().BeFalse();
    }

    [Fact]
    public void Basket_line_keeps_server_authoritative_price_separate_from_browser_advisory_price()
    {
        CommerceBasketLine line = CommerceBasketLine.Add(TenantId, CompanyId, BasketId, Guid.NewGuid(),
            2m, 1m, 100m, "ZAR");

        line.AdvisoryUnitPrice.Should().Be(1m);
        line.AuthoritativeUnitPrice.Should().Be(100m);
    }

    [Theory]
    [InlineData(PaymentAttemptStatus.Authorised, PaymentAttemptStatus.Captured, true)]
    [InlineData(PaymentAttemptStatus.Captured, PaymentAttemptStatus.Authorised, false)]
    [InlineData(PaymentAttemptStatus.Failed, PaymentAttemptStatus.Captured, false)]
    [InlineData(PaymentAttemptStatus.Captured, PaymentAttemptStatus.Reversed, true)]
    public void Payment_status_transitions_are_monotonic(PaymentAttemptStatus current, PaymentAttemptStatus next, bool allowed)
        => PaymentAttempt.IsAllowedTransition(current, next).Should().Be(allowed);

    [Fact]
    public async Task Payment_event_replay_from_another_company_is_refused()
    {
        var companyId = Guid.NewGuid();
        var existing = PaymentAttempt.Record(Guid.NewGuid(), companyId, Guid.NewGuid(), "evt-1", "fingerprint",
            "provider-1", PaymentAttemptStatus.Authorised, null, DateTimeOffset.UtcNow);
        var attempts = Substitute.For<IPaymentAttemptRepository>();
        attempts.FindByEventIdAsync("evt-1", Arg.Any<CancellationToken>()).Returns(existing);
        var company = Substitute.For<ICompanyContext>();
        Guid otherCompanyId = Guid.NewGuid();
        company.CompanyId.Returns((Guid?)otherCompanyId);

        var action = () => new ApplyPaymentNotificationCommandHandler(
            Substitute.For<ICheckoutIntentRepository>(), attempts,
            Substitute.For<ITenantContext>(), company,
            Substitute.For<IClock>()).HandleAsync(new ApplyPaymentNotificationCommand(
                Guid.NewGuid(), otherCompanyId, "evt-1", "fingerprint", "provider-1", "Authorised", null));

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Payment event belongs to another company.");
    }

    [Fact]
    public async Task Payment_event_replay_rejects_changed_checkout_or_provider_payload()
    {
        Guid companyId = Guid.NewGuid();
        Guid checkoutId = Guid.NewGuid();
        var existing = PaymentAttempt.Record(Guid.NewGuid(), companyId, checkoutId, "evt-2", "fingerprint",
            "provider-1", PaymentAttemptStatus.Authorised, "ref-1", DateTimeOffset.UtcNow);
        var attempts = Substitute.For<IPaymentAttemptRepository>();
        attempts.FindByEventIdAsync("evt-2", Arg.Any<CancellationToken>()).Returns(existing);
        var company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(companyId);

        var action = () => new ApplyPaymentNotificationCommandHandler(
            Substitute.For<ICheckoutIntentRepository>(), attempts, Substitute.For<ITenantContext>(), company,
            Substitute.For<IClock>()).HandleAsync(new ApplyPaymentNotificationCommand(
                Guid.NewGuid(), companyId, "evt-2", "fingerprint", "provider-2", "Authorised", "ref-1"));

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Payment event was replayed with different content.");
    }

    [Fact]
    public async Task Payment_capture_calls_gateway_once_and_replays_without_a_second_capture()
    {
        Guid checkoutId = Guid.NewGuid();
        var checkout = CheckoutIntent.Submit(TenantId, CompanyId, ChannelId, BasketId, "owner", "key", "fingerprint", DateTimeOffset.UtcNow);
        var checkouts = Substitute.For<ICheckoutIntentRepository>();
        checkouts.FindAsync(checkoutId, Arg.Any<CancellationToken>()).Returns(checkout);
        var attempts = Substitute.For<IPaymentAttemptRepository>();
        var authorised = PaymentAttempt.Record(TenantId, CompanyId, checkoutId, "authorised-event", "auth-fingerprint",
            "provider-1", PaymentAttemptStatus.Authorised, "auth-ref", DateTimeOffset.UtcNow);
        attempts.FindLatestForCheckoutAsync(checkoutId, "provider-1", Arg.Any<CancellationToken>()).Returns(authorised);
        var gateway = Substitute.For<IPaymentGateway>();
        gateway.CaptureAsync(Arg.Any<PaymentGatewayOperation>(), Arg.Any<CancellationToken>())
            .Returns(new PaymentGatewayResult("provider-1", "captured", "capture-ref"));
        var company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(CompanyId);
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);

        var command = new ExecutePaymentOperationCommand(checkoutId, CompanyId, PaymentOperationKind.Capture,
            "provider-1", "VUMA-1", 125m, "ZAR", "operation-1");
        var handler = new ExecutePaymentOperationCommandHandler(checkouts, attempts, gateway, company, tenant,
            Substitute.For<IClock>());

        await handler.HandleAsync(command);

        string operationFingerprintInput = string.Join('|', checkoutId, PaymentOperationKind.Capture, "provider-1", "VUMA-1", 125m, "ZAR");
        attempts.FindByEventIdAsync("payment-operation:operation-1", Arg.Any<CancellationToken>())
            .Returns(PaymentAttempt.Record(TenantId, CompanyId, checkoutId, "payment-operation:operation-1",
                Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(operationFingerprintInput))),
                "provider-1", PaymentAttemptStatus.Captured, "capture-ref", DateTimeOffset.UtcNow));
        await handler.HandleAsync(command);
        await gateway.Received(1).CaptureAsync(Arg.Any<PaymentGatewayOperation>(), Arg.Any<CancellationToken>());
    }
}
