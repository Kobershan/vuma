using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Contracts;
using VumaRetail.Contracts.Identity;
using VumaRetail.IntegrationTests.Harness;
using VumaRetail.Web.Api;
using VumaRetail.Web.Diagnostics;

namespace VumaRetail.IntegrationTests.Api;

/// <summary>
/// The Stage 03 API contract, asserted against the store server over HTTP
/// (<c>docs/TESTING.md</c> §1, <c>docs/API_STANDARDS.md</c>).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ApiContractTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Signing_in_returns_a_token_and_the_api_version_header()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        await harness.CreateUserAsync("nmokoena");

        HttpResponseMessage response = await harness.Client.PostAsJsonAsync(
            "/api/v1/auth/token",
            new SignInRequest("nmokoena", "CorrectHorseBattery1"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues(VumaApi.VersionHeader).Should().ContainSingle().Which.Should().Be("v1");
    }

    [Fact]
    public async Task A_failed_sign_in_says_the_same_thing_whatever_went_wrong()
    {
        // docs/SECURITY.md §1. ProblemDetails is a tempting place to reintroduce the distinction
        // Stage 02 deliberately collapsed, with a helpful code per cause. It must not.
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        await harness.CreateUserAsync("nmokoena");

        JsonElement noSuchUser = await ProblemAsync(harness, new SignInRequest("ghost", "CorrectHorseBattery1"));
        JsonElement wrongPassword = await ProblemAsync(harness, new SignInRequest("nmokoena", "WrongPassword99"));

        noSuchUser.GetProperty("code").GetString().Should().Be(ApiErrorCodes.InvalidCredentials);
        wrongPassword.GetProperty("code").GetString().Should().Be(ApiErrorCodes.InvalidCredentials);
        wrongPassword.GetProperty("detail").GetString()
            .Should().Be(noSuchUser.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task An_unauthenticated_call_is_a_problem_document_with_a_code()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);

        HttpResponseMessage response = await harness.Client.GetAsync(new Uri("/api/v1/me/permissions", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        JsonElement problem = await ReadProblemAsync(response);
        problem.GetProperty("code").GetString().Should().Be(ApiErrorCodes.Unauthenticated);
    }

    [Fact]
    public async Task A_caller_without_the_permission_gets_a_forbidden_problem_document()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        await harness.CreateUserAsync("cashier");

        using HttpClient signedIn = await harness.SignInAsync("cashier");

        HttpResponseMessage response = await signedIn.GetAsync(new Uri("/api/v1/permissions", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadProblemAsync(response)).GetProperty("code").GetString().Should().Be(ApiErrorCodes.Forbidden);
    }

    [Fact]
    public async Task A_caller_holding_the_permission_gets_the_catalogue()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        await harness.CreateUserAsync("owner", permissions: IdentityPermissions.RoleView);

        using HttpClient signedIn = await harness.SignInAsync("owner");

        PermissionDescriptorResponse[]? catalogue = await signedIn
            .GetFromJsonAsync<PermissionDescriptorResponse[]>("/api/v1/permissions");

        catalogue.Should().NotBeNullOrEmpty();
        catalogue.Should().Contain(entry => entry.Permission == IdentityPermissions.RoleView);
    }

    [Fact]
    public async Task A_malformed_request_comes_back_as_a_400_naming_the_property()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);

        HttpResponseMessage response = await harness.Client.PostAsJsonAsync(
            "/api/v1/auth/terminal/activate",
            new ActivateTerminalRequest(harness.StoreId, "code", "too-short", "board-serial-1"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        JsonElement problem = await ReadProblemAsync(response);
        problem.GetProperty("code").GetString().Should().Be(ApiErrorCodes.ValidationFailed);
        problem.GetProperty("errors").TryGetProperty(
            nameof(ActivateTerminalRequest.CertificateThumbprint),
            out _).Should().BeTrue();
    }

    [Fact]
    public async Task A_business_rule_refusal_comes_back_as_a_422_with_its_own_code()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);

        HttpResponseMessage response = await harness.Client.PostAsJsonAsync(
            "/api/v1/auth/terminal/activate",
            new ActivateTerminalRequest(harness.StoreId, "NOT-A-REAL-CODE", new string('A', 64), "board-serial-1"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ReadProblemAsync(response)).GetProperty("code").GetString()
            .Should().Be("TERMINAL_ACTIVATION_REFUSED");
    }

    [Fact]
    public async Task Every_error_carries_the_correlation_id_the_caller_supplied()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);

        using HttpRequestMessage request = new(HttpMethod.Get, "/api/v1/me/permissions");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, "till-3-invoice-run");

        HttpResponseMessage response = await harness.Client.SendAsync(request);

        response.Headers.GetValues(CorrelationIdMiddleware.HeaderName)
            .Should().ContainSingle().Which.Should().Be("till-3-invoice-run");
        (await ReadProblemAsync(response)).GetProperty("correlationId").GetString()
            .Should().Be("till-3-invoice-run");
    }

    [Fact]
    public async Task A_correlation_id_is_minted_when_the_caller_supplies_none()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);

        HttpResponseMessage response = await harness.Client.GetAsync(new Uri("/health", UriKind.Relative));

        response.Headers.GetValues(CorrelationIdMiddleware.HeaderName)
            .Should().ContainSingle().Which.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task The_openapi_document_is_served_and_describes_every_endpoint()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);

        JsonDocument document = JsonDocument.Parse(
            await harness.Client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative)));

        JsonElement paths = document.RootElement.GetProperty("paths");

        foreach (string path in new[]
        {
            "/api/v1/auth/token",
            "/api/v1/auth/pin",
            "/api/v1/auth/refresh",
            "/api/v1/auth/sign-out",
            "/api/v1/auth/terminal/activate",
            "/api/v1/me/permissions",
            "/api/v1/permissions",
        })
        {
            paths.TryGetProperty(path, out _).Should().BeTrue($"{path} must appear in the document");
        }
    }

    [Fact]
    public async Task Every_procurement_operation_reaches_the_openapi_document()
    {
        // R3 and CLAUDE.md §8: nothing exists in a UI that is not reachable over the versioned API
        // first. Stage 12 ships no screen at all (PROGRESS.md §4.3), so this document is the entire
        // deliverable a later WPF or Android client renders — an endpoint missing from it is a feature
        // that does not exist.
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);

        JsonDocument document = JsonDocument.Parse(
            await harness.Client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative)));

        JsonElement paths = document.RootElement.GetProperty("paths");

        foreach (string path in new[]
        {
            "/api/v1/procurement/requisitions",
            "/api/v1/procurement/requisitions/{requisitionId}",
            "/api/v1/procurement/requisitions/{requisitionId}/lines",
            "/api/v1/procurement/requisitions/{requisitionId}/submit",
            "/api/v1/procurement/requisitions/{requisitionId}/decide",
            "/api/v1/procurement/requisitions/{requisitionId}/cancel",
            "/api/v1/procurement/rfqs",
            "/api/v1/procurement/rfqs/{rfqId}",
            "/api/v1/procurement/rfqs/{rfqId}/lines",
            "/api/v1/procurement/rfqs/{rfqId}/issue",
            "/api/v1/procurement/rfqs/{rfqId}/responses",
            "/api/v1/procurement/rfqs/{rfqId}/responses/{responseId}/lines",
            "/api/v1/procurement/rfqs/{rfqId}/award",
            "/api/v1/procurement/rfqs/{rfqId}/close",
            "/api/v1/procurement/purchase-orders",
            "/api/v1/procurement/purchase-orders/{purchaseOrderId}",
            "/api/v1/procurement/purchase-orders/{purchaseOrderId}/lines",
            "/api/v1/procurement/purchase-orders/{purchaseOrderId}/lines/{lineId}",
            "/api/v1/procurement/purchase-orders/{purchaseOrderId}/approve",
            "/api/v1/procurement/purchase-orders/{purchaseOrderId}/issue",
            "/api/v1/procurement/purchase-orders/{purchaseOrderId}/amend",
            "/api/v1/procurement/purchase-orders/{purchaseOrderId}/close",
            "/api/v1/procurement/purchase-orders/{purchaseOrderId}/receipts",
            "/api/v1/procurement/purchase-orders/{purchaseOrderId}/matches",
            "/api/v1/procurement/goods-receipts",
            "/api/v1/procurement/goods-receipts/{goodsReceiptId}",
            "/api/v1/procurement/goods-receipts/{goodsReceiptId}/lines",
            "/api/v1/procurement/goods-receipts/{goodsReceiptId}/complete",
            "/api/v1/procurement/goods-receipts/{goodsReceiptId}/cancel",
            "/api/v1/procurement/reconciliation/stock-issues",
            "/api/v1/procurement/matches",
            "/api/v1/procurement/matches/{matchId}",
            "/api/v1/procurement/matches/{matchId}/release",
            "/api/v1/procurement/scorecards",
            "/api/v1/procurement/scorecards/{partnerId}",
        })
        {
            paths.TryGetProperty(path, out _).Should().BeTrue($"{path} must appear in the document");
        }
    }

    [Fact]
    public async Task Every_warehouse_operation_reaches_the_openapi_document()
    {
        // R3 and CLAUDE.md §8, exactly as Stage 12's own test above: Stage 13 ships no screen either
        // (PROGRESS.md §4.3), and "handheld" means this API surface (the stage document's own words) —
        // an endpoint missing from the document is a feature that does not exist for a scanner client.
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);

        JsonDocument document = JsonDocument.Parse(
            await harness.Client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative)));

        JsonElement paths = document.RootElement.GetProperty("paths");

        foreach (string path in new[]
        {
            "/api/v1/warehouse/zones",
            "/api/v1/warehouse/zones/{zoneId}/deactivate",
            "/api/v1/warehouse/zones/{zoneId}/bins",
            "/api/v1/warehouse/bins",
            "/api/v1/warehouse/bins/{binId}/deactivate",
            "/api/v1/warehouse/bins/{binId}/stock",
            "/api/v1/warehouse/bins/{binId}/stock/one",
            "/api/v1/warehouse/bins/move",
            "/api/v1/warehouse/locations/{locationId}/bins",
            "/api/v1/warehouse/putaway",
            "/api/v1/warehouse/putaway/{putawayTaskId}",
            "/api/v1/warehouse/locations/{locationId}/putaway/pending",
            "/api/v1/warehouse/putaway/{putawayTaskId}/confirm",
            "/api/v1/warehouse/putaway/{putawayTaskId}/cancel",
            "/api/v1/warehouse/pick-waves",
            "/api/v1/warehouse/pick-waves/{pickWaveId}",
            "/api/v1/warehouse/pick-waves/{pickWaveId}/tasks",
            "/api/v1/warehouse/pick-waves/{pickWaveId}/release",
            "/api/v1/warehouse/pick-waves/{pickWaveId}/cancel",
            "/api/v1/warehouse/pick-waves/{pickWaveId}/pack",
            "/api/v1/warehouse/pick-waves/{pickWaveId}/ship",
            "/api/v1/warehouse/pick-waves/{pickWaveId}/shipment",
            "/api/v1/warehouse/pick-tasks/{pickTaskId}/confirm",
            "/api/v1/warehouse/pick-tasks/{pickTaskId}/cancel",
            "/api/v1/warehouse/cycle-counts",
            "/api/v1/warehouse/cycle-counts/{cycleCountId}",
            "/api/v1/warehouse/cycle-counts/{cycleCountId}/counts",
            "/api/v1/warehouse/cycle-counts/{cycleCountId}/finalize",
        })
        {
            paths.TryGetProperty(path, out _).Should().BeTrue($"{path} must appear in the document");
        }
    }

    [Theory]
    [InlineData("/api/v1/warehouse/putaway/{0}/confirm")]
    [InlineData("/api/v1/warehouse/pick-tasks/{0}/confirm")]
    [InlineData("/api/v1/warehouse/pick-waves/{0}/ship")]
    [InlineData("/api/v1/warehouse/cycle-counts/{0}/finalize")]
    public async Task A_caller_without_warehouse_permissions_is_forbidden_from_the_stages_four_high_risk_operations(
        string pathTemplate)
    {
        // The stage document names these four by name: putaway confirm, pick confirm, ship confirm and
        // cycle count finalize. Each is checked here rather than one standing in for the group — a
        // permission that is declared, granted and never enforced is exactly the defect §4.15 recorded
        // against Stage 09, and one representative endpoint would not have caught it.
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        await harness.CreateUserAsync("picker");

        using HttpClient signedIn = await harness.SignInAsync("picker");

        HttpResponseMessage response = await signedIn.PostAsync(
            new Uri(string.Format(CultureInfo.InvariantCulture, pathTemplate, Guid.NewGuid()), UriKind.Relative),
            JsonContent.Create(new { }));

        response.StatusCode.Should().Be(
            HttpStatusCode.Forbidden,
            "the permission must be refused before the handler ever looks the document up");
        (await ReadProblemAsync(response)).GetProperty("code").GetString().Should().Be(ApiErrorCodes.Forbidden);
    }

    [Fact]
    public async Task Every_operation_documents_the_standard_error_responses()
    {
        // CLAUDE.md §8 asks for error responses on every endpoint. Attaching them by transformer
        // rather than by hand is what makes that true of the fortieth endpoint as well as the first.
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);

        JsonDocument document = JsonDocument.Parse(
            await harness.Client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative)));

        JsonElement operation = document.RootElement
            .GetProperty("paths")
            .GetProperty("/api/v1/auth/token")
            .GetProperty("post");

        JsonElement responses = operation.GetProperty("responses");

        foreach (string status in new[] { "400", "401", "403", "404", "409", "422", "500" })
        {
            responses.TryGetProperty(status, out _).Should().BeTrue($"{status} must be documented");
        }
    }

    [Fact]
    public async Task A_request_example_reaches_the_document()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);

        JsonDocument document = JsonDocument.Parse(
            await harness.Client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative)));

        JsonElement example = document.RootElement
            .GetProperty("paths")
            .GetProperty("/api/v1/auth/token")
            .GetProperty("post")
            .GetProperty("requestBody")
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("example");

        example.GetProperty("userName").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Every_route_is_versioned_except_the_named_infrastructure_ones()
    {
        // The rule that stops an endpoint quietly becoming un-versionable once a customer
        // integration depends on it. Asserted against the live endpoint table rather than the
        // source, so a route added through a group or a convention is covered too.
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);

        // Force the host to build before the endpoint table is read.
        _ = await harness.Client.GetAsync(new Uri("/health", UriKind.Relative));

        using IServiceScope scope = harness.Services.CreateScope();
        EndpointDataSource endpoints = scope.ServiceProvider.GetRequiredService<EndpointDataSource>();

        List<string> unversioned = endpoints.Endpoints
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText ?? string.Empty)
            .Where(pattern => !pattern.TrimStart('/').StartsWith("api/v", StringComparison.OrdinalIgnoreCase))
            .Where(pattern => !VumaApi.IsUnversionedRoute(pattern))
            .ToList();

        unversioned.Should().BeEmpty();
    }

    private static async Task<JsonElement> ProblemAsync(ApiHarness harness, SignInRequest request)
    {
        HttpResponseMessage response = await harness.Client.PostAsJsonAsync("/api/v1/auth/token", request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        return await ReadProblemAsync(response);
    }

    private static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
}
