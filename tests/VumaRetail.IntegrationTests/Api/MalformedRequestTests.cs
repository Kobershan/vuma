using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using VumaRetail.Contracts;
using VumaRetail.Finance.Permissions;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Api;

/// <summary>
/// A request that never reaches a handler is the caller's to fix, and must say so
/// (<c>docs/API_STANDARDS.md</c>, <c>docs/PROGRESS.md</c> §4.6).
/// </summary>
/// <remarks>
/// Minimal APIs raise <c>BadHttpRequestException</c> when model binding fails. Nothing mapped it, so
/// every one of these cases returned <c>500 INTERNAL_ERROR</c> — the server telling the caller it had
/// broken, when the caller had sent an incomplete request they could have corrected. It also logged
/// an error per occurrence, which makes one client's bad integration read as an outage.
///
/// These are asserted over real HTTP rather than against the handler, because the exception is raised
/// by the framework's binding layer: a unit test would have to construct the exception itself and
/// would prove only that the <c>switch</c> arm exists, not that anything reaches it.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class MalformedRequestTests(PostgresFixture fixture)
{
    [Fact]
    public async Task A_missing_required_query_parameter_is_a_400_the_caller_can_act_on()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        await harness.CreateUserAsync("nmokoena", permissions: FinancePermissions.TaxView);
        HttpClient client = await harness.SignInAsync("nmokoena");

        // taxCode is required and absent. amount and currency are supplied so that the missing one is
        // unambiguous.
        HttpResponseMessage response = await client.GetAsync(
            new Uri("/api/v1/finance/tax/calculate?amount=115&currency=ZAR", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be(ApiErrorCodes.MalformedRequest);
        problem.GetProperty("status").GetInt32().Should().Be(400);

        // The detail has to name what was wrong, or a 400 is no more actionable than the 500 was.
        problem.GetProperty("detail").GetString().Should().Contain("taxCode");
    }

    [Fact]
    public async Task An_unparseable_query_parameter_is_a_400_rather_than_a_500()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        await harness.CreateUserAsync("nmokoena", permissions: FinancePermissions.TaxView);
        HttpClient client = await harness.SignInAsync("nmokoena");

        HttpResponseMessage response = await client.GetAsync(
            new Uri("/api/v1/finance/tax/calculate?taxCode=STANDARD&amount=not-a-number&currency=ZAR", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().Should().Be(ApiErrorCodes.MalformedRequest);
    }

    [Fact]
    public async Task An_unreadable_body_is_a_400_and_discloses_nothing_from_inside_it()
    {
        // The disclosure half matters as much as the status. A JsonException carries a path into the
        // payload and often a fragment of it; echoing that into a response body sends the caller's
        // own data back out through an error channel and into the log.
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        await harness.CreateUserAsync("nmokoena", permissions: FinancePermissions.LedgerConfigure);
        HttpClient client = await harness.SignInAsync("nmokoena");

        const string secret = "SUPER-SECRET-ACCOUNT-CODE";

        // Valid JSON up to the point it is truncated mid-value, so the parser fails with the secret
        // already inside the buffer it is describing.
        string malformedJson = "{\"code\": \"" + secret + "\", \"name\": \"unterminated";
        StringContent body = new(malformedJson, Encoding.UTF8, "application/json");

        HttpResponseMessage response = await client.PostAsync(
            new Uri("/api/v1/finance/accounts", UriKind.Relative), body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        string raw = await response.Content.ReadAsStringAsync();
        raw.Should().NotContain(secret);

        JsonElement problem = JsonSerializer.Deserialize<JsonElement>(raw);
        problem.GetProperty("code").GetString().Should().Be(ApiErrorCodes.MalformedRequest);
    }

    [Fact]
    public async Task A_malformed_request_still_carries_the_correlation_id()
    {
        // Every error carries one (CONVENTIONS.md §5). The 500 path set it; the new 400 path has to
        // as well, or a support call about a 400 has nothing to quote.
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        await harness.CreateUserAsync("nmokoena", permissions: FinancePermissions.TaxView);
        HttpClient client = await harness.SignInAsync("nmokoena");

        HttpResponseMessage response = await client.GetAsync(
            new Uri("/api/v1/finance/tax/calculate?amount=115&currency=ZAR", UriKind.Relative));

        // The status assertion is not incidental here. Without it this test passes against the old
        // 500 path too — that one carried a correlation id as well — so it would have looked like
        // coverage while proving nothing about the fix.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.TryGetProperty("correlationId", out JsonElement correlationId).Should().BeTrue();
        correlationId.GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_well_formed_request_that_a_validator_rejects_is_still_a_validation_failure()
    {
        // The two must stay distinguishable. VALIDATION_FAILED means the request bound and a rule
        // rejected a value, so there is an errors extension naming the properties; MALFORMED_REQUEST
        // means binding never got that far. Collapsing them would cost a client the ability to tell
        // "I sent the wrong shape" from "I sent the wrong value".
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        await harness.CreateUserAsync("nmokoena", permissions: FinancePermissions.LedgerConfigure);
        HttpClient client = await harness.SignInAsync("nmokoena");

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/finance/accounts",
            new { code = "", name = "", type = "Asset", currency = "ZAR", controlAccountType = "None" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be(ApiErrorCodes.ValidationFailed);
        problem.TryGetProperty("errors", out _).Should().BeTrue();
    }
}
