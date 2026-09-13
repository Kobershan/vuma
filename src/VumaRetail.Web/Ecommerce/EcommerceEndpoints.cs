#pragma warning disable CS1591
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Ecommerce;
using VumaRetail.Web.Api;
using VumaRetail.Web.Licensing;

namespace VumaRetail.Web.Ecommerce;

/// <summary>Stage 21 storefront catalogue endpoints.</summary>
public static class EcommerceEndpoints
{
    public static IEndpointRouteBuilder MapVumaEcommerce(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder storefront = endpoints.MapVumaApi().MapGroup("/storefront")
            .WithTags("Storefront").RequireModule("ecommerce");

        storefront.MapGet("/products", ListProductsAsync)
            .RequirePermission(VumaRetail.Application.Ecommerce.EcommercePermissions.View)
            .Produces<IReadOnlyList<StorefrontProductResult>>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Lists published products for the registered storefront host.");

        RouteGroupBuilder channels = endpoints.MapVumaApi().MapGroup("/channels")
            .WithTags("Storefront Channels").RequireModule("ecommerce");
        channels.MapPost("", RegisterChannelAsync)
            .RequirePermission(VumaRetail.Application.Ecommerce.EcommercePermissions.Manage)
            .Produces<Guid>(StatusCodes.Status201Created)
            .WithSummary("Registers an active storefront channel.");
        channels.MapPost("/{id:guid}/products", PublishProductAsync)
            .RequirePermission(VumaRetail.Application.Ecommerce.EcommercePermissions.Manage)
            .Produces<Guid>(StatusCodes.Status201Created)
            .WithSummary("Publishes one sell-facing product version to a storefront channel.");
        return endpoints;
    }

    private static async Task<IResult> ListProductsAsync(
        HttpRequest request, int? limit, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        IReadOnlyList<StorefrontProductResult> products = await dispatcher.QueryAsync(
            new ListStorefrontProductsQuery(request.Host.Host, limit ?? 50), cancellationToken).ConfigureAwait(false);
        return Results.Ok(products);
    }

    private static async Task<IResult> RegisterChannelAsync(
        RegisterChannelRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid id = await dispatcher.SendAsync(new RegisterChannelCommand(request.CompanyId, request.Code, request.Host), cancellationToken).ConfigureAwait(false);
        return Results.Created($"/api/v1/channels/{id:D}", id);
    }

    private static async Task<IResult> PublishProductAsync(
        Guid id, PublishProductRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid productId = await dispatcher.SendAsync(new PublishProductCommand(id, request.CompanyId, request.ItemId,
            request.ItemVariantId, request.Sku, request.Name, request.Description, request.Price, request.Currency,
            request.Available, request.AvailabilityAsAt, request.Version), cancellationToken).ConfigureAwait(false);
        return Results.Created($"/api/v1/storefront/products/{productId:D}", productId);
    }

    public sealed record RegisterChannelRequest(Guid CompanyId, string Code, string Host);
    public sealed record PublishProductRequest(Guid CompanyId, Guid? ItemId, Guid? ItemVariantId, string Sku, string Name,
        string? Description, decimal Price, string Currency, decimal Available, DateTimeOffset AvailabilityAsAt, int Version);
}
