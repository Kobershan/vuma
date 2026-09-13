using System.Text.Json;
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
        using JsonDocument document = JsonDocument.Parse(await harness.Client.GetStringAsync("/openapi/v1.json"));
        JsonElement paths = document.RootElement.GetProperty("paths");

        paths.TryGetProperty("/api/v1/storefront/products", out JsonElement route).Should().BeTrue();
        route.TryGetProperty("get", out _).Should().BeTrue();
    }
}
