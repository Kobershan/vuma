#pragma warning disable CS1591
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Connect;
using VumaRetail.Domain.Connect;
using VumaRetail.Web.Api;
using VumaRetail.Web.Licensing;

namespace VumaRetail.Web.Connect;

public static class ConnectEndpoints
{
    public static IEndpointRouteBuilder MapVumaConnect(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder api = endpoints.MapVumaApi().MapGroup("/connect")
            .WithTags("Vuma Connect")
            .WithDescription("Supplier portal and retailer trading API. Every query is connection-scoped and every write requires the corresponding Connect permission.")
            .RequireModule("connect");
        api.MapGet("/connections", async (IDispatcher d, CancellationToken ct) => TypedResults.Ok(await d.QueryAsync(new ListConnectConnectionsQuery(), ct))).RequirePermission(ConnectPermissions.View);
        api.MapGet("/suppliers", async (IDispatcher d, CancellationToken ct) => TypedResults.Ok(await d.QueryAsync(new ListConnectSuppliersQuery(), ct))).RequirePermission(ConnectPermissions.View);
        api.MapPost("/connections/codes", async (IssueCodeRequest r, IDispatcher d, CancellationToken ct) => TypedResults.Ok(await d.SendAsync(new IssueConnectionCodeCommand(r.Code, r.Uses, r.ExpiresAt, r.PriceTier, r.Territory, r.GrantsPortalAccess), ct))).RequirePermission(ConnectPermissions.Manage);
        api.MapPost("/connections/redeem", async (RedeemCodeRequest r, IDispatcher d, CancellationToken ct) => TypedResults.Created("/api/v1/connect/connections", await d.SendAsync(new RedeemConnectionCodeCommand(r.Code, r.RetailerAccountReference), ct))).RequirePermission(ConnectPermissions.Manage);
        api.MapPost("/connections/{id:guid}/accept", async (Guid id, AcceptConnectionRequest r, IDispatcher d, CancellationToken ct) => TypedResults.Ok(await d.SendAsync(new AcceptTradingConnectionCommand(id, r.Currency, r.CreditLimit, r.LeadTimeDays, r.MinimumOrderValue), ct))).RequirePermission(ConnectPermissions.Manage);
        api.MapPost("/connections/{id:guid}/suspend", async (Guid id, IDispatcher d, CancellationToken ct) => { await d.SendAsync(new SuspendTradingConnectionCommand(id), ct); return TypedResults.NoContent(); }).RequirePermission(ConnectPermissions.Manage);
        api.MapPost("/connections/{id:guid}/end", async (Guid id, IDispatcher d, CancellationToken ct) => { await d.SendAsync(new EndTradingConnectionCommand(id), ct); return TypedResults.NoContent(); }).RequirePermission(ConnectPermissions.Manage);

        api.MapPost("/catalogue/publish", async (PublishCatalogueRequest r, IDispatcher d, CancellationToken ct) => TypedResults.Created($"/api/v1/connect/catalogue/{await d.SendAsync(new PublishCatalogueCommand(r.ConnectionId, r.Version, r.EffectiveFrom, r.VersionNote, r.Lines.Select(x => new PublishCatalogueLine(x.SupplierSku, x.Description, x.Barcode, x.PackSize, x.MinimumOrderQuantity, x.LeadTimeDays)).ToList()), ct)}", new { })).RequirePermission(ConnectPermissions.Publish);
        api.MapGet("/catalogue/{id:guid}", async (Guid id, IDispatcher d, CancellationToken ct) => { ConnectPublicationResult? value = await d.QueryAsync(new GetConnectPublicationQuery(id), ct); return value is null ? Results.NotFound() : Results.Ok(value); }).RequirePermission(ConnectPermissions.View);
        api.MapPost("/price-lists/publish", async (PublishPriceRequest r, IDispatcher d, CancellationToken ct) => TypedResults.Created($"/api/v1/connect/price-lists/{await d.SendAsync(new PublishPriceProposalCommand(r.ConnectionId, r.EffectiveFrom, r.ExpiresAt, r.VersionNote, r.Lines.Select(x => new PublishPriceLine(x.SupplierSku, x.UnitPrice, x.Currency, x.MinimumOrderQuantity, x.LeadTimeDays)).ToList()), ct)}", new { })).RequirePermission(ConnectPermissions.Publish);
        api.MapGet("/price-lists/incoming", async (Guid connectionId, IDispatcher d, CancellationToken ct) => TypedResults.Ok(await d.QueryAsync(new ListIncomingPriceProposalsQuery(connectionId), ct))).RequirePermission(ConnectPermissions.View);
        api.MapPost("/price-lists/{id:guid}/decide", async (Guid id, DecidePriceRequest r, IDispatcher d, CancellationToken ct) => { await d.SendAsync(new DecidePriceProposalCommand(id, r.Accept, r.LineIds, r.Reason), ct); return TypedResults.NoContent(); }).RequirePermission(ConnectPermissions.Decide);
        api.MapPost("/price-lists/{id:guid}/rollback", async (Guid id, IDispatcher d, CancellationToken ct) => { await d.SendAsync(new RollbackPriceProposalCommand(id), ct); return TypedResults.NoContent(); }).RequirePermission(ConnectPermissions.Publish);
        api.MapPost("/orders", async (PlaceConnectOrderRequest r, IDispatcher d, CancellationToken ct) => TypedResults.Created($"/api/v1/connect/orders/{await d.SendAsync(new PlaceConnectOrderCommand(r.ConnectionId, r.PurchaseOrderId, r.OrderNumber, r.Lines.Select(x => new ConnectOrderLineInput(x.SupplierSku, x.Description, x.Quantity, x.UnitOfMeasure, x.UnitPrice, x.Currency)).ToList()), ct)}", new { })).RequirePermission(ConnectPermissions.Order);
        api.MapGet("/orders", async (Guid? connectionId, IDispatcher d, CancellationToken ct) => TypedResults.Ok(await d.QueryAsync(new ListConnectOrdersQuery(connectionId), ct))).RequirePermission(ConnectPermissions.View);
        api.MapPost("/orders/{id:guid}/confirm", async (Guid id, ConfirmConnectOrderRequest r, IDispatcher d, CancellationToken ct) => { await d.SendAsync(new ConfirmConnectOrderCommand(id, r.Quantities, r.PromisedAt), ct); return TypedResults.NoContent(); }).RequirePermission(ConnectPermissions.Order);
        api.MapPost("/orders/{id:guid}/reject", async (Guid id, RejectConnectOrderRequest r, IDispatcher d, CancellationToken ct) => { await d.SendAsync(new RejectConnectOrderCommand(id, r.Reason), ct); return TypedResults.NoContent(); }).RequirePermission(ConnectPermissions.Order);
        api.MapPost("/orders/{id:guid}/dispatch", async (Guid id, DispatchConnectOrderRequest r, IDispatcher d, CancellationToken ct) => { await d.SendAsync(new DispatchConnectOrderCommand(id, r.DispatchNoteNumber, r.Quantities, r.DispatchedAt), ct); return TypedResults.NoContent(); }).RequirePermission(ConnectPermissions.Order);
        api.MapPost("/orders/{id:guid}/receive", async (Guid id, IDispatcher d, CancellationToken ct) => { await d.SendAsync(new ReceiveConnectOrderCommand(id), ct); return TypedResults.NoContent(); }).RequirePermission(ConnectPermissions.Order);
        api.MapPost("/payments/settle", async (SettlePaymentRequest r, IDispatcher d, CancellationToken ct) => TypedResults.Ok(await d.SendAsync(new SettleConnectPaymentCommand(r.PaymentId, r.ConnectionId, r.InvoiceReference, r.Amount, r.Currency, r.Method), ct))).RequirePermission(ConnectPermissions.Order);
        api.MapPost("/orders/{id:guid}/claims", async (Guid id, RaiseClaimRequest r, IDispatcher d, CancellationToken ct) => TypedResults.Created($"/api/v1/connect/claims/{await d.SendAsync(new RaiseConnectClaimCommand(id, r.OrderLineId, r.ClaimNumber, r.Reason, r.Quantity, r.UnitOfMeasure, r.Amount, r.Currency, r.Description), ct)}", new { })).RequirePermission(ConnectPermissions.Order);
        api.MapPost("/claims/{id:guid}/resolve", async (Guid id, ResolveClaimRequest r, IDispatcher d, CancellationToken ct) => { await d.SendAsync(new ResolveConnectClaimCommand(id, r.Credit, r.CreditNoteReference), ct); return TypedResults.NoContent(); }).RequirePermission(ConnectPermissions.Order);
        return endpoints;
    }

    public sealed record IssueCodeRequest(string Code, int Uses, DateTimeOffset ExpiresAt, string? PriceTier, string? Territory, bool GrantsPortalAccess);
    public sealed record RedeemCodeRequest(string Code, string? RetailerAccountReference);
    public sealed record AcceptConnectionRequest(string Currency, decimal CreditLimit, int LeadTimeDays, decimal MinimumOrderValue);
    public sealed record PublishCatalogueRequest(Guid ConnectionId, int Version, DateTimeOffset EffectiveFrom, string? VersionNote, IReadOnlyList<PublishCatalogueLineRequest> Lines);
    public sealed record PublishCatalogueLineRequest(string SupplierSku, string Description, string? Barcode, int PackSize, decimal? MinimumOrderQuantity, int? LeadTimeDays);
    public sealed record PublishPriceRequest(Guid ConnectionId, DateTimeOffset EffectiveFrom, DateTimeOffset? ExpiresAt, string? VersionNote, IReadOnlyList<PublishPriceLineRequest> Lines);
    public sealed record PublishPriceLineRequest(string SupplierSku, decimal UnitPrice, string Currency, decimal? MinimumOrderQuantity, int? LeadTimeDays);
    public sealed record DecidePriceRequest(bool Accept, IReadOnlyCollection<Guid>? LineIds, string? Reason);
    public sealed record PlaceConnectOrderRequest(Guid ConnectionId, Guid PurchaseOrderId, string OrderNumber, IReadOnlyList<PlaceConnectOrderLineRequest> Lines);
    public sealed record PlaceConnectOrderLineRequest(string SupplierSku, string Description, decimal Quantity, string UnitOfMeasure, decimal UnitPrice, string Currency);
    public sealed record ConfirmConnectOrderRequest(IReadOnlyDictionary<Guid, decimal> Quantities, DateTimeOffset PromisedAt);
    public sealed record RejectConnectOrderRequest(string Reason);
    public sealed record DispatchConnectOrderRequest(string DispatchNoteNumber, IReadOnlyDictionary<Guid, decimal> Quantities, DateTimeOffset DispatchedAt);
    public sealed record SettlePaymentRequest(Guid PaymentId, Guid ConnectionId, string InvoiceReference, decimal Amount, string Currency, ConnectPaymentMethod Method);
    public sealed record RaiseClaimRequest(Guid OrderLineId, string ClaimNumber, ConnectClaimReason Reason, decimal Quantity, string UnitOfMeasure, decimal Amount, string Currency, string Description);
    public sealed record ResolveClaimRequest(bool Credit, string? CreditNoteReference);
}
