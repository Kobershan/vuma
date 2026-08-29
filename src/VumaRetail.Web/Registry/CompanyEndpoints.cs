using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Application.Registry;
using VumaRetail.Contracts.Registry;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Web.Api;

namespace VumaRetail.Web.Registry;

public static class CompanyEndpoints
{
    public const string SelectionHeader = "X-Vuma-Company-Id";

    public static IEndpointRouteBuilder MapVumaCompanies(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder companies = endpoints.MapVumaApi().MapGroup("/companies").WithTags("Companies");
        companies.MapGet("/", async (VumaRegistryDbContext db, ITenantContext tenant, CancellationToken ct) =>
            Results.Ok(await db.Companies.AsNoTracking().Where(x => x.TenantId == tenant.TenantId && x.IsActive).OrderBy(x => x.Code).Select(x => new CompanyResponse(x.Id, x.Code, x.LegalName, x.TradingName, x.BaseCurrency, x.Locale, x.DocumentPrefix, x.LifecycleState.ToString(), x.IsActive, x.SchemaVersion, x.MigrationState, x.ProvisioningStep, x.ProvisioningError, x.DeactivatedAt, x.DeactivatedBy, x.DeactivationReason)).ToListAsync(ct)))
            .RequirePermission(PlatformPermissions.CompanyView).Produces<IReadOnlyList<CompanyResponse>>().WithSummary("Lists active companies available to this tenant.");

        companies.MapPost("/", async (ProvisionCompanyRequest request, IDispatcher dispatcher, CancellationToken ct) =>
            Results.Created("/api/v1/companies", new { id = await dispatcher.SendAsync(new ProvisionCompanyCommand(request.Code, request.LegalName, request.TradingName, request.BaseCurrency, request.Locale, request.DocumentPrefix), ct) }))
            .RequirePermission(PlatformPermissions.CompanyManage).Produces(StatusCodes.Status201Created).WithSummary("Provisions an isolated company database and registers the company.");

        companies.MapPost("/{companyId:guid}/activate", async (Guid companyId, IDispatcher dispatcher, CancellationToken ct) => { await dispatcher.SendAsync(new ActivateCompanyCommand(companyId), ct); return Results.NoContent(); })
            .RequirePermission(PlatformPermissions.CompanyManage).Produces(StatusCodes.Status204NoContent).WithSummary("Activates a registered company after migrations are current.");

        companies.MapPost("/{companyId:guid}/deactivate", async (Guid companyId, DeactivateCompanyRequest request, IDispatcher dispatcher, CancellationToken ct) => { await dispatcher.SendAsync(new DeactivateCompanyCommand(companyId, request.Reason), ct); return Results.NoContent(); })
            .RequirePermission(PlatformPermissions.CompanyManage).Produces(StatusCodes.Status204NoContent).WithSummary("Deactivates a company while retaining it read-only.");

        companies.MapGet("/{companyId:guid}/migration", async (Guid companyId, VumaRegistryDbContext db, ITenantContext tenant, CancellationToken ct) =>
            await db.Companies.AsNoTracking().Where(x => x.Id == companyId && x.TenantId == tenant.TenantId).Select(x => new CompanyMigrationStatusResponse(x.Id, x.Code, x.LifecycleState.ToString(), x.SchemaVersion, x.MigrationState, x.MigrationState == "Current" ? null : "Apply the pending company migration before serving this company.", x.ProvisioningError)).SingleOrDefaultAsync(ct) is { } status ? Results.Ok(status) : Results.NotFound())
            .RequirePermission(PlatformPermissions.CompanyView).Produces<CompanyMigrationStatusResponse>().WithSummary("Reports the migration state of one tenant company.");

        companies.MapPost("/select", (HttpContext http, VumaRegistryDbContext db, ITenantContext tenant, IPrincipalAccessor principal, ICompanyContext context) =>
        {
            if (!http.Request.Headers.TryGetValue(SelectionHeader, out var value) || !Guid.TryParse(value, out var companyId)) throw new InvalidOperationException("COMPANY_SELECTION_REQUIRED");
            if (!db.Companies.Any(x => x.Id == companyId && x.TenantId == tenant.TenantId && x.IsActive)) return Results.NotFound();
            context.SetCompany(companyId);
            return Results.Ok(new SelectCompanyResponse(companyId, principal.TerminalId is not null ? "terminal" : "header"));
        }).RequirePermission(PlatformPermissions.CompanyView).Produces<SelectCompanyResponse>().WithSummary("Validates and binds the acting company for this request.");

        return endpoints;
    }

    public sealed record DeactivateCompanyRequest(string Reason);
}
