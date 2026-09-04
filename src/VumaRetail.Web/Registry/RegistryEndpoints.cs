using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Application.Registry;
using VumaRetail.Contracts.Registry;
using VumaRetail.Domain.Registry;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Web.Api;

namespace VumaRetail.Web.Registry;

/// <summary>Stage 06e: the Operator ID, company links, shared premises, registry users and tills.</summary>
public static class RegistryEndpoints
{
    /// <summary>Maps the trading-group routes.</summary>
    /// <param name="endpoints">The route builder.</param>
    /// <returns>The route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapVumaRegistry(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder operators = endpoints.MapVumaApi().MapGroup("/operator").WithTags("Operator");
        operators.MapGet("/", async (VumaRegistryDbContext db, ITenantContext tenant, IOperatorContext operatorContext, CancellationToken ct) =>
        {
            Guid operatorId = operatorContext.RequireOperatorId();
            Operator? row = await db.Operators.AsNoTracking()
                .FirstOrDefaultAsync(o => o.OperatorId == operatorId && o.TenantId == tenant.TenantId, ct);

            if (row is null)
            {
                return Results.NotFound();
            }

            List<OperatorCompanyResponse> companies = await db.Companies.AsNoTracking()
                .Where(c => c.TenantId == tenant.TenantId && c.OperatorId == operatorId)
                .OrderBy(c => c.Code)
                .Select(c => new OperatorCompanyResponse(c.Id, c.Code, c.IsActive))
                .ToListAsync(ct);

            return Results.Ok(new OperatorResponse(row.OperatorId, row.DisplayName, row.IsActive, companies));
        })
        .RequirePermission(RegistryPermissions.GroupLinkView)
        .Produces<OperatorResponse>()
        .Produces(StatusCodes.Status404NotFound)
        .WithSummary("The acting Operator ID and its companies.");

        RouteGroupBuilder links = endpoints.MapVumaApi().MapGroup("/company-links").WithTags("CompanyLinks");
        links.MapGet("/", async (Guid? companyId, string? status, VumaRegistryDbContext db, ITenantContext tenant, CancellationToken ct) =>
        {
            IQueryable<CompanyLink> query = db.CompanyLinks.AsNoTracking().Where(x => x.TenantId == tenant.TenantId);

            if (companyId is not null)
            {
                query = query.Where(x => x.CompanyAId == companyId || x.CompanyBId == companyId);
            }

            if (status is not null)
            {
                if (!Enum.TryParse<CompanyLinkStatus>(status, ignoreCase: true, out CompanyLinkStatus parsed))
                {
                    throw new InvalidOperationException("LINK_STATUS_UNKNOWN");
                }

                query = query.Where(x => x.Status == parsed);
            }

            List<CompanyLinkResponse> result = await query
                .OrderBy(x => x.CompanyAId)
                .ThenBy(x => x.CompanyBId)
                .Select(x => new CompanyLinkResponse(x.Id, x.CompanyAId, x.CompanyBId, (int)x.Scopes, x.Status.ToString(), x.EffectiveFrom, x.EffectiveTo))
                .ToListAsync(ct);

            return Results.Ok(result);
        })
        .RequirePermission(RegistryPermissions.GroupLinkView)
        .Produces<IReadOnlyList<CompanyLinkResponse>>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .WithSummary("Lists company links, filterable by company and status.");

        links.MapPost("/", async (ProposeCompanyLinkRequest request, IDispatcher dispatcher, CancellationToken ct) =>
            Results.Created(
                "/api/v1/company-links",
                await dispatcher.SendAsync(new ProposeCompanyLinkCommand(request.CompanyAId, request.CompanyBId, (CompanyLinkScope)request.Scopes), ct)))
        .RequirePermission(RegistryPermissions.GroupLinkPropose)
        .Produces<Guid>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .WithSummary("Proposes a company link. Both companies must sit under the acting operator.");

        links.MapPost("/{id:guid}/accept", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
        {
            await dispatcher.SendAsync(new AcceptCompanyLinkCommand(id), ct);
            return Results.NoContent();
        })
        .RequirePermission(RegistryPermissions.GroupLinkAccept)
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .WithSummary("Accepts a link proposal for the acting company. The second acceptance activates it.");

        links.MapPost("/{id:guid}/suspend", async (Guid id, SuspendCompanyLinkRequest request, IDispatcher dispatcher, CancellationToken ct) =>
        {
            await dispatcher.SendAsync(new SuspendCompanyLinkCommand(id, request.Reason), ct);
            return Results.NoContent();
        })
        // No suspend permission is declared: suspending shares the revoke permission, which is
        // the link-lifecycle mutation permission alongside accept.
        .RequirePermission(RegistryPermissions.GroupLinkRevoke)
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .WithSummary("Suspends an active link with a reason. Reversible via resume.");

        links.MapPost("/{id:guid}/resume", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
        {
            await dispatcher.SendAsync(new ResumeCompanyLinkCommand(id), ct);
            return Results.NoContent();
        })
        // Resuming re-accepts the arrangement, so it shares the accept permission.
        .RequirePermission(RegistryPermissions.GroupLinkAccept)
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .WithSummary("Resumes a suspended link.");

        links.MapPost("/{id:guid}/revoke", async (Guid id, RevokeCompanyLinkRequest request, IDispatcher dispatcher, CancellationToken ct) =>
        {
            await dispatcher.SendAsync(new RevokeCompanyLinkCommand(id, request.Reason), ct);
            return Results.NoContent();
        })
        .RequirePermission(RegistryPermissions.GroupLinkRevoke)
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .WithSummary("Revokes a link with a reason of at least ten characters. Final; history stands.");

        RouteGroupBuilder premises = endpoints.MapVumaApi().MapGroup("/premises").WithTags("Premises");
        premises.MapGet("/", async (VumaRegistryDbContext db, ITenantContext tenant, CancellationToken ct) =>
            Results.Ok(await db.Premises.AsNoTracking()
                .Where(x => x.TenantId == tenant.TenantId)
                .OrderBy(x => x.Code)
                .Select(x => new PremisesResponse(x.Id, x.Code, x.Name, x.IsActive))
                .ToListAsync(ct)))
        .RequirePermission(RegistryPermissions.PremisesManage)
        .Produces<IReadOnlyList<PremisesResponse>>()
        .WithSummary("Lists premises.");

        premises.MapPost("/", async (CreatePremisesRequest request, IDispatcher dispatcher, CancellationToken ct) =>
            Results.Created(
                "/api/v1/premises",
                await dispatcher.SendAsync(new CreatePremisesCommand(request.Code, request.Name, request.Address, request.GeoLocation, request.TradingHours), ct)))
        .RequirePermission(RegistryPermissions.PremisesManage)
        .Produces<Guid>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .WithSummary("Creates a premises.");

        premises.MapPost("/{id:guid}/occupancies", async (Guid id, AddPremisesOccupancyRequest request, IDispatcher dispatcher, CancellationToken ct) =>
            Results.Created(
                $"/api/v1/premises/{id}/occupancies",
                await dispatcher.SendAsync(new AddPremisesOccupancyCommand(id, request.CompanyId, request.StoreId), ct)))
        .RequirePermission(RegistryPermissions.PremisesManage)
        .Produces<Guid>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .WithSummary("Adds a company as an occupant of a premises. A second occupant needs SharedFloor with each current one.");

        premises.MapPost("/{id:guid}/bin-layout/publish", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
        {
            await dispatcher.SendAsync(new PublishPremisesBinLayoutCommand(id), ct);
            return Results.NoContent();
        })
        .RequirePermission(RegistryPermissions.PremisesManage)
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .WithSummary("Mirrors the mastered bin layout into every occupying company's warehouse schema as a saga.");

        RouteGroupBuilder users = endpoints.MapVumaApi().MapGroup("/users").WithTags("RegistryUsers");
        users.MapGet("/", async (VumaRegistryDbContext db, ITenantContext tenant, CancellationToken ct) =>
            Results.Ok(await db.RegistryUsers.AsNoTracking()
                .Where(x => x.TenantId == tenant.TenantId)
                .OrderBy(x => x.Login)
                .Select(x => new RegistryUserResponse(x.Id, x.Login, x.DisplayName, x.IsEnabled))
                .ToListAsync(ct)))
        .RequirePermission(RegistryPermissions.RegistryUserManage)
        .Produces<IReadOnlyList<RegistryUserResponse>>()
        .WithSummary("Lists the registry user directory.");

        users.MapPost("/", async (CreateRegistryUserRequest request, IDispatcher dispatcher, CancellationToken ct) =>
            Results.Created(
                "/api/v1/users",
                await dispatcher.SendAsync(new CreateRegistryUserCommand(request.Login, request.DisplayName, request.ContactDetails, request.OperatorId), ct)))
        .RequirePermission(RegistryPermissions.RegistryUserManage)
        .Produces<Guid>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .WithSummary("Creates a registry user.");

        users.MapPost("/{id:guid}/company-access", async (Guid id, GrantCompanyAccessRequest request, IDispatcher dispatcher, CancellationToken ct) =>
            Results.Created(
                $"/api/v1/users/{id}/company-access",
                await dispatcher.SendAsync(new GrantCompanyAccessCommand(id, request.CompanyId, request.Roles), ct)))
        .RequirePermission(RegistryPermissions.RegistryUserManage)
        .Produces<Guid>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .WithSummary("Grants a user access to a company under the same Operator ID.");

        users.MapDelete("/{id:guid}/company-access/{companyId:guid}", async (Guid id, Guid companyId, IDispatcher dispatcher, CancellationToken ct) =>
        {
            await dispatcher.SendAsync(new RevokeCompanyAccessCommand(id, companyId), ct);
            return Results.NoContent();
        })
        .RequirePermission(RegistryPermissions.RegistryUserManage)
        .Produces(StatusCodes.Status204NoContent)
        .WithSummary("Revokes a user's access to a company.");

        RouteGroupBuilder terminals = endpoints.MapVumaApi().MapGroup("/terminals").WithTags("Terminals");
        terminals.MapPost("/", async (RegisterTerminalRequest request, IDispatcher dispatcher, CancellationToken ct) =>
            Results.Created(
                "/api/v1/terminals",
                await dispatcher.SendAsync(new RegisterTerminalCommand(request.PremisesId, request.TerminalId, request.DeviceCertThumbprint), ct)))
        .RequirePermission(RegistryPermissions.TerminalManage)
        .Produces<Guid>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .WithSummary("Registers a terminal at a premises.");

        terminals.MapPost("/{id:guid}/companies", async (Guid id, SetTerminalCompaniesRequest request, IDispatcher dispatcher, CancellationToken ct) =>
        {
            await dispatcher.SendAsync(new SetTerminalCompaniesCommand(id, request.CompanyIds), ct);
            return Results.NoContent();
        })
        .RequirePermission(RegistryPermissions.TerminalManage)
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .WithSummary("Sets the companies a till may sell for, all under one Operator ID.");

        return endpoints;
    }
}
