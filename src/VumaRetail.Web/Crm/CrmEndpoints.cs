using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Crm;
using VumaRetail.Application.Crm.Commands;
using VumaRetail.Application.Crm.Permissions;
using VumaRetail.Application.Crm.Queries;
using VumaRetail.Contracts;
using VumaRetail.Contracts.Crm;
using VumaRetail.Domain.Crm;
using VumaRetail.Web.Api;
using VumaRetail.Web.Licensing;

namespace VumaRetail.Web.Crm;

/// <summary>
/// The <c>crm</c> module's endpoints: leads, opportunities, activities, segments, consent and
/// the 360° customer view (Stage 19).
/// </summary>
/// <remarks>
/// R3: nothing exists in a UI before it exists here. Every write goes through a command
/// handler; conversion links to an existing Stage 06 partner and never creates identity.
/// </remarks>
public static class CrmEndpoints
{
    /// <summary>Maps the CRM endpoints under the current API version.</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapVumaCrm(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder api = endpoints.MapVumaApi();

        RouteGroupBuilder crm = api.MapGroup("/crm").WithTags("Crm").RequireModule("crm");

        crm.MapPost("/leads", CreateLeadAsync)
            .RequirePermission(CrmPermissions.LeadManage)
            .Produces<CrmIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Captures a lead. A duplicate email in the same store is refused.");

        crm.MapPut("/leads/{leadId:guid}", UpdateLeadAsync)
            .RequirePermission(CrmPermissions.LeadManage)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Updates a lead's captured details. Terminal leads refuse.");

        crm.MapPost("/leads/{leadId:guid}/assign", AssignLeadAsync)
            .RequirePermission(CrmPermissions.LeadManage)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Assigns a lead to a user.");

        crm.MapPost("/leads/{leadId:guid}/disqualify", DisqualifyLeadAsync)
            .RequirePermission(CrmPermissions.LeadManage)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Qualifies a lead out. Terminal — a disqualified lead never re-opens.");

        crm.MapPost("/leads/{leadId:guid}/convert", ConvertLeadAsync)
            .RequirePermission(CrmPermissions.LeadManage)
            .Produces<LeadConversionResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Converts a lead into an existing partner, one way, with a system activity.")
            .WithDescription(
                "The partner must already exist in this company's database; the lead is untouched "
                + "when it does not. Replaying a conversion returns the existing link.");

        crm.MapGet("/leads/{leadId:guid}", GetLeadAsync)
            .RequirePermission(CrmPermissions.LeadView)
            .Produces<LeadResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Reads one lead by id.");

        crm.MapGet("/leads", ListLeadsAsync)
            .RequirePermission(CrmPermissions.LeadView)
            .Produces<PageResponse<LeadResponse>>()
            .WithSummary("Pages leads, newest first.");

        crm.MapPost("/opportunities", CreateOpportunityAsync)
            .RequirePermission(CrmPermissions.OpportunityManage)
            .Produces<CrmIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Opens an opportunity, against a lead or directly against a customer.");

        crm.MapPost("/opportunities/{opportunityId:guid}/stage", MoveOpportunityStageAsync)
            .RequirePermission(CrmPermissions.OpportunityManage)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Moves a deal to a new open stage. Closed deals refuse.");

        crm.MapPost("/opportunities/{opportunityId:guid}/win", WinOpportunityAsync)
            .RequirePermission(CrmPermissions.OpportunityManage)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Wins a deal against a customer. A win without a customer is refused.");

        crm.MapPost("/opportunities/{opportunityId:guid}/lose", LoseOpportunityAsync)
            .RequirePermission(CrmPermissions.OpportunityManage)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Loses a deal with a recorded reason.");

        crm.MapGet("/opportunities/{opportunityId:guid}", GetOpportunityAsync)
            .RequirePermission(CrmPermissions.OpportunityView)
            .Produces<OpportunityResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Reads one opportunity by id.");

        crm.MapGet("/opportunities", ListOpportunitiesAsync)
            .RequirePermission(CrmPermissions.OpportunityView)
            .Produces<PageResponse<OpportunityResponse>>()
            .WithSummary("Pages opportunities, newest first.");

        crm.MapPost("/activities", LogActivityAsync)
            .RequirePermission(CrmPermissions.ActivityLog)
            .Produces<CrmIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Logs an interaction. Append-only — a logged activity is never edited.");

        crm.MapGet("/activities", ListActivitiesAsync)
            .RequirePermission(CrmPermissions.ActivityView)
            .Produces<IReadOnlyList<ActivityResponse>>()
            .WithSummary("Lists activities for a lead, opportunity or customer.");

        crm.MapPost("/segments", CreateSegmentAsync)
            .RequirePermission(CrmPermissions.SegmentManage)
            .Produces<CrmIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Creates a static or dynamic segment.");

        crm.MapPost("/segments/{segmentId:guid}/members", AddStaticMemberAsync)
            .RequirePermission(CrmPermissions.SegmentManage)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Adds a member to a static segment. Dynamic segments refuse.");

        crm.MapPost("/segments/{segmentId:guid}/deactivate", DeactivateSegmentAsync)
            .RequirePermission(CrmPermissions.SegmentManage)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Deactivates a segment. It matches nobody until reactivated.");

        crm.MapGet("/segments", ListSegmentsAsync)
            .RequirePermission(CrmPermissions.SegmentView)
            .Produces<IReadOnlyList<SegmentResponse>>()
            .WithSummary("Lists segments for a company.");

        crm.MapGet("/segments/{segmentId:guid}/members", GetSegmentMembersAsync)
            .RequirePermission(CrmPermissions.SegmentView)
            .Produces<IReadOnlyList<SegmentMemberResponse>>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Lists a static segment's members.");

        crm.MapPost("/consents/give", GiveConsentAsync)
            .RequirePermission(CrmPermissions.ConsentManage)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Records affirmative consent for one purpose.");

        crm.MapPost("/consents/withdraw", WithdrawConsentAsync)
            .RequirePermission(CrmPermissions.ConsentManage)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Withdraws consent. Immediate — no grace period for marketing.");

        crm.MapGet("/consents", GetConsentStateAsync)
            .RequirePermission(CrmPermissions.ConsentView)
            .Produces<IReadOnlyList<ConsentResponse>>()
            .WithSummary("Reads every consent row for a customer.");

        crm.MapGet("/customers/{customerId:guid}/360-view", GetCustomer360ViewAsync)
            .RequirePermission(CrmPermissions.View360)
            .Produces<Customer360ViewResponse>()
            .WithSummary("The 360° customer view, materialised live on every read.");

        return endpoints;
    }

    private static void BindCompany(ICompanyContext company, Guid? companyId)
    {
        ArgumentNullException.ThrowIfNull(company);

        if (companyId is null || companyId == Guid.Empty)
        {
            return;
        }

        if (company.CompanyId is { } bound && bound != companyId)
        {
            throw new ValidationFailedException(
                nameof(companyId),
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    [nameof(companyId)] = ["The request names a different company than the scope already holds."],
                });
        }

        if (company.CompanyId is null)
        {
            company.SetCompany(companyId.Value);
        }
    }

    private static async Task<IResult> CreateLeadAsync(
        CreateLeadRequest request,
        IDispatcher dispatcher,
        ICompanyContext company,
        CancellationToken cancellationToken)
    {
        BindCompany(company, request.CompanyId);
        Guid id = await dispatcher
            .SendAsync(
                new CreateLeadCommand(
                    request.CompanyId, request.FirstName, request.LastName, request.Email,
                    request.Phone, request.Company,
                    Enum.Parse<LeadSource>(request.Source, ignoreCase: true), request.StoreId),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/crm/leads/{id}", new CrmIdResponse(id));
    }

    private static async Task<IResult> UpdateLeadAsync(
        Guid leadId,
        UpdateLeadRequest request,
        IDispatcher dispatcher,
        ICompanyContext company,
        CancellationToken cancellationToken)
    {
        BindCompany(company, company.CompanyId);
        await dispatcher
            .SendAsync(
                new UpdateLeadCommand(leadId, request.FirstName, request.LastName, request.Phone, request.Company),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> AssignLeadAsync(
        Guid leadId,
        AssignLeadRequest request,
        IDispatcher dispatcher,
        ICompanyContext company,
        CancellationToken cancellationToken)
    {
        BindCompany(company, company.CompanyId);
        await dispatcher
            .SendAsync(new AssignLeadCommand(leadId, request.UserId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> DisqualifyLeadAsync(
        Guid leadId,
        DisqualifyLeadRequest request,
        IDispatcher dispatcher,
        ICompanyContext company,
        CancellationToken cancellationToken)
    {
        BindCompany(company, company.CompanyId);
        await dispatcher
            .SendAsync(new DisqualifyLeadCommand(leadId, request.Dead), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> ConvertLeadAsync(
        Guid leadId,
        ConvertLeadRequest request,
        IDispatcher dispatcher,
        ICompanyContext company,
        CancellationToken cancellationToken)
    {
        BindCompany(company, company.CompanyId);
        LeadConversionOutcome outcome = await dispatcher
            .SendAsync(new ConvertLeadCommand(leadId, request.CustomerId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new LeadConversionResponse(outcome.LeadId, outcome.CustomerId, outcome.ConvertedAt));
    }

    private static async Task<IResult> GetLeadAsync(
        Guid leadId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        LeadEntry lead = await dispatcher
            .QueryAsync(new GetLeadQuery(leadId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(ToResponse(lead));
    }

    private static async Task<IResult> ListLeadsAsync(
        Guid? companyId,
        string? status,
        int? limit,
        string? after,
        IDispatcher dispatcher,
        ICompanyContext company,
        CancellationToken cancellationToken)
    {
        BindCompany(company, companyId);
        LeadStatus? parsed = string.IsNullOrWhiteSpace(status)
            ? null
            : Enum.Parse<LeadStatus>(status, ignoreCase: true);

        PageResult<LeadEntry> page = await dispatcher
            .QueryAsync(new ListLeadsQuery(companyId, parsed, limit, after), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new PageResponse<LeadResponse>(
            [.. page.Items.Select(ToResponse)], page.NextCursor, page.HasMore));
    }

    private static async Task<IResult> CreateOpportunityAsync(
        CreateOpportunityRequest request,
        IDispatcher dispatcher,
        ICompanyContext company,
        CancellationToken cancellationToken)
    {
        BindCompany(company, request.CompanyId);
        Guid id = await dispatcher
            .SendAsync(
                new CreateOpportunityCommand(
                    request.CompanyId, request.Title, request.ExpectedAmount, request.Currency,
                    request.Probability, request.LeadId, request.CustomerId, request.Description,
                    request.CloseDate, request.StoreId),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/crm/opportunities/{id}", new CrmIdResponse(id));
    }

    private static async Task<IResult> MoveOpportunityStageAsync(
        Guid opportunityId,
        MoveOpportunityStageRequest request,
        IDispatcher dispatcher,
        ICompanyContext company,
        CancellationToken cancellationToken)
    {
        BindCompany(company, company.CompanyId);
        await dispatcher
            .SendAsync(
                new MoveOpportunityStageCommand(
                    opportunityId, Enum.Parse<OpportunityStage>(request.Stage, ignoreCase: true),
                    request.Probability),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> WinOpportunityAsync(
        Guid opportunityId,
        WinOpportunityRequest request,
        IDispatcher dispatcher,
        ICompanyContext company,
        CancellationToken cancellationToken)
    {
        BindCompany(company, company.CompanyId);
        await dispatcher
            .SendAsync(new WinOpportunityCommand(opportunityId, request.CustomerId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> LoseOpportunityAsync(
        Guid opportunityId,
        LoseOpportunityRequest request,
        IDispatcher dispatcher,
        ICompanyContext company,
        CancellationToken cancellationToken)
    {
        BindCompany(company, company.CompanyId);
        await dispatcher
            .SendAsync(new LoseOpportunityCommand(opportunityId, request.LossReason), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> GetOpportunityAsync(
        Guid opportunityId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        OpportunityEntry opportunity = await dispatcher
            .QueryAsync(new GetOpportunityQuery(opportunityId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(ToResponse(opportunity));
    }

    private static async Task<IResult> ListOpportunitiesAsync(
        Guid? companyId,
        string? stage,
        int? limit,
        string? after,
        IDispatcher dispatcher,
        ICompanyContext company,
        CancellationToken cancellationToken)
    {
        BindCompany(company, companyId);
        OpportunityStage? parsed = string.IsNullOrWhiteSpace(stage)
            ? null
            : Enum.Parse<OpportunityStage>(stage, ignoreCase: true);

        PageResult<OpportunityEntry> page = await dispatcher
            .QueryAsync(new ListOpportunitiesQuery(companyId, parsed, limit, after), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new PageResponse<OpportunityResponse>(
            [.. page.Items.Select(ToResponse)], page.NextCursor, page.HasMore));
    }

    private static async Task<IResult> LogActivityAsync(
        LogActivityRequest request,
        IDispatcher dispatcher,
        ICompanyContext company,
        CancellationToken cancellationToken)
    {
        BindCompany(company, request.CompanyId);
        Guid id = await dispatcher
            .SendAsync(
                new LogActivityCommand(
                    request.CompanyId, Enum.Parse<ActivityType>(request.Type, ignoreCase: true),
                    request.Subject, request.Body,
                    Enum.Parse<ActivityDirection>(request.Direction, ignoreCase: true),
                    request.DurationMinutes, request.LeadId, request.OpportunityId,
                    request.CustomerId, request.StoreId),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/crm/activities/{id}", new CrmIdResponse(id));
    }

    private static async Task<IResult> ListActivitiesAsync(
        Guid? leadId,
        Guid? opportunityId,
        Guid? customerId,
        int? limit,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ActivityEntry> rows = await dispatcher
            .QueryAsync(new ListActivitiesQuery(leadId, opportunityId, customerId, limit), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<ActivityResponse>>([.. rows.Select(ToResponse)]);
    }

    private static async Task<IResult> CreateSegmentAsync(
        CreateSegmentRequest request,
        IDispatcher dispatcher,
        ICompanyContext company,
        CancellationToken cancellationToken)
    {
        BindCompany(company, request.CompanyId);
        Guid id = await dispatcher
            .SendAsync(
                new CreateSegmentCommand(
                    request.CompanyId, request.Name,
                    Enum.Parse<SegmentKind>(request.Kind, ignoreCase: true),
                    request.Description, request.QueryExpression, request.StoreId),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/crm/segments/{id}", new CrmIdResponse(id));
    }

    private static async Task<IResult> AddStaticMemberAsync(
        Guid segmentId,
        AddStaticMemberRequest request,
        IDispatcher dispatcher,
        ICompanyContext company,
        CancellationToken cancellationToken)
    {
        BindCompany(company, company.CompanyId);
        await dispatcher
            .SendAsync(
                new AddStaticMemberCommand(
                    segmentId, Enum.Parse<MemberType>(request.MemberType, ignoreCase: true),
                    request.MemberId),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> DeactivateSegmentAsync(
        Guid segmentId,
        IDispatcher dispatcher,
        ICompanyContext company,
        CancellationToken cancellationToken)
    {
        BindCompany(company, company.CompanyId);
        await dispatcher
            .SendAsync(new DeactivateSegmentCommand(segmentId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> ListSegmentsAsync(
        Guid? companyId,
        IDispatcher dispatcher,
        ICompanyContext company,
        CancellationToken cancellationToken)
    {
        BindCompany(company, companyId);
        IReadOnlyList<SegmentEntry> rows = await dispatcher
            .QueryAsync(new ListSegmentsQuery(companyId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<SegmentResponse>>([.. rows.Select(ToResponse)]);
    }

    private static async Task<IResult> GetSegmentMembersAsync(
        Guid segmentId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        IReadOnlyList<SegmentMemberEntry> rows = await dispatcher
            .QueryAsync(new GetSegmentMembersQuery(segmentId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<SegmentMemberResponse>>(
            [.. rows.Select(row => new SegmentMemberResponse(
                row.MemberType.ToString(), row.MemberId, row.AddedAt))]);
    }

    private static async Task<IResult> GiveConsentAsync(
        GiveConsentRequest request,
        IDispatcher dispatcher,
        ICompanyContext company,
        CancellationToken cancellationToken)
    {
        BindCompany(company, request.CompanyId);
        await dispatcher
            .SendAsync(
                new GiveConsentCommand(
                    request.CompanyId, request.CustomerId,
                    Enum.Parse<ConsentType>(request.Type, ignoreCase: true),
                    request.Source, request.ExpiresAt, request.StoreId),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> WithdrawConsentAsync(
        WithdrawConsentRequest request,
        IDispatcher dispatcher,
        ICompanyContext company,
        CancellationToken cancellationToken)
    {
        BindCompany(company, request.CompanyId);
        await dispatcher
            .SendAsync(
                new WithdrawConsentCommand(request.CompanyId, request.CustomerId,
                    Enum.Parse<ConsentType>(request.Type, ignoreCase: true), request.Reason),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> GetConsentStateAsync(
        Guid customerId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        IReadOnlyList<ConsentEntry> rows = await dispatcher
            .QueryAsync(new GetConsentStateQuery(customerId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<ConsentResponse>>(
            [.. rows.Select(row => new ConsentResponse(
                row.CustomerId, row.Type.ToString(), row.State.ToString(),
                row.GrantedAt, row.WithdrawnAt, row.ExpiresAt))]);
    }

    private static async Task<IResult> GetCustomer360ViewAsync(
        Guid customerId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Customer360View view = await dispatcher
            .QueryAsync(new GetCustomer360ViewQuery(customerId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new Customer360ViewResponse(
            view.CustomerId, view.LeadCount, view.OpenOpportunityCount, view.OpenOpportunityValue,
            view.OpportunityCurrency, view.ActivityCount, view.SegmentNames,
            [.. view.Consents.Select(entry => new ConsentStateResponse(
                entry.Type.ToString(), entry.State.ToString()))],
            view.AsAt));
    }

    private static LeadResponse ToResponse(LeadEntry lead) => new(
        lead.Id, lead.CompanyId, lead.FirstName, lead.LastName, lead.Email, lead.Phone,
        lead.Company, lead.Source.ToString(), lead.Status.ToString(), lead.AssignedTo,
        lead.CustomerId, lead.ConvertedAt);

    private static OpportunityResponse ToResponse(OpportunityEntry opportunity) => new(
        opportunity.Id, opportunity.CompanyId, opportunity.Title, opportunity.Description,
        opportunity.ExpectedAmount, opportunity.Currency, opportunity.Stage.ToString(),
        opportunity.Probability, opportunity.CloseDate, opportunity.LossReason,
        opportunity.LeadId, opportunity.CustomerId, opportunity.AssignedTo);

    private static ActivityResponse ToResponse(ActivityEntry activity) => new(
        activity.Id, activity.Type.ToString(), activity.Direction.ToString(), activity.Subject,
        activity.Body, activity.HappenedAt, activity.DurationMinutes, activity.LeadId,
        activity.OpportunityId, activity.CustomerId);

    private static SegmentResponse ToResponse(SegmentEntry segment) => new(
        segment.Id, segment.Name, segment.Description, segment.Kind.ToString(),
        segment.QueryExpression, segment.IsActive);
}
