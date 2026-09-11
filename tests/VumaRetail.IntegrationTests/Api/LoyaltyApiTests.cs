using System.Net;
using System.Net.Http.Json;
using VumaRetail.Application.Loyalty.Permissions;
using VumaRetail.IntegrationTests.Harness;
using static VumaRetail.PublicApi.Loyalty.LoyaltyPublicContracts;

namespace VumaRetail.IntegrationTests.Api;

/// <summary>
/// The Stage 20 public loyalty surface over HTTP against the real store server: anonymous
/// callers get 401, callers without the permission get 403 on every write route, the earn →
/// balance → redeem loop works end to end, and every route is in the OpenAPI document.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class LoyaltyApiTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Anonymous_callers_get_401()
    {
        await using var harness = await ApiHarness.CreateAsync(fixture).ConfigureAwait(false);

        var body = new EarnRequest(
            Guid.NewGuid(), 150m, "ZAR", "sale-1", Guid.NewGuid());
        HttpResponseMessage response = await harness.Client
            .PostAsJsonAsync($"/api/v1/loyalty/members/{Guid.NewGuid()}/earn", body)
            .ConfigureAwait(false);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Endpoints_answer_behind_their_permissions()
    {
        await using var harness = await ApiHarness.CreateAsync(fixture).ConfigureAwait(false);

        await harness.CreateUserAsync(
            "reader1", "CorrectHorseBattery1",
            LoyaltyPermissions.BalanceView).ConfigureAwait(false);
        HttpClient reader = await harness.SignInAsync("reader1").ConfigureAwait(false);

        Guid customerId = Guid.NewGuid();
        Guid companyId = Guid.NewGuid();
        Guid key = Guid.NewGuid();

        (string Url, HttpContent? Body)[] writes =
        [
            ($"/api/v1/loyalty/members/{customerId}/enroll",
                JsonContent.Create(new EnrollMemberRequest(companyId))),
            ($"/api/v1/loyalty/members/{customerId}/earn",
                JsonContent.Create(new EarnRequest(companyId, 150m, "ZAR", "sale-1", key))),
            ($"/api/v1/loyalty/members/{customerId}/redeem",
                JsonContent.Create(new RedeemRequest(companyId, 50m, "reward-1", key))),
            ("/api/v1/loyalty/catalogue/sync",
                JsonContent.Create(new SyncCatalogueRequest(companyId))),
            ("/api/v1/loyalty/settings",
                JsonContent.Create(new ConfigureLoyaltyRequest(companyId, "ZAR", 1m, 365, true))),
        ];

        foreach ((string url, HttpContent? body) in writes)
        {
            HttpResponseMessage response = await reader.PostAsync(url, body).ConfigureAwait(false);
            response.StatusCode.Should().Be(
                HttpStatusCode.Forbidden, $"write route {url} must sit behind its permission");
        }
    }

    [Fact]
    public async Task Configure_enroll_earn_balance_redeem_loop_works_end_to_end()
    {
        await using var harness = await ApiHarness.CreateAsync(fixture).ConfigureAwait(false);

        await harness.CreateUserAsync(
            "till1", "CorrectHorseBattery1",
            LoyaltyPermissions.Admin,
            LoyaltyPermissions.MemberEnroll,
            LoyaltyPermissions.MemberView,
            LoyaltyPermissions.Earn,
            LoyaltyPermissions.Redeem,
            LoyaltyPermissions.BalanceView,
            LoyaltyPermissions.CatalogueView).ConfigureAwait(false);
        HttpClient till = await harness.SignInAsync("till1").ConfigureAwait(false);

        Guid companyId = Guid.NewGuid();
        Guid customerId = Guid.NewGuid();

        HttpResponseMessage configured = await till.PostAsJsonAsync(
            "/api/v1/loyalty/settings",
            new ConfigureLoyaltyRequest(companyId, "ZAR", 1m, 365, true)).ConfigureAwait(false);
        configured.StatusCode.Should().Be(HttpStatusCode.NoContent);

        HttpResponseMessage enrolled = await till.PostAsJsonAsync(
            $"/api/v1/loyalty/members/{customerId}/enroll",
            new EnrollMemberRequest(companyId)).ConfigureAwait(false);
        enrolled.StatusCode.Should().Be(HttpStatusCode.Created);

        HttpResponseMessage earned = await till.PostAsJsonAsync(
            $"/api/v1/loyalty/members/{customerId}/earn",
            new EarnRequest(companyId, 150m, "ZAR", "sale-1", Guid.NewGuid())).ConfigureAwait(false);
        earned.StatusCode.Should().Be(HttpStatusCode.OK);
        var earn = (await earned.Content
            .ReadFromJsonAsync<EarnResponse>().ConfigureAwait(false))!;
        earn.Queued.Should().BeFalse();
        earn.Points.Should().Be(150m);

        HttpResponseMessage balance = await till.GetAsync(
            $"/api/v1/loyalty/members/{customerId}/balance?companyId={companyId}").ConfigureAwait(false);
        balance.StatusCode.Should().Be(HttpStatusCode.OK);
        var balanceBody = (await balance.Content
            .ReadFromJsonAsync<BalanceResponse>().ConfigureAwait(false))!;
        balanceBody.Balance.Should().Be(150m);

        HttpResponseMessage redeemed = await till.PostAsJsonAsync(
            $"/api/v1/loyalty/members/{customerId}/redeem",
            new RedeemRequest(companyId, 50m, "reward-1", Guid.NewGuid())).ConfigureAwait(false);
        redeemed.StatusCode.Should().Be(HttpStatusCode.OK);

        HttpResponseMessage history = await till.GetAsync(
            $"/api/v1/loyalty/members/{customerId}/transactions?companyId={companyId}").ConfigureAwait(false);
        history.StatusCode.Should().Be(HttpStatusCode.OK);
        var transactions = (await history.Content
            .ReadFromJsonAsync<IReadOnlyList<TransactionResponse>>().ConfigureAwait(false))!;
        transactions.Should().HaveCount(2);

        HttpResponseMessage synced = await till.PostAsJsonAsync(
            "/api/v1/loyalty/catalogue/sync",
            new SyncCatalogueRequest(companyId)).ConfigureAwait(false);
        synced.StatusCode.Should().Be(HttpStatusCode.OK);

        HttpResponseMessage tiers = await till.GetAsync(
            $"/api/v1/loyalty/tiers?companyId={companyId}").ConfigureAwait(false);
        tiers.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Unknown_members_answer_404_not_403()
    {
        await using var harness = await ApiHarness.CreateAsync(fixture).ConfigureAwait(false);

        await harness.CreateUserAsync(
            "reader2", "CorrectHorseBattery1",
            LoyaltyPermissions.BalanceView).ConfigureAwait(false);
        HttpClient reader = await harness.SignInAsync("reader2").ConfigureAwait(false);

        HttpResponseMessage balance = await reader.GetAsync(
            $"/api/v1/loyalty/members/{Guid.NewGuid()}/balance?companyId={Guid.NewGuid()}")
            .ConfigureAwait(false);

        balance.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Webhook_rejects_without_a_secret()
    {
        await using var harness = await ApiHarness.CreateAsync(fixture).ConfigureAwait(false);

        // Anonymous by design (HMAC, not a user credential); missing configuration must fail closed.
        HttpResponseMessage accepted = await harness.Client.PostAsJsonAsync(
            "/api/v1/loyalty/webhooks/notifications",
            new WebhookNotificationRequest(
                Guid.NewGuid(), "orbit-ghost", 10m, null, "balance.adjusted")).ConfigureAwait(false);

        accepted.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Burst_past_the_limit_answers_429_with_retry_after()
    {
        await using var harness = await ApiHarness.CreateAsync(fixture).ConfigureAwait(false);

        await harness.CreateUserAsync(
            "scanner1", "CorrectHorseBattery1",
            LoyaltyPermissions.BalanceView,
            LoyaltyPermissions.MemberEnroll,
            LoyaltyPermissions.Admin).ConfigureAwait(false);
        HttpClient scanner = await harness.SignInAsync("scanner1").ConfigureAwait(false);

        Guid companyId = Guid.NewGuid();
        Guid customerId = Guid.NewGuid();

        await scanner.PostAsJsonAsync(
            "/api/v1/loyalty/settings",
            new ConfigureLoyaltyRequest(companyId, "ZAR", 1m, 365, true)).ConfigureAwait(false);
        await scanner.PostAsJsonAsync(
            $"/api/v1/loyalty/members/{customerId}/enroll",
            new EnrollMemberRequest(companyId)).ConfigureAwait(false);

        int limited = 0;
        for (int index = 0; index < 110; index++)
        {
            HttpResponseMessage response = await scanner.GetAsync(
                $"/api/v1/loyalty/members/{customerId}/balance?companyId={companyId}")
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                limited++;
                response.Headers.RetryAfter.Should().NotBeNull(
                    "a 429 carries Retry-After (API_STANDARDS.md §4)");
            }
            else
            {
                response.StatusCode.Should().Be(HttpStatusCode.OK);
            }
        }

        limited.Should().BePositive("110 reads against a 100/min bucket must trip the limiter");
    }

    [Fact]
    public async Task OpenAPI_lists_every_loyalty_route()
    {
        await using var harness = await ApiHarness.CreateAsync(fixture).ConfigureAwait(false);

        string document = await harness.Client.GetStringAsync("/openapi/v1.json").ConfigureAwait(false);

        string[] routes =
        [
            "/api/v1/loyalty/members/{customerId}/enroll",
            "/api/v1/loyalty/members/{customerId}",
            "/api/v1/loyalty/members/{customerId}/earn",
            "/api/v1/loyalty/members/{customerId}/redeem",
            "/api/v1/loyalty/members/{customerId}/balance",
            "/api/v1/loyalty/members/{customerId}/transactions",
            "/api/v1/loyalty/members/{customerId}/tier",
            "/api/v1/loyalty/tiers",
            "/api/v1/loyalty/rewards",
            "/api/v1/loyalty/catalogue/sync",
            "/api/v1/loyalty/settings",
            "/api/v1/loyalty/webhooks/notifications",
        ];
        foreach (string route in routes)
        {
            document.Should().Contain(route, $"OpenAPI must list {route}");
        }
    }
}
