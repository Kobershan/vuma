#pragma warning disable CS1591
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Marketing;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Web.Api;
using VumaRetail.Web.Licensing;

namespace VumaRetail.Web.Marketing;

public static class MarketingEndpoints
{
    public static IEndpointRouteBuilder MapVumaMarketing(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapVumaApi().MapGroup("/marketing").WithTags("Marketing").RequireModule("marketing");
        group.MapPost("/campaigns", async (CreateCampaignRequest r, ICompanyContext company, IDispatcher d, CancellationToken ct) => { company.SetCompany(r.CompanyId); return Results.Created("/api/v1/marketing/campaigns", await d.SendAsync(new CreateMarketingCampaignCommand(r.CompanyId, r.StoreId, r.Name, r.TemplateId, r.ScheduledAt), ct)); }).RequirePermission(MarketingPermissions.Manage);
        group.MapPost("/campaigns/{id:guid}/schedule", async (Guid id, Guid? companyId, ICompanyContext company, IDispatcher d, CancellationToken ct) => { BindCompany(company, companyId); await d.SendAsync(new ScheduleMarketingCampaignCommand(id), ct); return Results.NoContent(); }).RequirePermission(MarketingPermissions.Manage);
        group.MapPost("/campaigns/{id:guid}/cancel", async (Guid id, Guid? companyId, ICompanyContext company, IDispatcher d, CancellationToken ct) => { BindCompany(company, companyId); await d.SendAsync(new CancelMarketingCampaignCommand(id), ct); return Results.NoContent(); }).RequirePermission(MarketingPermissions.Manage);
        group.MapPost("/messages", async (QueueMessageRequest r, ICompanyContext company, IDispatcher d, CancellationToken ct) => { company.SetCompany(r.CompanyId); return Results.Created("/api/v1/marketing/messages", await d.SendAsync(new QueueOutboundMessageCommand(r.CompanyId, r.StoreId, r.CampaignId, r.CustomerId, r.IdempotencyKey, r.ScheduledAt), ct)); }).RequirePermission(MarketingPermissions.Manage);
        group.MapPost("/messages/{id:guid}/suppress", async (Guid id, Guid? companyId, ICompanyContext company, IDispatcher d, CancellationToken ct) => { BindCompany(company, companyId); await d.SendAsync(new SuppressOutboundMessageCommand(id), ct); return Results.NoContent(); }).RequirePermission(MarketingPermissions.Manage);
        group.MapPost("/messages/{id:guid}/sent", async (Guid id, Guid? companyId, ICompanyContext company, IDispatcher d, CancellationToken ct) => { BindCompany(company, companyId); await d.SendAsync(new MarkOutboundMessageSentCommand(id), ct); return Results.NoContent(); }).RequirePermission(MarketingPermissions.Manage);
        return endpoints;
    }
    private static void BindCompany(ICompanyContext company, Guid? companyId)
    {
        if (companyId is { } selected) company.SetCompany(selected);
    }
    public sealed record CreateCampaignRequest(Guid CompanyId, Guid? StoreId, string Name, string TemplateId, DateTimeOffset ScheduledAt);
    public sealed record QueueMessageRequest(Guid CompanyId, Guid? StoreId, Guid CampaignId, Guid CustomerId, string IdempotencyKey, DateTimeOffset ScheduledAt);
}
