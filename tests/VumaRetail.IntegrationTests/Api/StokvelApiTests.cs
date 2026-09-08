using System.Net;
using System.Net.Http.Json;
using VumaRetail.Application.CustomerAccounts.Permissions;
using VumaRetail.Contracts.CustomerAccounts;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Api;

/// <summary>
/// The Stage 10b stokvel endpoints over HTTP against the real store server: anonymous callers
/// get 401, callers without the stokvel permission get 403 on every write route, and every route
/// is present in the OpenAPI document.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class StokvelApiTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Anonymous_callers_get_401()
    {
        await using var harness = await ApiHarness.CreateAsync(fixture).ConfigureAwait(false);

        var body = new CreateStokvelRequest(
            "Grocery", "GroceryHamper", "constitution",
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 15), harness.StoreId);
        HttpResponseMessage response = await harness.Client
            .PostAsJsonAsync("/api/v1/stokvels", body).ConfigureAwait(false);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Endpoints_answer_behind_their_permissions()
    {
        await using var harness = await ApiHarness.CreateAsync(fixture).ConfigureAwait(false);

        await harness.CreateUserAsync(
            "clerk1", "CorrectHorseBattery1",
            CustomerAccountsPermissions.AccountView).ConfigureAwait(false);
        HttpClient clerk = await harness.SignInAsync("clerk1").ConfigureAwait(false);

        Guid groupId = Guid.NewGuid();
        Guid memberId = Guid.NewGuid();
        Guid payoutId = Guid.NewGuid();

        (string Method, string Url, HttpContent? Body)[] writes =
        [
            ("POST", "/api/v1/stokvels",
                JsonContent.Create(new CreateStokvelRequest(
                    "Grocery", "GroceryHamper", "constitution",
                    new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 15), harness.StoreId))),
            ("POST", $"/api/v1/stokvels/{groupId}/members",
                JsonContent.Create(new AddMemberRequest(
                    Guid.NewGuid(), "Member", 500m, "ZAR"))),
            ("POST", $"/api/v1/stokvels/{groupId}/contributions",
                JsonContent.Create(new RecordContributionRequest(
                    memberId, 100m, "ZAR", "Till", "RCPT-1"))),
            ("POST", $"/api/v1/stokvels/{groupId}/allocate-benefits",
                JsonContent.Create(new AllocateBenefitsRequest(90m, "ZAR"))),
            ("POST", $"/api/v1/stokvels/{groupId}/payouts",
                JsonContent.Create(new RequestPayoutRequest(
                    memberId, "Cash", 100m, "ZAR"))),
            ("POST", $"/api/v1/stokvels/payouts/{payoutId}/approve", null),
            ("POST", $"/api/v1/stokvels/payouts/{payoutId}/settle", null),
            ("POST", $"/api/v1/stokvels/{groupId}/hampers",
                JsonContent.Create(new CreateHamperRequest(
                    "Hamper", 450m, "ZAR",
                    new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 24), "MAIN",
                    [new HamperLineRequest(Guid.NewGuid(), null, 2m, "EA")]))),
            ("POST", $"/api/v1/stokvels/{groupId}/members/{memberId}/remove", null),
        ];

        foreach ((string method, string url, HttpContent? body) in writes)
        {
            HttpResponseMessage response = method == "POST" && body is not null
                ? await clerk.PostAsync(url, body).ConfigureAwait(false)
                : await clerk.PostAsync(url, new StringContent(string.Empty)).ConfigureAwait(false);
            response.StatusCode.Should().Be(
                HttpStatusCode.Forbidden, $"write route {url} must sit behind {CustomerAccountsPermissions.StokvelManage}");
        }

        HttpResponseMessage read = await clerk.GetAsync(
            $"/api/v1/stokvels/{groupId}/group-statement?callerRole=Treasurer").ConfigureAwait(false);
        read.StatusCode.Should().Be(
            HttpStatusCode.Forbidden, $"read routes must sit behind {CustomerAccountsPermissions.StokvelView}");
    }

    [Fact]
    public async Task OpenAPI_lists_every_stokvel_route()
    {
        await using var harness = await ApiHarness.CreateAsync(fixture).ConfigureAwait(false);

        string document = await harness.Client.GetStringAsync("/openapi/v1.json").ConfigureAwait(false);

        string[] routes =
        [
            "/api/v1/stokvels",
            "/api/v1/stokvels/{groupId}/members",
            "/api/v1/stokvels/{groupId}/contributions",
            "/api/v1/stokvels/{groupId}/allocate-benefits",
            "/api/v1/stokvels/{groupId}/payouts",
            "/api/v1/stokvels/payouts/{payoutId}/approve",
            "/api/v1/stokvels/payouts/{payoutId}/settle",
            "/api/v1/stokvels/{groupId}/hampers",
            "/api/v1/stokvels/{groupId}/statement",
            "/api/v1/stokvels/{groupId}/group-statement",
            "/api/v1/stokvels/{groupId}/members/{memberId}/remove",
        ];
        foreach (string route in routes)
        {
            document.Should().Contain(route, $"OpenAPI must list {route}");
        }
    }
}
