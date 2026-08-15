using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Catalog.Commands;
using VumaRetail.Application.Catalog.Permissions;
using VumaRetail.Application.Catalog.Queries;
using VumaRetail.Contracts;
using VumaRetail.Contracts.Catalog;
using VumaRetail.Domain.Catalog;
using VumaRetail.Web.Api;

namespace VumaRetail.Web.Catalog;

/// <summary>
/// The <c>catalog</c> module's endpoints: units of measure, items, variants and barcodes.
/// </summary>
/// <remarks>
/// R3: nothing exists in a UI before it exists here. <c>Item</c> and <c>ItemVariant</c> share one
/// resource — a variant is <c>POST /items/{id}/variants</c>, not its own top-level collection — because
/// a variant cannot exist without its parent and the domain enforces that (<c>Item.AddVariant</c>).
/// </remarks>
public static class CatalogEndpoints
{
    /// <summary>Maps the catalog endpoints under the current API version.</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapVumaCatalog(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder api = endpoints.MapVumaApi();

        RouteGroupBuilder units = api.MapGroup("/catalog/units-of-measure").WithTags("Catalog");

        units.MapGet("/", ListUnitsOfMeasureAsync)
            .RequirePermission(CatalogPermissions.ItemView)
            .Produces<IReadOnlyList<UnitOfMeasureResponse>>()
            .WithSummary("Every unit of measure in the tenant.")
            .WithDescription("Not paginated — a tenant's units of measure are configuration, not a catalogue.");

        units.MapPost("/", CreateUnitOfMeasureAsync)
            .RequirePermission(CatalogPermissions.UnitOfMeasureManage)
            .Produces<UnitOfMeasureIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Defines a base unit, or one that converts to an existing base unit.");

        RouteGroupBuilder items = api.MapGroup("/catalog/items").WithTags("Catalog");

        items.MapGet("/", ListItemsAsync)
            .RequirePermission(CatalogPermissions.ItemView)
            .Produces<PageResponse<ItemResponse>>()
            .WithSummary("Items, a keyset-paginated page at a time (docs/API_STANDARDS.md §8).");

        items.MapGet("/{itemId:guid}", GetItemAsync)
            .RequirePermission(CatalogPermissions.ItemView)
            .Produces<ItemResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Reads one item by id.");

        items.MapPost("/", CreateItemAsync)
            .RequirePermission(CatalogPermissions.ItemCreate)
            .Produces<ItemIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Creates an item. An item with no variants may carry a barcode directly.");

        items.MapPut("/{itemId:guid}", UpdateItemAsync)
            .RequirePermission(CatalogPermissions.ItemUpdate)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Updates an item's name, description and tax class.");

        items.MapPost("/{itemId:guid}/deactivate", DeactivateItemAsync)
            .RequirePermission(CatalogPermissions.ItemDeactivate)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Retires an item from sale and receipt. History is retained; nothing is deleted.");

        items.MapPost("/{itemId:guid}/variants", CreateItemVariantAsync)
            .RequirePermission(CatalogPermissions.ItemCreate)
            .Produces<ItemVariantIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Adds a variant. The item now sells only through its variants.")
            .WithDescription(
                "Refused with 422 if the item already carries a barcode of its own — remove it first.");

        RouteGroupBuilder barcodes = api.MapGroup("/catalog/barcodes").WithTags("Catalog");

        barcodes.MapPost("/", AddBarcodeAsync)
            .RequirePermission(CatalogPermissions.BarcodeManage)
            .Produces<BarcodeIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Attaches a barcode to an item or a variant. A scanner reads a code, not a type.")
            .WithDescription(
                "Refused with 422 if the target item already has a variant — attach the barcode to the "
                + "variant instead.");

        barcodes.MapPost("/{barcodeId:guid}/primary", SetPrimaryBarcodeAsync)
            .RequirePermission(CatalogPermissions.BarcodeManage)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Makes a barcode its owner's primary, demoting whichever one held that place before.");

        barcodes.MapDelete("/{barcodeId:guid}", RemoveBarcodeAsync)
            .RequirePermission(CatalogPermissions.BarcodeManage)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Removes a barcode outright — the one deliberate hard delete in this module.");

        return endpoints;
    }

    private static async Task<IResult> ListUnitsOfMeasureAsync(IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        IReadOnlyList<UnitOfMeasureResult> units = await dispatcher
            .QueryAsync(new ListUnitsOfMeasureQuery(), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<UnitOfMeasureResponse>>([.. units.Select(ToResponse)]);
    }

    private static async Task<IResult> CreateUnitOfMeasureAsync(
        CreateUnitOfMeasureRequest request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        UnitOfMeasureType type = ParseEnum<UnitOfMeasureType>(
            request.Type, nameof(request.Type), nameof(CreateUnitOfMeasureCommand));

        Guid id = await dispatcher
            .SendAsync(
                new CreateUnitOfMeasureCommand(
                    request.Code,
                    request.Name,
                    type,
                    request.BaseUnitOfMeasureId,
                    request.ConversionFactorToBase),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/catalog/units-of-measure/{id}", new UnitOfMeasureIdResponse(id));
    }

    private static async Task<IResult> ListItemsAsync(
        IDispatcher dispatcher,
        CancellationToken cancellationToken,
        int? limit = null,
        string? after = null)
    {
        PageResult<ItemResult> page = await dispatcher
            .QueryAsync(new ListItemsQuery(limit, after), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new PageResponse<ItemResponse>(
            [.. page.Items.Select(ToResponse)],
            page.NextCursor,
            page.HasMore));
    }

    private static async Task<IResult> GetItemAsync(Guid itemId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        ItemResult item = await dispatcher.QueryAsync(new GetItemQuery(itemId), cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(ToResponse(item));
    }

    private static async Task<IResult> CreateItemAsync(
        CreateItemRequest request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        ItemType itemType = ParseEnum<ItemType>(request.ItemType, nameof(request.ItemType), nameof(CreateItemCommand));

        Guid id = await dispatcher
            .SendAsync(
                new CreateItemCommand(
                    request.Code,
                    request.Name,
                    itemType,
                    request.UnitOfMeasureId,
                    request.Description,
                    request.TaxClassCode),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/catalog/items/{id}", new ItemIdResponse(id));
    }

    private static async Task<IResult> UpdateItemAsync(
        Guid itemId,
        UpdateItemDetailsRequest request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        await dispatcher
            .SendAsync(
                new UpdateItemDetailsCommand(itemId, request.Name, request.Description, request.TaxClassCode),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> DeactivateItemAsync(Guid itemId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new DeactivateItemCommand(itemId), cancellationToken).ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> CreateItemVariantAsync(
        Guid itemId,
        CreateItemVariantRequest request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<VariantAttribute> attributes =
            [.. request.Attributes.Select(attribute => VariantAttribute.Create(attribute.Name, attribute.Value))];

        Guid variantId = await dispatcher
            .SendAsync(new CreateItemVariantCommand(itemId, request.Sku, attributes), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/catalog/items/{itemId}/variants/{variantId}", new ItemVariantIdResponse(variantId));
    }

    private static async Task<IResult> AddBarcodeAsync(AddBarcodeRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        BarcodeSymbology symbology = ParseEnum<BarcodeSymbology>(
            request.Symbology, nameof(request.Symbology), nameof(AddBarcodeCommand));

        Guid id = await dispatcher
            .SendAsync(
                new AddBarcodeCommand(request.ItemId, request.ItemVariantId, request.Code, symbology, request.IsPrimary),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/catalog/barcodes/{id}", new BarcodeIdResponse(id));
    }

    private static async Task<IResult> SetPrimaryBarcodeAsync(Guid barcodeId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new SetPrimaryBarcodeCommand(barcodeId), cancellationToken).ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> RemoveBarcodeAsync(Guid barcodeId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new RemoveBarcodeCommand(barcodeId), cancellationToken).ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static UnitOfMeasureResponse ToResponse(UnitOfMeasureResult unit) => new(
        unit.Id,
        unit.Code,
        unit.Name,
        unit.Type.ToString(),
        unit.BaseUnitOfMeasureId,
        unit.ConversionFactorToBase,
        unit.IsActive);

    private static ItemResponse ToResponse(ItemResult item) => new(
        item.Id,
        item.Code,
        item.Name,
        item.Description,
        item.ItemType.ToString(),
        item.UnitOfMeasureId,
        item.TaxClassCode,
        item.IsActive,
        item.HasVariants);

    /// <summary>Parses an enum from a request field, refusing with a 400 rather than a 500 on a typo.</summary>
    private static TEnum ParseEnum<TEnum>(string value, string propertyName, string messageName)
        where TEnum : struct, Enum
    {
        if (Enum.TryParse(value, ignoreCase: true, out TEnum parsed) && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        throw new ValidationFailedException(
            messageName,
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [propertyName] = [$"'{value}' is not one of: {string.Join(", ", Enum.GetNames<TEnum>())}."],
            });
    }
}
