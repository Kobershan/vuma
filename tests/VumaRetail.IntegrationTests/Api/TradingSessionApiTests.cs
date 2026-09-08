using System.Net;
using System.Net.Http.Json;
using VumaRetail.Application.Registry.Trading;
using VumaRetail.Contracts.TradingSessions;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Api;

/// <summary>
/// The Stage 09b trading-session endpoints over HTTP against the real store server: anonymous
/// callers get 401, callers without the basket permission get 403 on every write route, and
/// every route is present in the OpenAPI document.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TradingSessionApiTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Anonymous_callers_get_401()
    {
        await using var harness = await ApiHarness.CreateAsync(fixture).ConfigureAwait(false);

        var body = new OpenTradingSessionRequest(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "ZAR", $"TS-{Guid.NewGuid():N}");
        HttpResponseMessage response = await harness.Client
            .PostAsJsonAsync("/api/v1/trading-sessions", body).ConfigureAwait(false);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Endpoints_answer_behind_their_permissions()
    {
        await using var harness = await ApiHarness.CreateAsync(fixture).ConfigureAwait(false);

        await harness.CreateUserAsync(
            "clerk1", "CorrectHorseBattery1",
            "pos.sale.view").ConfigureAwait(false);
        HttpClient clerk = await harness.SignInAsync("clerk1").ConfigureAwait(false);

        Guid sessionId = Guid.NewGuid();
        Guid lineId = Guid.NewGuid();

        (string Method, string Url, HttpContent? Body)[] writes =
        [
            ("POST", "/api/v1/trading-sessions",
                JsonContent.Create(new OpenTradingSessionRequest(
                    Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "ZAR", $"TS-{Guid.NewGuid():N}"))),
            ("POST", $"/api/v1/trading-sessions/{sessionId}/lines",
                JsonContent.Create(new AddBasketLineRequest("BAR-1", 1m, "EA", 100m, "ZAR"))),
            ("POST", $"/api/v1/trading-sessions/{sessionId}/lines/{lineId}/void", null),
            ("POST", $"/api/v1/trading-sessions/{sessionId}/tender",
                JsonContent.Create(new CaptureTenderRequest("Card", 100m, "ZAR"))),
            ("PUT", $"/api/v1/trading-sessions/{sessionId}/tender/allocations",
                JsonContent.Create(new OverrideAllocationRequest(
                    [new AllocationOverrideRequest(Guid.NewGuid(), 100m, "ZAR")]))),
            ("POST", $"/api/v1/trading-sessions/{sessionId}/complete", null),
            ("POST", $"/api/v1/trading-sessions/{sessionId}/void",
                JsonContent.Create(new VoidSessionRequest("changed mind"))),
            ("POST", $"/api/v1/trading-sessions/{sessionId}/returns",
                JsonContent.Create(new ReturnBasketLinesRequest(
                    Guid.NewGuid(), "INV-1", "torn",
                    [new ReturnSessionLineRequest(Guid.NewGuid(), 1m)]))),
        ];

        foreach ((string method, string url, HttpContent? body) in writes)
        {
            HttpResponseMessage response = method switch
            {
                "POST" when body is not null => await clerk.PostAsync(url, body).ConfigureAwait(false),
                "POST" => await clerk.PostAsync(url, new StringContent(string.Empty)).ConfigureAwait(false),
                _ => await clerk.PutAsync(url, body!).ConfigureAwait(false),
            };
            response.StatusCode.Should().Be(
                HttpStatusCode.Forbidden, $"write route {method} {url} must sit behind a basket permission");
        }

        HttpResponseMessage read = await clerk.GetAsync(
            $"/api/v1/trading-sessions/{sessionId}").ConfigureAwait(false);
        read.StatusCode.Should().Be(
            HttpStatusCode.Forbidden, "read routes must sit behind the basket permission");
    }

    [Fact]
    public async Task OpenAPI_lists_every_trading_route()
    {
        await using var harness = await ApiHarness.CreateAsync(fixture).ConfigureAwait(false);

        string document = await harness.Client.GetStringAsync("/openapi/v1.json").ConfigureAwait(false);

        string[] routes =
        [
            "/api/v1/trading-sessions",
            "/api/v1/trading-sessions/{sessionId}/lines",
            "/api/v1/trading-sessions/{sessionId}/lines/{lineId}/void",
            "/api/v1/trading-sessions/{sessionId}",
            "/api/v1/trading-sessions/{sessionId}/tender",
            "/api/v1/trading-sessions/{sessionId}/tender/allocations",
            "/api/v1/trading-sessions/{sessionId}/complete",
            "/api/v1/trading-sessions/{sessionId}/void",
            "/api/v1/trading-sessions/{sessionId}/documents",
            "/api/v1/trading-sessions/{sessionId}/returns",
        ];
        foreach (string route in routes)
        {
            document.Should().Contain(route, $"OpenAPI must list {route}");
        }
    }
}
