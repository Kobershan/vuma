using System.Text.Json;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Api;

/// <summary>Stage 23 service read/write route contract evidence.</summary>
[Collection(PostgresCollection.Name)]
public sealed class ServiceApiContractTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Service_openapi_contains_ticket_warranty_repair_part_sla_and_custody_routes()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        using JsonDocument document = JsonDocument.Parse(await harness.Client.GetStringAsync("/openapi/v1.json"));
        JsonElement paths = document.RootElement.GetProperty("paths");

        paths.TryGetProperty("/api/v1/service/tickets", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/service/warranties", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/service/repairs", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/service/parts", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/service/slas", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/service/custody", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/service/custody/export.csv", out _).Should().BeTrue();
    }
}
