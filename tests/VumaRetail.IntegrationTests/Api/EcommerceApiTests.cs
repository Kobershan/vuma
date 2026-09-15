using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Ecommerce;
using VumaRetail.Domain.Ecommerce;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Orders;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Registry;
using VumaRetail.IntegrationTests.Harness;
using VumaRetail.IntegrationTests.Orders;

namespace VumaRetail.IntegrationTests.Api;

/// <summary>Stage 21 storefront catalogue contract evidence.</summary>
[Collection(PostgresCollection.Name)]
public sealed class EcommerceApiTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Storefront_openapi_contains_the_published_products_route()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        using HttpResponseMessage openApiResponse = await harness.Client.GetAsync("/openapi/v1.json");
        if (!openApiResponse.IsSuccessStatusCode)
        {
            string body = await openApiResponse.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"OpenAPI returned {(int)openApiResponse.StatusCode}: {body}");
        }
        using JsonDocument document = JsonDocument.Parse(await openApiResponse.Content.ReadAsStringAsync());
        JsonElement paths = document.RootElement.GetProperty("paths");

        paths.TryGetProperty("/api/v1/storefront/products", out JsonElement route).Should().BeTrue();
        route.TryGetProperty("get", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/channels", out JsonElement channels).Should().BeTrue();
        channels.TryGetProperty("post", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/channels/{id}/products", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/storefront/baskets", out JsonElement baskets).Should().BeTrue();
        baskets.TryGetProperty("post", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/storefront/baskets/{id}/lines", out JsonElement lines).Should().BeTrue();
        lines.TryGetProperty("post", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/storefront/checkouts", out JsonElement checkouts).Should().BeTrue();
        checkouts.TryGetProperty("post", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/storefront/checkouts/{id}", out JsonElement checkout).Should().BeTrue();
        checkout.TryGetProperty("get", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/storefront/checkouts/{id}/confirm", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/storefront/checkouts/{id}/reject", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/storefront/checkouts/{id}/order", out JsonElement orders).Should().BeTrue();
        orders.TryGetProperty("post", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/storefront/checkouts/{id}/payment/{operation}", out JsonElement operations).Should().BeTrue();
        operations.TryGetProperty("post", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/storefront/webhooks/payments", out JsonElement webhooks).Should().BeTrue();
        webhooks.TryGetProperty("post", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/service/tickets", out JsonElement tickets).Should().BeTrue();
        tickets.TryGetProperty("post", out _).Should().BeTrue();
        tickets.TryGetProperty("get", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/service/tickets/{id}/close", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/service/sla/breaches", out JsonElement slaBreaches).Should().BeTrue();
        slaBreaches.TryGetProperty("get", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/service/warranties", out JsonElement warranties).Should().BeTrue();
        warranties.TryGetProperty("post", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/service/repairs", out JsonElement repairs).Should().BeTrue();
        repairs.TryGetProperty("post", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/service/custody", out JsonElement custody).Should().BeTrue();
        paths.TryGetProperty("/api/v1/service/parts", out _).Should().BeTrue();
        custody.TryGetProperty("get", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Signed_payment_webhook_replay_ten_times_persists_one_transition()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        Guid companyId = Guid.NewGuid();
        Guid checkoutId = await harness.InScopeAsync(async services =>
        {
            VumaRetailDbContext context = services.GetRequiredService<VumaRetailDbContext>();
            CheckoutIntent checkout = CheckoutIntent.Submit(harness.TenantId, companyId, Guid.NewGuid(), Guid.NewGuid(),
                "customer-replay", "checkout-key", "basket-fingerprint", DateTimeOffset.UtcNow);
            context.CheckoutIntents.Add(checkout);
            await context.CommitAsync();
            return checkout.Id;
        });

        string body = JsonSerializer.Serialize(new
        {
            CheckoutId = checkoutId,
            CompanyId = companyId,
            EventId = "tj-event-replay-1",
            ProviderPaymentId = "tj-payment-1",
            Status = "Authorised",
            ProviderReference = "TJ-AUTH-1"
        });
        string signature = "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes("test-webhook-secret"), Encoding.UTF8.GetBytes(body)));

        for (int attempt = 0; attempt < 10; attempt++)
        {
            using HttpRequestMessage request = new(HttpMethod.Post, "/api/v1/storefront/webhooks/payments")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-Vuma-Payment-Signature", signature);
            using HttpResponseMessage response = await harness.Client.SendAsync(request);
            response.StatusCode.Should().Be(System.Net.HttpStatusCode.Accepted);
        }

        int persisted = await harness.InScopeAsync(async services =>
            await services.GetRequiredService<VumaRetailDbContext>().PaymentAttempts
                .CountAsync(attempt => attempt.EventId == "tj-event-replay-1"));
        persisted.Should().Be(1);
    }

    [Fact]
    public async Task Payment_capture_endpoint_replays_without_a_second_gateway_call()
    {
        StubPaymentGateway gateway = new();
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture, configureServices: services =>
        {
            services.RemoveAll<IPaymentGateway>();
            services.AddSingleton<IPaymentGateway>(gateway);
        });
        Guid companyId = Guid.NewGuid();
        Guid checkoutId = await harness.InScopeAsync(async services =>
        {
            VumaRetailDbContext context = services.GetRequiredService<VumaRetailDbContext>();
            CheckoutIntent checkout = CheckoutIntent.Submit(harness.TenantId, companyId, Guid.NewGuid(), Guid.NewGuid(),
                "customer-capture", "checkout-key", "basket-fingerprint", DateTimeOffset.UtcNow);
            context.CheckoutIntents.Add(checkout);
            context.PaymentAttempts.Add(PaymentAttempt.Record(harness.TenantId, companyId, checkout.Id,
                "authorization-event", "authorization-fingerprint", "tj-payment-1",
                PaymentAttemptStatus.Authorised, "TJ-AUTH-1", DateTimeOffset.UtcNow));
            await context.CommitAsync();
            return checkout.Id;
        });

        await harness.CreateUserAsync("payment-operator", "CorrectHorseBattery1", EcommercePermissions.Payment);
        using HttpClient client = await harness.SignInAsync("payment-operator");
        var request = new
        {
            CompanyId = companyId,
            ProviderPaymentId = "tj-payment-1",
            MerchantReference = "VUMA-CAPTURE-1",
            Amount = 125m,
            Currency = "ZAR",
            IdempotencyKey = "capture-operation-1"
        };

        HttpResponseMessage first = await client.PostAsJsonAsync(
            $"/api/v1/storefront/checkouts/{checkoutId:D}/payment/capture", request);
        HttpResponseMessage replay = await client.PostAsJsonAsync(
            $"/api/v1/storefront/checkouts/{checkoutId:D}/payment/capture", request);

        first.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        replay.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        gateway.CaptureCalls.Should().Be(1);
        int persisted = await harness.InScopeAsync(async services =>
            await services.GetRequiredService<VumaRetailDbContext>().PaymentAttempts
                .CountAsync(attempt => attempt.EventId == "payment-operation:capture-operation-1"));
        persisted.Should().Be(1);
    }

    [Fact]
    public async Task Payment_authorization_endpoint_persists_and_replays_without_a_second_gateway_call()
    {
        StubPaymentGateway gateway = new();
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture, configureServices: services =>
        {
            services.RemoveAll<IPaymentGateway>();
            services.AddSingleton<IPaymentGateway>(gateway);
        });
        Guid companyId = Guid.NewGuid();
        Guid checkoutId = await harness.InScopeAsync(async services =>
        {
            VumaRetailDbContext context = services.GetRequiredService<VumaRetailDbContext>();
            CommerceBasket basket = CommerceBasket.Open(harness.TenantId, companyId, Guid.NewGuid(), "customer-authorize", DateTimeOffset.UtcNow);
            CheckoutIntent checkout = CheckoutIntent.Submit(harness.TenantId, companyId, basket.ChannelConnectionId, basket.Id,
                "customer-authorize", "checkout-key", "basket-fingerprint", DateTimeOffset.UtcNow);
            CommerceBasketLine line = CommerceBasketLine.Add(harness.TenantId, companyId, basket.Id,
                Guid.NewGuid(), 2m, 1m, 125m, "ZAR");
            context.CommerceBaskets.Add(basket);
            context.CheckoutIntents.Add(checkout);
            context.CommerceBasketLines.Add(line);
            await context.CommitAsync();
            return checkout.Id;
        });

        await harness.CreateUserAsync("payment-authorizer", "CorrectHorseBattery1", EcommercePermissions.Payment);
        using HttpClient client = await harness.SignInAsync("payment-authorizer");
        var request = new
        {
            CompanyId = companyId, OwnerKey = "customer-authorize",
            ReturnUrl = new Uri("https://merchant.example/return"),
            CancelUrl = new Uri("https://merchant.example/cancel"),
            NotificationUrl = new Uri("https://merchant.example/notify")
        };
        HttpResponseMessage first = await client.PostAsJsonAsync($"/api/v1/storefront/checkouts/{checkoutId:D}/payment", request);
        HttpResponseMessage replay = await client.PostAsJsonAsync($"/api/v1/storefront/checkouts/{checkoutId:D}/payment", request);
        string firstBody = await first.Content.ReadAsStringAsync();
        string replayBody = await replay.Content.ReadAsStringAsync();

        first.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, firstBody);
        replay.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, replayBody);
        gateway.AuthorizationCalls.Should().Be(1);
        int persisted = await harness.InScopeAsync(async services =>
            await services.GetRequiredService<VumaRetailDbContext>().PaymentAttempts
                .CountAsync(attempt => attempt.CheckoutIntentId == checkoutId && attempt.EventId == $"payment-authorization:{checkoutId:N}"));
        persisted.Should().Be(1);
    }

    [Fact]
    public async Task Confirmed_paid_checkout_creates_order_and_company_reservation_through_api()
    {
        (ApiHarness harness, OrdersScenario scenario, Guid companyId) = await OrdersHarnessSetup.CreateHarnessAsync(fixture);
        await using (harness)
        {
            Guid checkoutId = await harness.InScopeAsync(async services =>
            {
                DateTimeOffset now = harness.Clock.UtcNow;
                VumaRetailDbContext context = services.GetRequiredService<VumaRetailDbContext>();
                ChannelConnection channel = ChannelConnection.Register(harness.TenantId, companyId, "WEB", "checkout.example");
                channel.Activate();
                PublishedProduct product = PublishedProduct.Publish(harness.TenantId, companyId, channel.Id,
                    scenario.InStockItemId, null, "MILK-2L-WEB", "Full cream milk 2L", null, 59.99m, "ZAR", 50m, now, 1);
                CommerceBasket basket = CommerceBasket.Open(harness.TenantId, companyId, channel.Id, "checkout-owner", now);
                CommerceBasketLine line = CommerceBasketLine.Add(harness.TenantId, companyId, basket.Id,
                    product.Id, 2m, 1m, product.Price, product.Currency);
                CheckoutIntent checkout = CheckoutIntent.Submit(harness.TenantId, companyId, channel.Id, basket.Id,
                    "checkout-owner", "checkout-order-key", "checkout-order-fingerprint", now);
                checkout.Confirm(now.AddMinutes(1));
                PaymentAttempt payment = PaymentAttempt.Record(harness.TenantId, companyId, checkout.Id,
                    "payment-event-order", "payment-order-fingerprint", "tj-payment-order",
                    PaymentAttemptStatus.Captured, "TJ-CAPTURED", now);
                context.ChannelConnections.Add(channel);
                context.PublishedProducts.Add(product);
                context.CommerceBaskets.Add(basket);
                context.CommerceBasketLines.Add(line);
                context.CheckoutIntents.Add(checkout);
                context.PaymentAttempts.Add(payment);
                await context.CommitAsync();
                return checkout.Id;
            });

            await harness.CreateUserAsync("checkout-order-owner", "CorrectHorseBattery1", EcommercePermissions.Checkout);
            using HttpClient client = await harness.SignInAsync("checkout-order-owner");
            HttpResponseMessage response = await client.PostAsJsonAsync($"/api/v1/storefront/checkouts/{checkoutId:D}/order", new
            {
                CompanyId = companyId, OwnerKey = "checkout-owner", FulfillingLocationId = scenario.LocationId,
                FulfilmentType = OrderFulfilmentType.ClickAndCollect
            });
            string body = await response.Content.ReadAsStringAsync();
            response.StatusCode.Should().Be(System.Net.HttpStatusCode.Created, body);
            Guid orderId = (await response.Content.ReadFromJsonAsync<Guid>())!;

            SalesOrder order = await harness.InScopeAsync(async services =>
                await services.GetRequiredService<VumaRetailDbContext>().SalesOrders
                    .Include(value => value.Lines).SingleAsync(value => value.Id == orderId));
            order.Lines.Should().ContainSingle(line => line.ReservationId.HasValue && line.RequestedQuantity.Value == 2m);

            await harness.InScopeAsync(async services =>
            {
                services.GetRequiredService<ICompanyContext>().SetCompany(companyId);
                VumaRetailDbContext context = await services.GetRequiredService<ICompanyDbContextFactory>().CreateAsync();
                await using (context)
                {
                    StockReservation reservation = await context.StockReservations.SingleAsync(value =>
                        value.SourceDocumentId == orderId && value.State == ReservationState.Held);
                    reservation.Quantity.Value.Should().Be(2m);
                }
                return 0;
            });
        }
    }

    private sealed class StubPaymentGateway : IPaymentGateway
    {
        public int AuthorizationCalls { get; private set; }
        public int CaptureCalls { get; private set; }

        public Task<PaymentGatewayAuthorization> AuthorizeAsync(PaymentAuthorizationRequest request,
            CancellationToken cancellationToken = default)
        {
            AuthorizationCalls++;
            return Task.FromResult(new PaymentGatewayAuthorization("tj-payment-1", null, "Authorised", "TJ-AUTH-1"));
        }

        public Task<PaymentGatewayResult> CaptureAsync(PaymentGatewayOperation request,
            CancellationToken cancellationToken = default)
        {
            CaptureCalls++;
            return Task.FromResult(new PaymentGatewayResult(request.ProviderPaymentId, "Captured", "TJ-CAP-1"));
        }

        public Task<PaymentGatewayResult> VoidAsync(PaymentGatewayOperation request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new PaymentGatewayResult(request.ProviderPaymentId, "Reversed", "TJ-VOID-1"));

        public Task<PaymentGatewayResult> RefundAsync(PaymentGatewayOperation request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new PaymentGatewayResult(request.ProviderPaymentId, "Reversed", "TJ-REFUND-1"));
    }
}
