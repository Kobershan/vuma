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

        RouteGroupBuilder stage22 = endpoints.MapVumaApi().MapGroup("/stage22").WithTags("Stage22");
        stage22.MapPost("/business-groups", async (CreateBusinessGroupRequest request, IStage22RegistryService service, CancellationToken ct) =>
        {
            if (!Enum.TryParse<BusinessType>(request.BusinessType, true, out var type) || !Enum.TryParse<TransferCostingMethod>(request.TransferCostingMethod, true, out var costing) || !Enum.TryParse<DiscrepancyOwner>(request.DiscrepancyDefaultOwner, true, out var owner)) return Results.BadRequest("Unknown business type, costing method or discrepancy owner.");
            var result = await service.CreateBusinessAsync(request.BusinessId, request.BusinessName, type, request.TransferValueThreshold, costing, owner, request.StoreCodePrefix, ct);
            return Results.Created($"/api/v1/stage22/business-groups/{request.BusinessId}", new Stage22BusinessGroupResponse(result.Business.Id, result.Business.Name, result.Business.Type.ToString(), result.Settings.TransferValueThreshold, result.Settings.TransferCostingMethod.ToString(), result.Settings.DiscrepancyDefaultOwner.ToString(), result.Settings.StoreCodePrefix));
        }).RequirePermission(PlatformPermissions.CompanyManage);
        stage22.MapPost("/business-groups/{businessId:guid}/companies", async (Guid businessId, AddBusinessCompanyRequest request, IStage22RegistryService service, CancellationToken ct) => { var membership = await service.AddCompanyAsync(businessId, request.CompanyId, ct); return Results.Created($"/api/v1/stage22/business-groups/{businessId}/companies/{request.CompanyId}", new { membership.Id, membership.CompanyId }); }).RequirePermission(PlatformPermissions.CompanyManage);
        stage22.MapPost("/business-groups/{businessId:guid}/type", async (Guid businessId, ChangeBusinessTypeRequest request, IStage22RegistryService service, CancellationToken ct) => { if (!Enum.TryParse<BusinessType>(request.BusinessType, true, out var type)) return Results.BadRequest("Unknown business type."); var business = await service.ChangeBusinessTypeAsync(businessId, type, ct); return Results.Ok(new { business.Id, Type = business.Type.ToString() }); }).RequirePermission(PlatformPermissions.CompanyManage);
        stage22.MapPost("/hierarchy-nodes", async (CreateHierarchyNodeRequest request, IStage22RegistryService service, CancellationToken ct) => { if (!Enum.TryParse<HierarchyNodeType>(request.NodeType, true, out var nodeType) || !Enum.TryParse<OwnershipType>(request.OwnershipType, true, out var ownership)) return Results.BadRequest("Unknown hierarchy node type or ownership."); var node = await service.AddHierarchyNodeAsync(request.BusinessId, request.CompanyId, nodeType, ownership, request.ParentNodeId, request.StoreCode, request.StockHolding, ct); return Results.Created($"/api/v1/stage22/hierarchy-nodes/{node.Id}", new Stage22HierarchyNodeResponse(node.Id, node.BusinessId, node.CompanyId, node.NodeType.ToString(), node.OwnershipType.ToString(), node.ParentNodeId, node.StoreCode, node.StockHolding)); }).RequirePermission(PlatformPermissions.CompanyManage);
        stage22.MapGet("/hierarchy-nodes/{businessId:guid}", async (Guid businessId, VumaRegistryDbContext db, ITenantContext tenant, CancellationToken ct) => Results.Ok(await db.GroupHierarchyNodes.AsNoTracking().Where(x => x.TenantId == tenant.TenantId && x.BusinessId == businessId).Select(x => new Stage22HierarchyNodeResponse(x.Id, x.BusinessId, x.CompanyId, x.NodeType.ToString(), x.OwnershipType.ToString(), x.ParentNodeId, x.StoreCode, x.StockHolding)).ToListAsync(ct))).RequirePermission(PlatformPermissions.CompanyView);
        stage22.MapPost("/premises-routing", async (AddPremisesSkuRoutingRequest request, IStage22RegistryService service, CancellationToken ct) =>
        {
            PremisesSkuRouting route = await service.AddPremisesSkuRoutingAsync(request.PremisesId, request.SkuOrBarcode, request.CompanyId, request.IsBarcode, ct);
            return Results.Created($"/api/v1/stage22/premises-routing/{route.Id}", new { route.Id, route.PremisesId, route.SkuOrBarcode, route.CompanyId, route.IsBarcode });
        }).RequirePermission(PlatformPermissions.CompanyManage);
        stage22.MapGet("/business-groups/{businessId:guid}/owned-stock", async (Guid businessId, Guid? companyId, IStage22RegistryService service, CancellationToken ct) =>
            Results.Ok((await service.ListOwnedStockAsync(businessId, companyId, ct)).Select(x => new Stage22OwnedStockResponse(x.Id, x.BusinessId, x.CompanyId, x.LocationId, x.ItemId, x.ItemVariantId, x.OnHand, x.Reserved, x.InStaging, x.Available, x.UnitOfMeasure, x.AsAt))))
            .RequirePermission(PlatformPermissions.CompanyView);
        stage22.MapPost("/transfers", async (CreateTransferRequest request, IStage22RegistryService service, CancellationToken ct) =>
        {
            IReadOnlyCollection<Stage22TransferLine>? lines = request.Lines?.Select(line => new Stage22TransferLine(
                line.ItemId, line.ItemVariantId, line.Quantity, line.UnitOfMeasure, line.SenderLocationId,
                line.ReceiverLocationId)).ToArray();
            var transfer = await service.CreateTransferAsync(request.RequesterCompanyId, request.SenderCompanyId, request.ReceiverCompanyId, request.HoldingCompanyId, request.TotalValue, request.CentralBuying, lines, ct);
            return Results.Created($"/api/v1/stage22/transfers/{transfer.Id}", new Stage22TransferResponse(transfer.Id, transfer.RequesterCompanyId, transfer.SenderCompanyId, transfer.ReceiverCompanyId, transfer.TotalValue, transfer.Status.ToString(), transfer.ReceivedQuantity, transfer.DiscrepancyQuantity));
        }).RequirePermission(PlatformPermissions.CompanyManage);
        stage22.MapPost("/transfers/{id:guid}/approve", async (Guid id, IStage22RegistryService service, CancellationToken ct) => Results.Ok(await service.TransitionTransferAsync(id, "approve", cancellationToken: ct))).RequirePermission(PlatformPermissions.CompanyManage);
        stage22.MapPost("/transfers/{id:guid}/accept", async (Guid id, IStage22RegistryService service, CancellationToken ct) => Results.Ok(await service.TransitionTransferAsync(id, "accept", cancellationToken: ct))).RequirePermission(PlatformPermissions.CompanyManage);
        stage22.MapPost("/transfers/{id:guid}/reserve", async (Guid id, IStage22RegistryService service, CancellationToken ct) => Results.Ok(await service.TransitionTransferAsync(id, "reserve", cancellationToken: ct))).RequirePermission(PlatformPermissions.CompanyManage);
        stage22.MapPost("/transfers/{id:guid}/pick", async (Guid id, IStage22RegistryService service, CancellationToken ct) => Results.Ok(await service.TransitionTransferAsync(id, "pick", cancellationToken: ct))).RequirePermission(PlatformPermissions.CompanyManage);
        stage22.MapPost("/transfers/{id:guid}/ship", async (Guid id, IStage22RegistryService service, CancellationToken ct) => Results.Ok(await service.TransitionTransferAsync(id, "ship", cancellationToken: ct))).RequirePermission(PlatformPermissions.CompanyManage);
        stage22.MapPost("/transfers/{id:guid}/in-transit", async (Guid id, IStage22RegistryService service, CancellationToken ct) => Results.Ok(await service.TransitionTransferAsync(id, "in-transit", cancellationToken: ct))).RequirePermission(PlatformPermissions.CompanyManage);
        stage22.MapPost("/transfers/{id:guid}/receive", async (Guid id, ReceiveTransferRequest request, IStage22RegistryService service, CancellationToken ct) => Results.Ok(await service.TransitionTransferAsync(id, "receive", quantity: request.Quantity, cancellationToken: ct))).RequirePermission(PlatformPermissions.CompanyManage);
        stage22.MapPost("/transfers/{id:guid}/reconcile", async (Guid id, ReconcileTransferRequest request, IStage22RegistryService service, CancellationToken ct) => Results.Ok(await service.TransitionTransferAsync(id, "reconcile", requestedQuantity: request.RequestedQuantity, reason: request.Reason, cancellationToken: ct))).RequirePermission(PlatformPermissions.CompanyManage);
        stage22.MapPost("/transfers/{id:guid}/remainder", async (Guid id, IStage22RegistryService service, CancellationToken ct) => Results.Created($"/api/v1/stage22/transfers", await service.TransitionTransferAsync(id, "remainder", cancellationToken: ct))).RequirePermission(PlatformPermissions.CompanyManage);
        stage22.MapPost("/transfers/{id:guid}/reverse", async (Guid id, IStage22RegistryService service, CancellationToken ct) => Results.Created($"/api/v1/stage22/transfers", await service.TransitionTransferAsync(id, "reverse", cancellationToken: ct))).RequirePermission(PlatformPermissions.CompanyManage);
        return endpoints;
    }
}
