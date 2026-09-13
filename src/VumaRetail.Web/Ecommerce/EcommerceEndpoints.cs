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
        return endpoints;
    }

    private static async Task<IResult> ListProductsAsync(
        HttpRequest request, int? limit, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        IReadOnlyList<StorefrontProductResult> products = await dispatcher.QueryAsync(
            new ListStorefrontProductsQuery(request.Host.Host, limit ?? 50), cancellationToken).ConfigureAwait(false);
        return Results.Ok(products);
    }
}
