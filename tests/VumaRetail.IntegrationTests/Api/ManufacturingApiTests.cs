using System.Net;
using System.Net.Http.Json;
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
}
