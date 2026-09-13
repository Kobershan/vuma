#pragma warning disable CS1591
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
        storefront.MapPost("/baskets", OpenBasketAsync)
            .RequirePermission(VumaRetail.Application.Ecommerce.EcommercePermissions.Checkout)
            .Produces<Guid>(StatusCodes.Status201Created)
            .WithSummary("Opens a tenant-scoped storefront basket.");
        storefront.MapPost("/baskets/{id:guid}/lines", AddBasketLineAsync)
            .RequirePermission(VumaRetail.Application.Ecommerce.EcommercePermissions.Checkout)
            .Produces<Guid>(StatusCodes.Status201Created)
            .WithSummary("Adds a published product to an owned basket; submitted price is advisory.");
        storefront.MapPost("/checkouts", SubmitCheckoutAsync)
            .RequirePermission(VumaRetail.Application.Ecommerce.EcommercePermissions.Checkout)
            .Produces<CheckoutAcceptedResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Submits a durable checkout intent; store confirmation remains pending.");
        storefront.MapGet("/checkouts/{id:guid}", GetCheckoutStatusAsync)
            .RequirePermission(VumaRetail.Application.Ecommerce.EcommercePermissions.Checkout)
            .Produces<CheckoutStatusResult>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Reads checkout status for its owning customer.");
        storefront.MapPost("/webhooks/payments", ApplyPaymentWebhookAsync)
            .AllowAnonymous()
            .Produces<Guid>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithSummary("Accepts a signed, replay-safe payment provider notification.");

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

    private static async Task<IResult> OpenBasketAsync(
        OpenBasketRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid id = await dispatcher.SendAsync(new OpenBasketCommand(request.ChannelId, request.CompanyId, request.OwnerKey), cancellationToken).ConfigureAwait(false);
        return Results.Created($"/api/v1/storefront/baskets/{id:D}", id);
    }

    private static async Task<IResult> AddBasketLineAsync(
        Guid id, AddBasketLineRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid lineId = await dispatcher.SendAsync(new AddBasketLineCommand(id, request.CompanyId, request.OwnerKey,
            request.PublishedProductId, request.Quantity, request.AdvisoryUnitPrice, request.Currency), cancellationToken).ConfigureAwait(false);
        return Results.Created($"/api/v1/storefront/baskets/{id:D}/lines/{lineId:D}", lineId);
    }

    private static async Task<IResult> SubmitCheckoutAsync(
        SubmitCheckoutRequest request, HttpRequest http, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        string idempotencyKey = http.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return Results.BadRequest(new { error = "Idempotency-Key is required." });
        Guid id = await dispatcher.SendAsync(new SubmitCheckoutCommand(request.BasketId, request.CompanyId,
            request.OwnerKey, idempotencyKey, request.ContentFingerprint), cancellationToken).ConfigureAwait(false);
        return Results.Accepted($"/api/v1/storefront/checkouts/{id:D}", new CheckoutAcceptedResponse(id, "Pending", DateTimeOffset.UtcNow.AddHours(24)));
    }

    private static async Task<IResult> GetCheckoutStatusAsync(
        Guid id, [AsParameters] GetCheckoutStatusRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        CheckoutStatusResult? result = await dispatcher.QueryAsync(
            new GetCheckoutStatusQuery(id, request.CompanyId, request.OwnerKey), cancellationToken).ConfigureAwait(false);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    private static async Task<IResult> ApplyPaymentWebhookAsync(
        HttpRequest http, IConfiguration configuration, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        using StreamReader reader = new(http.Body);
        string body = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        string secret = configuration["Vuma:Ecommerce:PaymentWebhookSecret"] ?? string.Empty;
        if (!PaymentWebhookSecurity.Verify(body, http.Headers["X-Vuma-Payment-Signature"].ToString(), secret))
            return Results.Unauthorized();
        PaymentWebhookRequest? request = JsonSerializer.Deserialize<PaymentWebhookRequest>(body);
        if (request is null)
            return Results.BadRequest();
        string fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(body)));
        Guid id = await dispatcher.SendAsync(new ApplyPaymentNotificationCommand(request.CheckoutId, request.CompanyId,
            request.EventId, fingerprint, request.ProviderPaymentId, request.Status, request.ProviderReference), cancellationToken).ConfigureAwait(false);
        return Results.Accepted($"/api/v1/storefront/checkouts/{request.CheckoutId:D}", id);
    }

    public sealed record RegisterChannelRequest(Guid CompanyId, string Code, string Host);
    public sealed record PublishProductRequest(Guid CompanyId, Guid? ItemId, Guid? ItemVariantId, string Sku, string Name,
        string? Description, decimal Price, string Currency, decimal Available, DateTimeOffset AvailabilityAsAt, int Version);
    public sealed record OpenBasketRequest(Guid ChannelId, Guid CompanyId, string OwnerKey);
    public sealed record AddBasketLineRequest(Guid CompanyId, string OwnerKey, Guid PublishedProductId,
        decimal Quantity, decimal AdvisoryUnitPrice, string Currency);
    public sealed record SubmitCheckoutRequest(Guid BasketId, Guid CompanyId, string OwnerKey, string ContentFingerprint);
    public sealed record CheckoutAcceptedResponse(Guid OperationId, string Status, DateTimeOffset ExpiresAt);
    public sealed record GetCheckoutStatusRequest(Guid CompanyId, string OwnerKey);
    public sealed record PaymentWebhookRequest(Guid CheckoutId, Guid CompanyId, string EventId,
        string ProviderPaymentId, string Status, string? ProviderReference);
}
