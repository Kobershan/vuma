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
        group.MapPost("/", CreateAssetAsync).RequirePermission(AssetPermissions.Manage).Produces<Guid>(StatusCodes.Status201Created);
        group.MapPost("/{id:guid}/place-in-service", PlaceInServiceAsync).RequirePermission(AssetPermissions.Manage).Produces(StatusCodes.Status204NoContent);
        group.MapPost("/{id:guid}/dispose", DisposeAsync).RequirePermission(AssetPermissions.Manage).Produces(StatusCodes.Status204NoContent);
        group.MapPost("/{id:guid}/books", CreateBookAsync).RequirePermission(AssetPermissions.Manage).Produces<Guid>(StatusCodes.Status201Created);
        group.MapPost("/books/{id:guid}/depreciation", RunDepreciationAsync).RequirePermission(AssetPermissions.Manage).Produces<Guid>(StatusCodes.Status201Created);
        RouteGroupBuilder maintenance = endpoints.MapVumaApi().MapGroup("/maintenance/orders")
            .WithTags("Maintenance").RequireModule("assets");
        maintenance.MapPost("/", CreateMaintenanceAsync).RequirePermission(AssetPermissions.Manage).Produces<Guid>(StatusCodes.Status201Created);
        maintenance.MapPost("/{id:guid}/start", StartMaintenanceAsync).RequirePermission(AssetPermissions.Manage).Produces(StatusCodes.Status204NoContent);
        maintenance.MapPost("/{id:guid}/complete", CompleteMaintenanceAsync).RequirePermission(AssetPermissions.Manage).Produces(StatusCodes.Status204NoContent);
        maintenance.MapPost("/{id:guid}/cancel", CancelMaintenanceAsync).RequirePermission(AssetPermissions.Manage).Produces(StatusCodes.Status204NoContent);
        RouteGroupBuilder checklists = endpoints.MapVumaApi().MapGroup("/assets/checklists")
            .WithTags("Assets").RequireModule("assets");
        checklists.MapPost("/", CreateChecklistAsync).RequirePermission(AssetPermissions.Manage).Produces<Guid>(StatusCodes.Status201Created);
        checklists.MapPost("/{id:guid}/executions", SubmitChecklistAsync).RequirePermission(AssetPermissions.Manage).Produces<Guid>(StatusCodes.Status201Created);
        checklists.MapPost("/executions/{executionId:guid}/evidence/download", AuthorizeEvidenceDownloadAsync)
            .RequirePermission(AssetPermissions.View).Produces<ChecklistEvidenceDownloadResult>(StatusCodes.Status200OK);
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

    private static async Task<IResult> CreateMaintenanceAsync(CreateMaintenanceRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid id = await dispatcher.SendAsync(new CreateMaintenanceOrderCommand(request.CompanyId, request.AssetId,
            request.Description, request.ScheduledOn), cancellationToken).ConfigureAwait(false);
        return Results.Created($"/api/v1/maintenance/orders/{id:D}", id);
    }

    private static async Task<IResult> StartMaintenanceAsync(Guid id, AssetCompanyRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    { await dispatcher.SendAsync(new StartMaintenanceOrderCommand(request.CompanyId, id), cancellationToken).ConfigureAwait(false); return Results.NoContent(); }

    private static async Task<IResult> CompleteMaintenanceAsync(Guid id, AssetCompanyRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    { await dispatcher.SendAsync(new CompleteMaintenanceOrderCommand(request.CompanyId, id), cancellationToken).ConfigureAwait(false); return Results.NoContent(); }

    private static async Task<IResult> CancelMaintenanceAsync(Guid id, AssetCompanyRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    { await dispatcher.SendAsync(new CancelMaintenanceOrderCommand(request.CompanyId, id), cancellationToken).ConfigureAwait(false); return Results.NoContent(); }

    private static async Task<IResult> CreateChecklistAsync(CreateChecklistRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    { Guid id = await dispatcher.SendAsync(new CreateStoreChecklistCommand(request.CompanyId, request.StoreId, request.Code, request.Name, request.ItemCodes), cancellationToken).ConfigureAwait(false); return Results.Created($"/api/v1/assets/checklists/{id:D}", id); }

    private static async Task<IResult> SubmitChecklistAsync(Guid id, SubmitChecklistRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    { Guid executionId = await dispatcher.SendAsync(new SubmitChecklistExecutionCommand(request.CompanyId, request.StoreId, id, request.OperationId, request.DeviceId, request.CapturedAt, request.SubmittedAt, request.EvidenceReference), cancellationToken).ConfigureAwait(false); return Results.Created($"/api/v1/assets/checklists/{id:D}/executions/{executionId:D}", executionId); }

    private static async Task<IResult> AuthorizeEvidenceDownloadAsync(Guid executionId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        ChecklistEvidenceDownloadResult? result = await dispatcher.QueryAsync(new AuthorizeChecklistEvidenceDownloadQuery(executionId), cancellationToken).ConfigureAwait(false);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    public sealed record CreateAssetRequest(Guid CompanyId, string AssetNumber, string Description, DateOnly AcquiredOn, decimal Cost, string Currency);
    public sealed record AssetCompanyRequest(Guid CompanyId);
    public sealed record DisposeAssetRequest(Guid CompanyId, DateOnly DisposedOn);
    public sealed record CreateBookRequest(Guid CompanyId, string BookName, DateOnly InServiceOn, decimal ResidualValue, string Currency, int UsefulLifeMonths);
    public sealed record RunDepreciationRequest(Guid CompanyId, DateOnly Period);
    public sealed record CreateMaintenanceRequest(Guid CompanyId, Guid AssetId, string Description, DateOnly? ScheduledOn);
    public sealed record CreateChecklistRequest(Guid CompanyId, Guid? StoreId, string Code, string Name, IReadOnlyCollection<string> ItemCodes);
    public sealed record SubmitChecklistRequest(Guid CompanyId, Guid? StoreId, Guid OperationId, string DeviceId, DateTimeOffset CapturedAt, DateTimeOffset SubmittedAt, string EvidenceReference);
}
