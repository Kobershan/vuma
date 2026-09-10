using NSubstitute;
using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Crm;
using VumaRetail.Application.Crm.Commands;
using VumaRetail.Application.Crm.Queries;
using VumaRetail.Application.Partners;
using VumaRetail.Domain.Crm;
using VumaRetail.Domain.Partners;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Crm;

/// <summary>
/// CRM handlers, queries and services against stubbed repositories: every not-found path, every
/// state refusal, validators and the manifest (Stage 19).
/// </summary>
public sealed class CrmHandlerUnitTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Company = Guid.NewGuid();

    private sealed class Stubs
    {
        public ILeadRepository Leads = Substitute.For<ILeadRepository>();
        public IOpportunityRepository Opportunities = Substitute.For<IOpportunityRepository>();
        public IActivityRepository Activities = Substitute.For<IActivityRepository>();
        public ISegmentRepository Segments = Substitute.For<ISegmentRepository>();
        public ISegmentMemberRepository Members = Substitute.For<ISegmentMemberRepository>();
        public IConsentRepository Consents = Substitute.For<IConsentRepository>();
        public IPartnerRepository Partners = Substitute.For<IPartnerRepository>();
        public ITenantContext TenantContext = Substitute.For<ITenantContext>();
        public IClock Clock = Substitute.For<IClock>();
        public IPrincipalAccessor Principal = Substitute.For<IPrincipalAccessor>();

        public Stubs()
        {
            TenantContext.TenantId.Returns(Tenant);
            Clock.UtcNow.Returns(Now);
            Principal.Principal.Returns("user:operator");
        }

        public Lead RealLead(string email = "a@example.co.za") => new(
            Tenant, Company, "Athoi", "Molefe", email, null, null, LeadSource.Web);
    }

    [Fact]
    public async Task Update_missing_lead_is_not_found()
    {
        var stubs = new Stubs();
        var handler = new UpdateLeadCommandHandler(stubs.Leads);

        Func<Task> act = () => handler.HandleAsync(
            new UpdateLeadCommand(Guid.NewGuid(), "A", "B", null, null));

        await act.Should().ThrowAsync<LeadNotFoundException>();
    }

    [Fact]
    public async Task Update_applies_details()
    {
        var stubs = new Stubs();
        Lead lead = stubs.RealLead();
        stubs.Leads.FindAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(lead);
        var handler = new UpdateLeadCommandHandler(stubs.Leads);

        await handler.HandleAsync(new UpdateLeadCommand(lead.Id, "New", "Name", "082", "Co"));

        lead.FirstName.Should().Be("New");
        lead.Company.Should().Be("Co");
    }

    [Fact]
    public async Task Assign_sets_assignee()
    {
        var stubs = new Stubs();
        Lead lead = stubs.RealLead();
        stubs.Leads.FindAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(lead);
        var handler = new AssignLeadCommandHandler(stubs.Leads);
        var user = Guid.NewGuid();

        await handler.HandleAsync(new AssignLeadCommand(lead.Id, user));

        lead.AssignedTo.Should().Be(user);
    }

    [Fact]
    public async Task Disqualify_closes_the_pipeline()
    {
        var stubs = new Stubs();
        Lead lead = stubs.RealLead();
        stubs.Leads.FindAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(lead);
        var handler = new DisqualifyLeadCommandHandler(stubs.Leads);

        await handler.HandleAsync(new DisqualifyLeadCommand(lead.Id, Dead: true));

        lead.Status.Should().Be(LeadStatus.Dead);
    }

    [Fact]
    public async Task Convert_missing_lead_is_not_found()
    {
        var stubs = new Stubs();
        var handler = new ConvertLeadCommandHandler(
            stubs.Leads, stubs.Activities, stubs.Partners, stubs.Clock);

        Func<Task> act = () => handler.HandleAsync(
            new ConvertLeadCommand(Guid.NewGuid(), Guid.NewGuid()));

        await act.Should().ThrowAsync<LeadNotFoundException>();
    }

    [Fact]
    public async Task Lead_lifecycle_mark_contacted_then_qualify()
    {
        Lead lead = new Stubs().RealLead();
        lead.MarkContacted();
        lead.Status.Should().Be(LeadStatus.Contacted);
        lead.Qualify();
        lead.Status.Should().Be(LeadStatus.Qualified);
        lead.AssignTo(Guid.NewGuid());
        lead.AssignedTo.Should().NotBeNull();
    }

    [Fact]
    public async Task Get_missing_lead_is_not_found()
    {
        var stubs = new Stubs();
        var handler = new GetLeadQueryHandler(stubs.Leads);

        Func<Task> act = () => handler.HandleAsync(new GetLeadQuery(Guid.NewGuid()));

        await act.Should().ThrowAsync<LeadNotFoundException>();
    }

    [Fact]
    public async Task List_leads_pages_newest_first()
    {
        var stubs = new Stubs();
        Lead first = stubs.RealLead("a@example.co.za");
        Lead second = stubs.RealLead("b@example.co.za");
        stubs.Leads.ListPageAsync(
                Arg.Any<Guid?>(), Arg.Any<LeadStatus?>(), Arg.Any<Guid?>(), Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(([first, second], false));
        var handler = new ListLeadsQueryHandler(stubs.Leads);

        PageResult<LeadEntry> page = await handler.HandleAsync(
            new ListLeadsQuery(Company, null, 10, null));

        page.Items.Should().HaveCount(2);
        page.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task Opportunity_query_paths()
    {
        var stubs = new Stubs();
        var deal = new Opportunity(
            Tenant, Company, "Rollout", new Money(100m, "ZAR"), 10);
        stubs.Opportunities.FindAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(deal);
        stubs.Opportunities.ListPageAsync(
                Arg.Any<Guid?>(), Arg.Any<OpportunityStage?>(), Arg.Any<Guid?>(), Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(([deal], true));

        var get = new GetOpportunityQueryHandler(stubs.Opportunities);
        (await get.HandleAsync(new GetOpportunityQuery(deal.Id))).Title.Should().Be("Rollout");

        var list = new ListOpportunitiesQueryHandler(stubs.Opportunities);
        PageResult<OpportunityEntry> page = await list.HandleAsync(
            new ListOpportunitiesQuery(Company, null, 10, "not-a-guid"));
        page.HasMore.Should().BeTrue();

        var missing = new GetOpportunityQueryHandler(
            Substitute.For<IOpportunityRepository>());
        Func<Task> act = () => missing.HandleAsync(new GetOpportunityQuery(Guid.NewGuid()));
        await act.Should().ThrowAsync<OpportunityNotFoundException>();
    }

    [Fact]
    public async Task Activity_listing_selects_by_parent()
    {
        var stubs = new Stubs();
        var activity = new Activity(
            Tenant, Company, ActivityType.Call, "Hi", null, Now, leadId: Guid.NewGuid());
        stubs.Activities.ListForLeadAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns([activity]);
        stubs.Activities.ListForOpportunityAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns([]);
        stubs.Activities.ListForCustomerAsync(
                Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);
        var handler = new ListActivitiesQueryHandler(stubs.Activities);

        (await handler.HandleAsync(new ListActivitiesQuery(Guid.NewGuid(), null, null, null)))
            .Should().ContainSingle();
        (await handler.HandleAsync(new ListActivitiesQuery(null, Guid.NewGuid(), null, null)))
            .Should().BeEmpty();
        (await handler.HandleAsync(new ListActivitiesQuery(null, null, Guid.NewGuid(), 5)))
            .Should().BeEmpty();
        (await handler.HandleAsync(new ListActivitiesQuery(null, null, null, null)))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task Segment_queries_and_consent_state()
    {
        var stubs = new Stubs();
        var segment = new Segment(Tenant, Company, "Gold", SegmentKind.Static);
        stubs.Segments.ListAsync(Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns([segment]);
        stubs.Segments.FindAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(segment);
        stubs.Members.ListMembersAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns([]);
        stubs.Consents.ListForCustomerAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var segments = new ListSegmentsQueryHandler(stubs.Segments);
        (await segments.HandleAsync(new ListSegmentsQuery(Company)))
            .Should().ContainSingle();

        var members = new GetSegmentMembersQueryHandler(stubs.Segments, stubs.Members);
        (await members.HandleAsync(new GetSegmentMembersQuery(Guid.NewGuid())))
            .Should().BeEmpty();

        var missing = new GetSegmentMembersQueryHandler(
            Substitute.For<ISegmentRepository>(), stubs.Members);
        Func<Task> act = () => missing.HandleAsync(new GetSegmentMembersQuery(Guid.NewGuid()));
        await act.Should().ThrowAsync<SegmentNotFoundException>();

        var consents = new GetConsentStateQueryHandler(stubs.Consents);
        (await consents.HandleAsync(new GetConsentStateQuery(Guid.NewGuid())))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task Consent_service_states()
    {
        var stubs = new Stubs();
        var service = new ConsentService(stubs.Consents);

        (await service.GetStateAsync(Guid.NewGuid(), ConsentType.MarketingEmail))
            .Should().Be(ConsentState.NotAsked);
        (await service.IsValidAsync(Guid.NewGuid(), ConsentType.MarketingEmail, Now))
            .Should().BeFalse();

        var consent = new Consent(Tenant, Company, Guid.NewGuid(), ConsentType.MarketingEmail);
        consent.Give(Now, "form", "user:operator");
        stubs.Consents.FindAsync(Arg.Any<Guid>(), Arg.Any<ConsentType>(), Arg.Any<CancellationToken>())
            .Returns(consent);

        (await service.GetStateAsync(Guid.NewGuid(), ConsentType.MarketingEmail))
            .Should().Be(ConsentState.Given);
        (await service.IsValidAsync(Guid.NewGuid(), ConsentType.MarketingEmail, Now))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Segment_service_refusals()
    {
        var stubs = new Stubs();
        var service = new SegmentService(stubs.Segments, stubs.Members);

        (await service.IsMemberAsync(Guid.NewGuid(), MemberType.Customer, Guid.NewGuid()))
            .Should().BeFalse("unknown segments match nobody");

        var dynamic = new Segment(
            Tenant, Company, "Dyn", SegmentKind.Dynamic, queryExpression: "x");
        stubs.Segments.FindAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(dynamic);
        (await service.IsMemberAsync(Guid.NewGuid(), MemberType.Customer, Guid.NewGuid()))
            .Should().BeFalse("dynamic segments have no static rows");

        var dormant = new Segment(Tenant, Company, "Old", SegmentKind.Static);
        dormant.Deactivate();
        stubs.Segments.FindAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(dormant);
        (await service.IsMemberAsync(Guid.NewGuid(), MemberType.Customer, Guid.NewGuid()))
            .Should().BeFalse("inactive segments match nobody");
    }

    [Fact]
    public async Task View_query_delegates_to_the_service()
    {
        var stubs = new Stubs();
        var service = Substitute.For<ICustomer360ViewService>();
        var expected = new Customer360View(
            Guid.NewGuid(), 0, 0, 0m, "ZAR", 0, [], [], Now);
        service.GetViewAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(expected);
        var handler = new GetCustomer360ViewQueryHandler(service);

        (await handler.HandleAsync(new GetCustomer360ViewQuery(Guid.NewGuid())))
            .Should().Be(expected);
    }

    [Fact]
    public void Manifest_declares_crm()
    {
        var manifest = new CrmModuleManifest();
        manifest.Module.Should().Be("crm");
        manifest.LicenceFlag.Should().Be("crm");
        manifest.Description.Should().NotBeNullOrWhiteSpace();
        manifest.IsCore.Should().BeFalse();
    }

    [Fact]
    public void Validators_reject_garbage()
    {
        new CreateLeadCommandValidator()
            .Validate(new CreateLeadCommand(Guid.Empty, "", "", "not-an-email", null, null, LeadSource.Web))
            .IsValid.Should().BeFalse();
        new ConvertLeadCommandValidator()
            .Validate(new ConvertLeadCommand(Guid.Empty, Guid.Empty))
            .IsValid.Should().BeFalse();
        new GiveConsentCommandValidator()
            .Validate(new GiveConsentCommand(Guid.Empty, Guid.Empty, ConsentType.MarketingEmail, ""))
            .IsValid.Should().BeFalse();
    }
}
