using System.Net;
using System.Net.Http.Json;
using VumaRetail.Application.Crm.Permissions;
using VumaRetail.Contracts.Crm;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Api;

/// <summary>
/// The Stage 19 CRM endpoints over HTTP against the real store server: anonymous callers get
/// 401, callers without the permission get 403 on every write route, the happy paths work end
/// to end, and every route is present in the OpenAPI document.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CrmApiTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Anonymous_callers_get_401()
    {
        await using var harness = await ApiHarness.CreateAsync(fixture).ConfigureAwait(false);

        var body = new CreateLeadRequest(
            Guid.NewGuid(), "Athoi", "Molefe", "a@example.co.za", null, null, "Web");
        HttpResponseMessage response = await harness.Client
            .PostAsJsonAsync("/api/v1/crm/leads", body).ConfigureAwait(false);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Endpoints_answer_behind_their_permissions()
    {
        await using var harness = await ApiHarness.CreateAsync(fixture).ConfigureAwait(false);

        await harness.CreateUserAsync(
            "clerk1", "CorrectHorseBattery1",
            CrmPermissions.LeadView).ConfigureAwait(false);
        HttpClient clerk = await harness.SignInAsync("clerk1").ConfigureAwait(false);

        Guid leadId = Guid.NewGuid();
        Guid dealId = Guid.NewGuid();
        Guid segmentId = Guid.NewGuid();
        Guid companyId = Guid.NewGuid();

        (string Method, string Url, HttpContent? Body)[] writes =
        [
            ("POST", "/api/v1/crm/leads",
                JsonContent.Create(new CreateLeadRequest(
                    companyId, "Athoi", "Molefe", "a@example.co.za", null, null, "Web"))),
            ("PUT", $"/api/v1/crm/leads/{leadId}",
                JsonContent.Create(new UpdateLeadRequest("Athoi", "Molefe", null, null))),
            ("POST", $"/api/v1/crm/leads/{leadId}/assign",
                JsonContent.Create(new AssignLeadRequest(Guid.NewGuid()))),
            ("POST", $"/api/v1/crm/leads/{leadId}/disqualify",
                JsonContent.Create(new DisqualifyLeadRequest())),
            ("POST", $"/api/v1/crm/leads/{leadId}/convert",
                JsonContent.Create(new ConvertLeadRequest(Guid.NewGuid()))),
            ("POST", "/api/v1/crm/opportunities",
                JsonContent.Create(new CreateOpportunityRequest(
                    companyId, "Rollout", 25000m, "ZAR", 10))),
            ("POST", $"/api/v1/crm/opportunities/{dealId}/stage",
                JsonContent.Create(new MoveOpportunityStageRequest("Proposal", 40))),
            ("POST", $"/api/v1/crm/opportunities/{dealId}/win",
                JsonContent.Create(new WinOpportunityRequest(Guid.NewGuid()))),
            ("POST", $"/api/v1/crm/opportunities/{dealId}/lose",
                JsonContent.Create(new LoseOpportunityRequest("No budget"))),
            ("POST", "/api/v1/crm/activities",
                JsonContent.Create(new LogActivityRequest(
                    companyId, "Call", "Intro", "Went well"))),
            ("POST", "/api/v1/crm/segments",
                JsonContent.Create(new CreateSegmentRequest(companyId, "Gold", "Static"))),
            ("POST", $"/api/v1/crm/segments/{segmentId}/members",
                JsonContent.Create(new AddStaticMemberRequest("Customer", Guid.NewGuid()))),
            ("POST", $"/api/v1/crm/segments/{segmentId}/deactivate", null),
            ("POST", "/api/v1/crm/consents/give",
                JsonContent.Create(new GiveConsentRequest(
                    companyId, Guid.NewGuid(), "MarketingEmail", "signup-form"))),
            ("POST", "/api/v1/crm/consents/withdraw",
                JsonContent.Create(new WithdrawConsentRequest(
                    companyId, Guid.NewGuid(), "MarketingEmail"))),
        ];

        foreach ((string method, string url, HttpContent? body) in writes)
        {
            HttpResponseMessage response = body is not null
                ? method == "POST"
                    ? await clerk.PostAsync(url, body).ConfigureAwait(false)
                    : await clerk.PutAsync(url, body).ConfigureAwait(false)
                : await clerk.PostAsync(url, new StringContent(string.Empty)).ConfigureAwait(false);
            response.StatusCode.Should().Be(
                HttpStatusCode.Forbidden, $"write route {method} {url} must sit behind its manage permission");
        }

        HttpResponseMessage read = await clerk.GetAsync(
            $"/api/v1/crm/leads/{leadId}").ConfigureAwait(false);
        read.StatusCode.Should().Be(
            HttpStatusCode.NotFound, "a missing permission would be 403; 404 proves crm.lead.view passed");
    }

    [Fact]
    public async Task Lead_and_opportunity_happy_paths_work_end_to_end()
    {
        await using var harness = await ApiHarness.CreateAsync(fixture).ConfigureAwait(false);

        await harness.CreateUserAsync(
            "opener1", "CorrectHorseBattery1",
            CrmPermissions.LeadManage,
            CrmPermissions.LeadView,
            CrmPermissions.OpportunityManage,
            CrmPermissions.OpportunityView,
            CrmPermissions.ActivityLog,
            CrmPermissions.ActivityView,
            CrmPermissions.SegmentManage,
            CrmPermissions.SegmentView,
            CrmPermissions.ConsentManage,
            CrmPermissions.ConsentView,
            CrmPermissions.View360).ConfigureAwait(false);
        HttpClient opener = await harness.SignInAsync("opener1").ConfigureAwait(false);
        Guid companyId = Guid.NewGuid();

        HttpResponseMessage created = await opener.PostAsJsonAsync(
            "/api/v1/crm/leads",
            new CreateLeadRequest(
                companyId, "Athoi", "Molefe", "happy@example.co.za", null, null, "Web"))
            .ConfigureAwait(false);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var leadRef = (await created.Content
            .ReadFromJsonAsync<CrmIdResponse>().ConfigureAwait(false))!;

        HttpResponseMessage fetched = await opener.GetAsync(
            $"/api/v1/crm/leads/{leadRef.Id}").ConfigureAwait(false);
        fetched.StatusCode.Should().Be(HttpStatusCode.OK);
        var lead = (await fetched.Content
            .ReadFromJsonAsync<LeadResponse>().ConfigureAwait(false))!;
        lead.Email.Should().Be("happy@example.co.za");
        lead.Status.Should().Be("New");

        HttpResponseMessage deal = await opener.PostAsJsonAsync(
            "/api/v1/crm/opportunities",
            new CreateOpportunityRequest(companyId, "Rollout", 25000m, "ZAR", 10))
            .ConfigureAwait(false);
        deal.StatusCode.Should().Be(HttpStatusCode.Created);

        HttpResponseMessage activity = await opener.PostAsJsonAsync(
            "/api/v1/crm/activities",
            new LogActivityRequest(companyId, "Call", "Intro", "Went well"))
            .ConfigureAwait(false);
        activity.StatusCode.Should().Be(HttpStatusCode.Created);

        HttpResponseMessage segment = await opener.PostAsJsonAsync(
            "/api/v1/crm/segments",
            new CreateSegmentRequest(companyId, "Gold", "Static"))
            .ConfigureAwait(false);
        segment.StatusCode.Should().Be(HttpStatusCode.Created);

        Guid customerId = Guid.NewGuid();
        HttpResponseMessage consent = await opener.PostAsJsonAsync(
            "/api/v1/crm/consents/give",
            new GiveConsentRequest(companyId, customerId, "MarketingEmail", "signup-form"))
            .ConfigureAwait(false);
        consent.StatusCode.Should().Be(HttpStatusCode.NoContent);

        HttpResponseMessage view = await opener.GetAsync(
            $"/api/v1/crm/customers/{customerId}/360-view").ConfigureAwait(false);
        view.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task OpenAPI_lists_every_crm_route()
    {
        await using var harness = await ApiHarness.CreateAsync(fixture).ConfigureAwait(false);

        string document = await harness.Client.GetStringAsync("/openapi/v1.json").ConfigureAwait(false);

        string[] routes =
        [
            "/api/v1/crm/leads",
            "/api/v1/crm/leads/{leadId}",
            "/api/v1/crm/leads/{leadId}/assign",
            "/api/v1/crm/leads/{leadId}/disqualify",
            "/api/v1/crm/leads/{leadId}/convert",
            "/api/v1/crm/opportunities",
            "/api/v1/crm/opportunities/{opportunityId}",
            "/api/v1/crm/opportunities/{opportunityId}/stage",
            "/api/v1/crm/opportunities/{opportunityId}/win",
            "/api/v1/crm/opportunities/{opportunityId}/lose",
            "/api/v1/crm/activities",
            "/api/v1/crm/segments",
            "/api/v1/crm/segments/{segmentId}/members",
            "/api/v1/crm/segments/{segmentId}/deactivate",
            "/api/v1/crm/consents/give",
            "/api/v1/crm/consents/withdraw",
            "/api/v1/crm/consents",
            "/api/v1/crm/customers/{customerId}/360-view",
        ];
        foreach (string route in routes)
        {
            document.Should().Contain(route, $"OpenAPI must list {route}");
        }
    }
}
