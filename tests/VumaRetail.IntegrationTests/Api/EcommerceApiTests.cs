using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VumaRetail.Domain.Ecommerce;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.IntegrationTests.Harness;

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
        paths.TryGetProperty("/api/v1/storefront/checkouts/{id}/payment/{operation}", out JsonElement operations).Should().BeTrue();
        operations.TryGetProperty("post", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/storefront/webhooks/payments", out JsonElement webhooks).Should().BeTrue();
        webhooks.TryGetProperty("post", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/service/tickets", out JsonElement tickets).Should().BeTrue();
        tickets.TryGetProperty("post", out _).Should().BeTrue();
        tickets.TryGetProperty("get", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/service/tickets/{id}/close", out _).Should().BeTrue();
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
}
