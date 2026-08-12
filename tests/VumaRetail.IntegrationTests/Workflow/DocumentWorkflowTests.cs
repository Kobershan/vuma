using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using VumaRetail.Application.Abstractions.Workflow;
using VumaRetail.Contracts.Workflow;
using VumaRetail.IntegrationTests.Api;
using VumaRetail.IntegrationTests.Harness;
using VumaRetail.Workflow.Permissions;

namespace VumaRetail.IntegrationTests.Workflow;

/// <summary>
/// Document attach → new version → blob round-trip through <c>FileSystemDocumentBlobStore</c>, with a
/// matching SHA-256 (<c>docs/stages/STAGE-05-workflow.md</c> "Tests / acceptance").
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DocumentWorkflowTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Attaching_a_document_stores_the_bytes_and_the_matching_hash()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        await harness.CreateUserAsync(
            "clerk", permissions: [WorkflowPermissions.DocumentAttach, WorkflowPermissions.DocumentView]);
        HttpClient clerk = await harness.SignInAsync("clerk");

        byte[] content = Encoding.UTF8.GetBytes("Purchase order PO-1042, total ZAR 9,000.00");
        string expectedHash = Convert.ToHexStringLower(SHA256.HashData(content));

        HttpResponseMessage response = await clerk.PostAsJsonAsync(
            "/api/v1/workflow/documents",
            new AttachDocumentRequest(
                "procurement", "purchase-order", Guid.NewGuid(), "attachment", "PO-1042 signed",
                "text/plain", Convert.ToBase64String(content)));

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        DocumentVersionSummary? summary = await response.Content.ReadFromJsonAsync<DocumentVersionSummary>();
        summary.Should().NotBeNull();
        summary!.VersionNumber.Should().Be(1);
        summary.ContentHash.Should().Be(expectedHash);
        summary.SizeBytes.Should().Be(content.LongLength);

        // Round-tripped through the real FileSystemDocumentBlobStore, not asserted against the
        // handler's own return value.
        byte[] downloaded = await clerk.GetByteArrayAsync(
            $"/api/v1/workflow/documents/{summary.DocumentId}/versions/1/content");

        downloaded.Should().BeEquivalentTo(content);
        Convert.ToHexStringLower(SHA256.HashData(downloaded)).Should().Be(expectedHash);
    }

    [Fact]
    public async Task Attaching_a_new_version_never_overwrites_the_prior_one_and_advances_CurrentVersionNumber()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        await harness.CreateUserAsync("clerk", permissions: [WorkflowPermissions.DocumentAttach, WorkflowPermissions.DocumentView]);
        HttpClient clerk = await harness.SignInAsync("clerk");

        byte[] firstContent = Encoding.UTF8.GetBytes("draft one");
        HttpResponseMessage firstResponse = await clerk.PostAsJsonAsync(
            "/api/v1/workflow/documents",
            new AttachDocumentRequest(
                "procurement", "purchase-order", Guid.NewGuid(), "attachment", "Draft",
                "text/plain", Convert.ToBase64String(firstContent)));

        DocumentVersionSummary first = (await firstResponse.Content.ReadFromJsonAsync<DocumentVersionSummary>())!;

        byte[] secondContent = Encoding.UTF8.GetBytes("final, signed version");
        HttpResponseMessage secondResponse = await clerk.PostAsJsonAsync(
            $"/api/v1/workflow/documents/{first.DocumentId}/versions",
            new AttachDocumentVersionRequest("text/plain", Convert.ToBase64String(secondContent)));

        secondResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        DocumentVersionSummary second = (await secondResponse.Content.ReadFromJsonAsync<DocumentVersionSummary>())!;

        second.VersionNumber.Should().Be(2);
        second.ContentHash.Should().NotBe(first.ContentHash);

        // The document now points at version 2 as current...
        DocumentSummary? document = await clerk.GetFromJsonAsync<DocumentSummary>(
            $"/api/v1/workflow/documents/{first.DocumentId}");
        document!.CurrentVersionNumber.Should().Be(2);

        // ...but version 1's bytes are still retrievable, untouched.
        byte[] firstStillThere = await clerk.GetByteArrayAsync(
            $"/api/v1/workflow/documents/{first.DocumentId}/versions/1/content");
        firstStillThere.Should().BeEquivalentTo(firstContent);

        byte[] secondBytes = await clerk.GetByteArrayAsync(
            $"/api/v1/workflow/documents/{first.DocumentId}/versions/2/content");
        secondBytes.Should().BeEquivalentTo(secondContent);
    }

    [Fact]
    public async Task Attaching_a_document_without_the_permission_is_forbidden()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        await harness.CreateUserAsync("cashier");
        HttpClient cashier = await harness.SignInAsync("cashier");

        HttpResponseMessage response = await cashier.PostAsJsonAsync(
            "/api/v1/workflow/documents",
            new AttachDocumentRequest(
                "procurement", "purchase-order", Guid.NewGuid(), "attachment", "PO",
                "text/plain", Convert.ToBase64String("x"u8.ToArray())));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Listing_documents_for_an_entity_returns_only_that_entitys_documents()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        await harness.CreateUserAsync("clerk", permissions: [WorkflowPermissions.DocumentAttach, WorkflowPermissions.DocumentView]);
        HttpClient clerk = await harness.SignInAsync("clerk");

        Guid entityOne = Guid.NewGuid();
        Guid entityTwo = Guid.NewGuid();

        await clerk.PostAsJsonAsync("/api/v1/workflow/documents", new AttachDocumentRequest(
            "procurement", "purchase-order", entityOne, "attachment", "For one",
            "text/plain", Convert.ToBase64String("a"u8.ToArray())));

        await clerk.PostAsJsonAsync("/api/v1/workflow/documents", new AttachDocumentRequest(
            "procurement", "purchase-order", entityTwo, "attachment", "For two",
            "text/plain", Convert.ToBase64String("b"u8.ToArray())));

        HttpResponseMessage listResponse = await clerk.GetAsync(
            $"/api/v1/workflow/documents?module=procurement&entityType=purchase-order&entityId={entityOne}");

        IReadOnlyList<DocumentSummary>? documents = await listResponse.Content
            .ReadFromJsonAsync<IReadOnlyList<DocumentSummary>>();

        documents.Should().HaveCount(1);
        documents![0].Title.Should().Be("For one");
    }

    [Fact]
    public async Task Generating_a_document_stores_the_rendered_bytes_through_the_passthrough_renderer()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        await harness.CreateUserAsync(
            "clerk", permissions: [WorkflowPermissions.DocumentAttach, WorkflowPermissions.DocumentView]);
        HttpClient clerk = await harness.SignInAsync("clerk");

        Guid entityId = Guid.NewGuid();

        DocumentVersionSummary result = await harness.SendAsync(new VumaRetail.Workflow.Documents.GenerateDocumentCommand(
            "procurement",
            "purchase-order",
            entityId,
            "generated-report",
            "PO summary",
            "purchase-order-summary",
            new Dictionary<string, string> { ["orderNumber"] = "PO-1042", ["total"] = "9000.00" },
            "text/plain"));

        result.VersionNumber.Should().Be(1);

        byte[] downloaded = await clerk.GetByteArrayAsync(
            $"/api/v1/workflow/documents/{result.DocumentId}/versions/1/content");
        string text = Encoding.UTF8.GetString(downloaded);

        text.Should().Contain("purchase-order-summary").And.Contain("PO-1042");
        Convert.ToHexStringLower(SHA256.HashData(downloaded)).Should().Be(result.ContentHash);
    }
}
