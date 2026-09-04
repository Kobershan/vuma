using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Registry;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Domain.Registry;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Contracts.Registry;
using VumaRetail.Web.Api;

namespace VumaRetail.Web.Registry;

public static class RegistryEndpoints
{
    public static IEndpointRouteBuilder MapVumaRegistry(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder registry = endpoints.MapVumaApi().MapGroup("/registry").WithTags("Registry");

        // Company links
        registry.MapPost("/company-links", async (ProposeCompanyLinkRequest request, IDispatcher dispatcher, CancellationToken ct) =>
            Results.Created("/api/v1/registry/company-links", await dispatcher.SendAsync(new ProposeCompanyLinkCommand(request.CompanyAId, request.CompanyBId, request.Scopes), ct)))
            .RequirePermission(RegistryPermissions.GroupLinkPropose).Produces<Guid>().WithSummary("Proposes a company link.");

        registry.MapGet("/company-links", async (VumaRegistryDbContext db, ITenantContext tenant, CancellationToken ct) =>
            Results.Ok(await db.CompanyLinks.AsNoTracking().Where(x => x.TenantId == tenant.TenantId).Select(x => new { x.Id, x.CompanyAId, x.CompanyBId, x.Scopes, x.Status, x.EffectiveFrom, x.EffectiveTo }).ToListAsync(ct)))
            .RequirePermission(RegistryPermissions.GroupLinkView).Produces<IReadOnlyList<object>>().WithSummary("Lists company links.");

        registry.MapPost("/company-links/{id:guid}/accept", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
            { await dispatcher.SendAsync(new AcceptCompanyLinkCommand(id), ct); return Results.NoContent(); })
            .RequirePermission(RegistryPermissions.GroupLinkAccept).Produces(StatusCodes.Status204NoContent).WithSummary("Accepts a company link proposal.");

        registry.MapPost("/company-links/{id:guid}/suspend", async (Guid id, SuspendCompanyLinkRequest request, IDispatcher dispatcher, CancellationToken ct) =>
            { await dispatcher.SendAsync(new SuspendCompanyLinkCommand(id, request.Reason), ct); return Results.NoContent(); })
            .RequirePermission(RegistryPermissions.GroupLinkRevoke).Produces(StatusCodes.Status204NoContent).WithSummary("Suspends/revokes a company link.");

        registry.MapPost("/company-links/{id:guid}/revoke", async (Guid id, RevokeCompanyLinkRequest request, IDispatcher dispatcher, CancellationToken ct) =>
            { await dispatcher.SendAsync(new RevokeCompanyLinkCommand(id, request.Reason), ct); return Results.NoContent(); })
            .RequirePermission(RegistryPermissions.GroupLinkRevoke).Produces(StatusCodes.Status204NoContent).WithSummary("Revokes a company link.");

        // Premises
        registry.MapPost("/premises", async (CreatePremisesRequest request, IDispatcher dispatcher, CancellationToken ct) =>
            Results.Created("/api/v1/registry/premises", await dispatcher.SendAsync(new CreatePremisesCommand(request.Code, request.Name, request.Address, request.GeoLocation, request.TradingHours), ct)))
            .RequirePermission(RegistryPermissions.PremisesManage).Produces<Guid>().WithSummary("Creates a premises.");

        registry.MapGet("/premises", async (VumaRegistryDbContext db, ITenantContext tenant, CancellationToken ct) =>
            Results.Ok(await db.Premises.AsNoTracking().Where(x => x.TenantId == tenant.TenantId).Select(x => new { x.Id, x.Code, x.Name, x.Address, x.GeoLocation, x.TradingHours, x.IsActive }).ToListAsync(ct)))
            .RequirePermission(RegistryPermissions.GroupLinkView).Produces<IReadOnlyList<object>>().WithSummary("Lists premises.");

        registry.MapPost("/premises/{id:guid}/occupants", async (Guid id, AddOccupancyRequest request, IDispatcher dispatcher, CancellationToken ct) =>
            Results.Created("/api/v1/registry/premises/{id}/occupants", await dispatcher.SendAsync(new AddPremisesOccupancyCommand(id, request.CompanyId, request.StoreId), ct)))
            .RequirePermission(RegistryPermissions.PremisesManage).Produces<Guid>().WithSummary("Adds a company as an occupant of a premises.");

        registry.MapPost("/premises/{id:guid}/bin-layouts", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
            { await dispatcher.SendAsync(new PublishPremisesBinLayoutCommand(id), ct); return Results.NoContent(); })
            .RequirePermission(RegistryPermissions.PremisesManage).Produces(StatusCodes.Status204NoContent).WithSummary("Publishes bin layout from premises.");

        // Registry users
        registry.MapPost("/registry-users", async (CreateRegistryUserRequest request, IDispatcher dispatcher, CancellationToken ct) =>
            Results.Created("/api/v1/registry/registry-users", await dispatcher.SendAsync(new CreateRegistryUserCommand(request.Login, request.DisplayName, request.ContactDetails, request.OperatorId), ct)))
            .RequirePermission(RegistryPermissions.RegistryUserManage).Produces<Guid>().WithSummary("Creates a registry user.");

        registry.MapPost("/registry-users/{id:guid}/access", async (Guid id, GrantAccessRequest request, IDispatcher dispatcher, CancellationToken ct) =>
            Results.Created("/api/v1/registry/registry-users/{id}/access", await dispatcher.SendAsync(new GrantCompanyAccessCommand(id, request.CompanyId, request.Roles), ct)))
            .RequirePermission(RegistryPermissions.RegistryUserManage).Produces<Guid>().WithSummary("Grants a user access to a company.");

        // Terminals
        registry.MapPost("/terminals", async (RegisterTerminalRequest request, IDispatcher dispatcher, CancellationToken ct) =>
            Results.Created("/api/v1/registry/terminals", await dispatcher.SendAsync(new RegisterTerminalCommand(request.PremisesId, request.TerminalId, request.DeviceCertThumbprint), ct)))
            .RequirePermission(RegistryPermissions.TerminalManage).Produces<Guid>().WithSummary("Registers a terminal.");

        registry.MapPost("/terminals/{id:guid}/companies", async (Guid id, SetTerminalCompaniesRequest request, IDispatcher dispatcher, CancellationToken ct) =>
            { await dispatcher.SendAsync(new SetTerminalCompaniesCommand(id, request.CompanyIds), ct); return Results.NoContent(); })
            .RequirePermission(RegistryPermissions.TerminalManage).Produces(StatusCodes.Status204NoContent).WithSummary("Sets companies a terminal may sell for.");

        return endpoints;
    }
}

public sealed record ProposeCompanyLinkRequest(Guid CompanyAId, Guid CompanyBId, CompanyLinkScope Scopes);
public sealed record SuspendCompanyLinkRequest(string Reason);
public sealed record RevokeCompanyLinkRequest(string Reason);
public sealed record CreatePremisesRequest(string Code, string Name, string Address, string GeoLocation, string TradingHours);
public sealed record AddOccupancyRequest(Guid CompanyId, Guid StoreId);
public sealed record CreateRegistryUserRequest(string Login, string DisplayName, string ContactDetails, Guid OperatorId);
public sealed record GrantAccessRequest(Guid CompanyId, string Roles);
public sealed record RegisterTerminalRequest(Guid PremisesId, string TerminalId, string DeviceCertThumbprint);
public sealed record SetTerminalCompaniesRequest(List<Guid> CompanyIds);