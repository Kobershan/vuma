using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using VumaRetail.Application.Manufacturing;
using VumaRetail.Contracts.Manufacturing;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Api;

/// <summary>Stage 16 BOM API behavior and permission-boundary tests.</summary>
[Collection(PostgresCollection.Name)]
public sealed class ManufacturingApiTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Authorized_user_can_create_read_and_publish_a_bom()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        await harness.CreateUserAsync("bom-manager", "CorrectHorseBattery1", ManufacturingPermissions.Manage, ManufacturingPermissions.View);
        using HttpClient client = await harness.SignInAsync("bom-manager");

        Guid finishedItemId = Guid.NewGuid();
        CreateBillOfMaterialsRequest request = new(
            Guid.NewGuid(),
            finishedItemId,
            null,
            1,
            "Starter kit",
            [new BillOfMaterialsLineRequest(Guid.NewGuid(), null, 2m, "EA", 5m)]);

        HttpResponseMessage created = await client.PostAsJsonAsync("/api/v1/manufacturing/boms/", request);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        BillOfMaterialsIdResponse response = (await created.Content.ReadFromJsonAsync<BillOfMaterialsIdResponse>())!;

        BillOfMaterialsResponse draft = await client.GetFromJsonAsync<BillOfMaterialsResponse>(
            $"/api/v1/manufacturing/boms/{response.Id:D}") ?? throw new InvalidOperationException("Missing BOM response.");
        draft.Status.Should().Be("Draft");
        draft.Lines.Should().ContainSingle();

        HttpResponseMessage published = await client.PostAsync(
            $"/api/v1/manufacturing/boms/{response.Id:D}/publish", content: null);
        published.StatusCode.Should().Be(HttpStatusCode.NoContent);

        BillOfMaterialsResponse result = await client.GetFromJsonAsync<BillOfMaterialsResponse>(
            $"/api/v1/manufacturing/boms/{response.Id:D}") ?? throw new InvalidOperationException("Missing published BOM response.");
        result.Status.Should().Be("Published");
    }

    [Fact]
    public async Task User_without_bom_permission_is_forbidden()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        await harness.CreateUserAsync("bom-reader", "CorrectHorseBattery1", ManufacturingPermissions.View);
        using HttpClient client = await harness.SignInAsync("bom-reader");

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/manufacturing/boms/",
            new CreateBillOfMaterialsRequest(
                Guid.NewGuid(), Guid.NewGuid(), null, 1, "Denied", [new BillOfMaterialsLineRequest(Guid.NewGuid(), null, 1m, "EA")]));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Missing_bom_is_reported_as_a_domain_error()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        await harness.CreateUserAsync("bom-viewer", "CorrectHorseBattery1", ManufacturingPermissions.View);
        using HttpClient client = await harness.SignInAsync("bom-viewer");

        HttpResponseMessage response = await client.GetAsync($"/api/v1/manufacturing/boms/{Guid.NewGuid():D}");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Openapi_describes_the_BOM_routes()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);

        using JsonDocument document = JsonDocument.Parse(
            await harness.Client.GetStringAsync("/openapi/v1.json"));
        JsonElement paths = document.RootElement.GetProperty("paths");

        paths.TryGetProperty("/api/v1/manufacturing/boms", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/manufacturing/boms/{id}", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/manufacturing/boms/{id}/publish", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Openapi_describes_the_production_execution_routes()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);

        using JsonDocument document = JsonDocument.Parse(await harness.Client.GetStringAsync("/openapi/v1.json"));
        JsonElement paths = document.RootElement.GetProperty("paths");

        paths.TryGetProperty("/api/v1/manufacturing/production-orders", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/manufacturing/production-orders/{id}", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/manufacturing/production-orders/{id}/capacity", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/manufacturing/production-orders/{id}/release", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/manufacturing/production-orders/{id}/issues", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/manufacturing/production-orders/{id}/receipts", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/manufacturing/production-orders/{id}/scrap", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/manufacturing/production-orders/{id}/close", out _).Should().BeTrue();
    }
}
