using System.Text.Json;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Api;

/// <summary>Stage 22 marketing operator and callback route contract evidence.</summary>
[Collection(PostgresCollection.Name)]
public sealed class MarketingApiTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Marketing_openapi_contains_operator_and_signed_callback_routes()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        using JsonDocument document = JsonDocument.Parse(await harness.Client.GetStringAsync("/openapi/v1.json"));
        JsonElement paths = document.RootElement.GetProperty("paths");

        paths.TryGetProperty("/api/v1/marketing/campaigns/{id}", out JsonElement campaign).Should().BeTrue();
        campaign.TryGetProperty("get", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/marketing/messages/{id}", out JsonElement message).Should().BeTrue();
        message.TryGetProperty("get", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/marketing/webhooks/{provider}", out JsonElement webhook).Should().BeTrue();
        webhook.TryGetProperty("post", out _).Should().BeTrue();
    }
}
