using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using VumaRetail.Application.Quality;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Api;

/// <summary>Stage 18 quality route and permission-boundary evidence.</summary>
[Collection(PostgresCollection.Name)]
public sealed class QualityApiTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Quality_openapi_contains_plans_certificates_and_recalls()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        using JsonDocument document = JsonDocument.Parse(await harness.Client.GetStringAsync("/openapi/v1.json"));
        JsonElement paths = document.RootElement.GetProperty("paths");

        paths.TryGetProperty("/api/v1/quality/inspection-plans", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/quality/inspection-plans/{id}/publish", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/quality/certificates", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/quality/certificates/{id}/revoke", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/quality/recalls", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/quality/recalls/{id}/trace", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/quality/recalls/{id}/close", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Quality_viewer_cannot_mutate_plans_certificates_or_recalls()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        await harness.CreateUserAsync("quality-viewer", "CorrectHorseBattery1", QualityPermissions.View);
        using HttpClient client = await harness.SignInAsync("quality-viewer");

        HttpResponseMessage plan = await client.PostAsJsonAsync("/api/v1/quality/inspection-plans", new
        {
            CompanyId = Guid.NewGuid(), ItemId = Guid.NewGuid(), ItemVariantId = (Guid?)null,
            Version = 1, Name = "Incoming", SampleSize = 1, AcceptanceCriteria = "Pass"
        });
        HttpResponseMessage certificate = await client.PostAsJsonAsync("/api/v1/quality/certificates", new
        {
            CompanyId = Guid.NewGuid(), ItemId = Guid.NewGuid(), ItemVariantId = (Guid?)null,
            CertificateNumber = "CERT-DENIED", Issuer = "Lab", IssuedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1), Evidence = "hash"
        });
        HttpResponseMessage recall = await client.PostAsJsonAsync("/api/v1/quality/recalls", new
        {
            OperationId = Guid.NewGuid(), CompanyId = Guid.NewGuid(), CaseNumber = "REC-DENIED",
            LotReference = "LOT-1", Reason = "test"
        });

        plan.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        certificate.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        recall.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
