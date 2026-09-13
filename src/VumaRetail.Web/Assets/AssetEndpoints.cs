#pragma warning disable CS1591
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Assets;
using VumaRetail.Web.Api;
using VumaRetail.Web.Licensing;

namespace VumaRetail.Web.Assets;

public static class AssetEndpoints
{
    public static IEndpointRouteBuilder MapVumaAssets(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapVumaApi().MapGroup("/assets")
            .WithTags("Assets").RequireModule("assets");
        group.MapPost("/", CreateAssetAsync).RequirePermission("assets.manage").Produces<Guid>(StatusCodes.Status201Created);
        group.MapPost("/{id:guid}/place-in-service", PlaceInServiceAsync).RequirePermission("assets.manage").Produces(StatusCodes.Status204NoContent);
        group.MapPost("/{id:guid}/dispose", DisposeAsync).RequirePermission("assets.manage").Produces(StatusCodes.Status204NoContent);
        group.MapPost("/{id:guid}/books", CreateBookAsync).RequirePermission("assets.manage").Produces<Guid>(StatusCodes.Status201Created);
        group.MapPost("/books/{id:guid}/depreciation", RunDepreciationAsync).RequirePermission("assets.manage").Produces<Guid>(StatusCodes.Status201Created);
        return endpoints;
    }

    private static async Task<IResult> CreateAssetAsync(CreateAssetRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid id = await dispatcher.SendAsync(new CreateFixedAssetCommand(request.CompanyId, request.AssetNumber,
            request.Description, request.AcquiredOn, request.Cost, request.Currency), cancellationToken).ConfigureAwait(false);
        return Results.Created($"/api/v1/assets/{id:D}", id);
    }

    private static async Task<IResult> PlaceInServiceAsync(Guid id, AssetCompanyRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new PlaceAssetInServiceCommand(request.CompanyId, id), cancellationToken).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> DisposeAsync(Guid id, DisposeAssetRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new DisposeFixedAssetCommand(request.CompanyId, id, request.DisposedOn), cancellationToken).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> CreateBookAsync(Guid id, CreateBookRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid bookId = await dispatcher.SendAsync(new CreateAssetBookCommand(request.CompanyId, id, request.BookName,
            request.InServiceOn, request.ResidualValue, request.Currency, request.UsefulLifeMonths), cancellationToken).ConfigureAwait(false);
        return Results.Created($"/api/v1/assets/{id:D}/books/{bookId:D}", bookId);
    }

    private static async Task<IResult> RunDepreciationAsync(Guid id, RunDepreciationRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid runId = await dispatcher.SendAsync(new RunDepreciationCommand(request.CompanyId, id, request.Period), cancellationToken).ConfigureAwait(false);
        return Results.Created($"/api/v1/assets/books/{id:D}/depreciation/{runId:D}", runId);
    }

    public sealed record CreateAssetRequest(Guid CompanyId, string AssetNumber, string Description, DateOnly AcquiredOn, decimal Cost, string Currency);
    public sealed record AssetCompanyRequest(Guid CompanyId);
    public sealed record DisposeAssetRequest(Guid CompanyId, DateOnly DisposedOn);
    public sealed record CreateBookRequest(Guid CompanyId, string BookName, DateOnly InServiceOn, decimal ResidualValue, string Currency, int UsefulLifeMonths);
    public sealed record RunDepreciationRequest(Guid CompanyId, DateOnly Period);
}
