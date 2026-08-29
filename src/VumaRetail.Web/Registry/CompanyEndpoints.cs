using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Application.Registry;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Web.Api;

namespace VumaRetail.Web.Registry;

public static class CompanyEndpoints
{
    public static IEndpointRouteBuilder MapVumaCompanies(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder companies = endpoints.MapVumaApi().MapGroup("/companies").WithTags("Companies");
        companies.MapGet("/", async (VumaRegistryDbContext db, ITenantContext tenant, CancellationToken ct) =>
                Results.Ok(await db.Companies.AsNoTracking().Where(x => x.TenantId == tenant.TenantId)
                    .Select(x => new CompanyResponse(x.Id, x.Code, x.LegalName, x.TradingName,
                        x.LifecycleState.ToString(), x.IsActive, x.DeactivatedAt, x.DeactivatedBy, x.DeactivationReason))
                    .ToListAsync(ct)))
            .RequirePermission(PlatformPermissions.CompanyView);

        companies.MapPost("/{companyId:guid}/deactivate", async (Guid companyId, DeactivateCompanyRequest request,
                IDispatcher dispatcher, CancellationToken ct) =>
                { await dispatcher.SendAsync(new DeactivateCompanyCommand(companyId, request.Reason), ct); return Results.NoContent(); })
            .RequirePermission(PlatformPermissions.CompanyManage)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return endpoints;
    }

    public sealed record DeactivateCompanyRequest(string Reason);
    public sealed record CompanyResponse(Guid Id, string Code, string LegalName, string TradingName,
        string LifecycleState, bool IsActive, DateTimeOffset? DeactivatedAt, string? DeactivatedBy, string? DeactivationReason);
}
