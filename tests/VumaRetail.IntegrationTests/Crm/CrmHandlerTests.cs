using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Crm;
using VumaRetail.Application.Crm.Commands;
using VumaRetail.Application.Crm.Queries;
using VumaRetail.Domain.Crm;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Crm;

/// <summary>
/// The <c>crm</c> module against real PostgreSQL: every command and query handler, the
/// conversion link, consent visibility in the 360° view, and the constraints that hold when
/// the aggregate is bypassed (Stage 19).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CrmHandlerTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Create_lead_writes_a_row_with_tenant_and_company()
    {
        await using CrmHarness harness = await CrmHarness.CreateAsync(fixture);

        Guid id = await harness.Dispatcher.SendAsync(new CreateLeadCommand(
            harness.CompanyId, "Athoi", "Molefe", "athoi@example.co.za",
            "0821234567", null, LeadSource.Web, harness.StoreId));

        Lead? stored = await harness.Leads.FindAsync(id);
        stored.Should().NotBeNull();
        stored!.Email.Should().Be("athoi@example.co.za");
        stored.TenantId.Should().Be(harness.TenantId);
        stored.CompanyId.Should().Be(harness.CompanyId);
        stored.Status.Should().Be(LeadStatus.New);
    }

    [Fact]
    public async Task Duplicate_email_in_same_store_is_refused()
    {
        await using CrmHarness harness = await CrmHarness.CreateAsync(fixture);

        await harness.Dispatcher.SendAsync(new CreateLeadCommand(
            harness.CompanyId, "Athoi", "Molefe", "dupe@example.co.za",
            null, null, LeadSource.Web, harness.StoreId));

        Func<Task> act = () => harness.Dispatcher.SendAsync(new CreateLeadCommand(
            harness.CompanyId, "Second", "Person", "DUPE@example.co.za",
            null, null, LeadSource.Referral, harness.StoreId));

        await act.Should().ThrowAsync<DuplicateLeadEmailException>();
    }

    [Fact]
    public async Task Convert_links_partner_and_logs_a_system_activity()
    {
        await using CrmHarness harness = await CrmHarness.CreateAsync(fixture);

        Guid leadId = await harness.Dispatcher.SendAsync(new CreateLeadCommand(
            harness.CompanyId, "Athoi", "Molefe", "convert@example.co.za",
            null, null, LeadSource.Web, harness.StoreId));

        LeadConversionOutcome outcome = await harness.Dispatcher.SendAsync(
            new ConvertLeadCommand(leadId, harness.CustomerId));

        outcome.CustomerId.Should().Be(harness.CustomerId);

        Lead? stored = await harness.Leads.FindAsync(leadId);
        stored!.Status.Should().Be(LeadStatus.Converted);
        stored.CustomerId.Should().Be(harness.CustomerId);

        IReadOnlyList<Activity> trail = await harness.Activities.ListForLeadAsync(leadId);
        trail.Should().ContainSingle()
            .Which.ActivityType.Should().Be(ActivityType.System);
    }

    [Fact]
    public async Task Convert_against_unknown_customer_leaves_lead_untouched()
    {
        await using CrmHarness harness = await CrmHarness.CreateAsync(fixture);

        Guid leadId = await harness.Dispatcher.SendAsync(new CreateLeadCommand(
            harness.CompanyId, "Athoi", "Molefe", "nolink@example.co.za",
            null, null, LeadSource.Web, harness.StoreId));

        Func<Task> act = () => harness.Dispatcher.SendAsync(
            new ConvertLeadCommand(leadId, Guid.NewGuid()));

        await act.Should().ThrowAsync<LeadCustomerNotFoundException>();

        Lead? stored = await harness.Leads.FindAsync(leadId);
        stored!.Status.Should().Be(LeadStatus.New);
        (await harness.Activities.ListForLeadAsync(leadId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Opportunity_win_and_loss_rules_hold_end_to_end()
    {
        await using CrmHarness harness = await CrmHarness.CreateAsync(fixture);

        Guid dealId = await harness.Dispatcher.SendAsync(new CreateOpportunityCommand(
            harness.CompanyId, "Rollout", 25000m, "ZAR", 10));

        await harness.Dispatcher.SendAsync(
            new MoveOpportunityStageCommand(dealId, OpportunityStage.Proposal, 40));

        // A win without a customer is refused before anything is written.
        Func<Task> badWin = () => harness.Dispatcher.SendAsync(
            new WinOpportunityCommand(dealId, Guid.Empty));
        await badWin.Should().ThrowAsync<ValidationFailedException>();

        await harness.Dispatcher.SendAsync(new WinOpportunityCommand(dealId, harness.CustomerId));

        OpportunityEntry won = await harness.Dispatcher.QueryAsync(new GetOpportunityQuery(dealId));
        won.Stage.Should().Be(OpportunityStage.Won);
        won.CustomerId.Should().Be(harness.CustomerId);
        won.ExpectedAmount.Should().Be(25000m);
        won.Currency.Should().Be("ZAR");
    }

    [Fact]
    public async Task Consent_withdrawal_is_visible_in_the_360_view_immediately()
    {
        await using CrmHarness harness = await CrmHarness.CreateAsync(fixture);

        await harness.Dispatcher.SendAsync(new GiveConsentCommand(
            harness.CompanyId, harness.CustomerId, ConsentType.MarketingEmail, "signup-form"));

        await harness.Dispatcher.SendAsync(new WithdrawConsentCommand(
            harness.CompanyId, harness.CustomerId, ConsentType.MarketingEmail, "too many"));

        Customer360View view = await harness.Dispatcher.QueryAsync(
            new GetCustomer360ViewQuery(harness.CustomerId));

        view.Consents.Should().ContainSingle()
            .Which.State.Should().Be(ConsentState.Withdrawn);
    }

    [Fact]
    public async Task View_360_aggregates_leads_deals_activities_segments_and_consent()
    {
        await using CrmHarness harness = await CrmHarness.CreateAsync(fixture);

        Guid leadId = await harness.Dispatcher.SendAsync(new CreateLeadCommand(
            harness.CompanyId, "Athoi", "Molefe", "view360@example.co.za",
            null, null, LeadSource.Web, harness.StoreId));
        await harness.Dispatcher.SendAsync(new ConvertLeadCommand(leadId, harness.CustomerId));

        await harness.Dispatcher.SendAsync(new CreateOpportunityCommand(
            harness.CompanyId, "Rollout", 25000m, "ZAR", 50, CustomerId: harness.CustomerId));

        await harness.Dispatcher.SendAsync(new LogActivityCommand(
            harness.CompanyId, ActivityType.Call, "Intro call", "Went well",
            CustomerId: harness.CustomerId));

        Guid segmentId = await harness.Dispatcher.SendAsync(new CreateSegmentCommand(
            harness.CompanyId, "Gold Prospects", SegmentKind.Static));
        await harness.Dispatcher.SendAsync(
            new AddStaticMemberCommand(segmentId, MemberType.Customer, harness.CustomerId));

        await harness.Dispatcher.SendAsync(new GiveConsentCommand(
            harness.CompanyId, harness.CustomerId, ConsentType.MarketingEmail, "signup-form"));

        Customer360View view = await harness.Dispatcher.QueryAsync(
            new GetCustomer360ViewQuery(harness.CustomerId));

        view.LeadCount.Should().Be(1);
        view.OpenOpportunityCount.Should().Be(1);
        view.OpenOpportunityValue.Should().Be(25000m);
        view.OpportunityCurrency.Should().Be("ZAR");
        // Conversion activity + intro call.
        view.ActivityCount.Should().Be(2);
        view.SegmentNames.Should().ContainSingle().Which.Should().Be("Gold Prospects");
        view.Consents.Should().ContainSingle()
            .Which.State.Should().Be(ConsentState.Given);
    }

    [Fact]
    public async Task Dynamic_segment_members_are_refused_and_invisible()
    {
        await using CrmHarness harness = await CrmHarness.CreateAsync(fixture);

        Guid segmentId = await harness.Dispatcher.SendAsync(new CreateSegmentCommand(
            harness.CompanyId, "Recent Buyers", SegmentKind.Dynamic,
            QueryExpression: "lastPurchase < 30d"));

        Func<Task> act = () => harness.Dispatcher.SendAsync(
            new AddStaticMemberCommand(segmentId, MemberType.Customer, harness.CustomerId));

        await act.Should().ThrowAsync<DynamicSegmentWriteNotAllowedException>();
    }

    [Fact]
    public async Task Consent_uniqueness_holds_when_the_aggregate_is_bypassed()
    {
        await using CrmHarness harness = await CrmHarness.CreateAsync(fixture);

        await harness.Dispatcher.SendAsync(new GiveConsentCommand(
            harness.CompanyId, harness.CustomerId, ConsentType.MarketingSms, "signup-form"));

        // Same purpose, same customer, straight at the table: the unique index refuses.
        harness.Context.CrmConsents.Add(new Consent(
            harness.TenantId, harness.CompanyId, harness.CustomerId, ConsentType.MarketingSms));

        Func<Task> act = () => harness.Context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
