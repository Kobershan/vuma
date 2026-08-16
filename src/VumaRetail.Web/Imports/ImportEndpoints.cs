using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Imports;
using VumaRetail.Application.Imports.Commands;
using VumaRetail.Application.Imports.Permissions;
using VumaRetail.Application.Imports.Queries;
using VumaRetail.Contracts;
using VumaRetail.Contracts.Imports;
using VumaRetail.Domain.Imports;
using VumaRetail.Web.Api;

namespace VumaRetail.Web.Imports;

/// <summary>
/// The <c>imports</c> module's endpoints: upload, map, preview, commit, roll back, and the saved
/// mappings.
/// </summary>
/// <remarks>
/// <para>
/// R3: nothing exists in a UI before it exists here. The whole import screen — the drop zone, the
/// column-mapping grid, the preview table with its bad rows filtered, the commit button and the "undo
/// this import" the person reaches for a day later — is one of these thirteen calls.
/// </para>
/// <para>
/// <b>The pipeline is expressed as state transitions, not as CRUD.</b> Uploading is a <c>POST</c> that
/// creates a batch; mapping is a <c>PUT</c>, because setting a mapping twice with the same bindings
/// leaves the same batch; and validate, commit, roll back and discard are each a <c>POST</c> to a
/// named sub-resource, because each is a thing that happens to the batch rather than a field on it.
/// There is no <c>DELETE</c> anywhere: a batch that has not committed is discarded and a committed one
/// is rolled back, and both leave the record of what happened (§7 rule 8).
/// </para>
/// <para>
/// <b>The batch id is the commit's idempotency key</b> (<c>API_STANDARDS.md</c> §9). A person whose
/// browser timed out on a forty-thousand-row commit will press the button again, and the second press
/// has to be the same commit rather than a second one — so committing an already-committed batch
/// replays the first commit's counters instead of refusing. A row lock taken inside the transaction
/// is what makes that true for two callers pressing at the same instant rather than one after the
/// other.
/// </para>
/// <para>
/// <b>The three read endpoints are separate for a reason each.</b> The batch does not carry its rows,
/// because a fifty-thousand-row file must not be one response; the preview pages them and filters by
/// status, because nobody scrolls forty thousand rows looking for eleven bad ones; and the target
/// catalogue is its own call because a mapping screen needs it before any file exists.
/// </para>
/// </remarks>
public static class ImportEndpoints
{
    /// <summary>Maps the imports endpoints under the current API version.</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapVumaImports(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder api = endpoints.MapVumaApi();
        RouteGroupBuilder imports = api.MapGroup("/imports").WithTags("Imports");

        imports.MapGet("/targets", ListTargetsAsync)
            .RequirePermission(ImportsPermissions.BatchView)
            .Produces<IReadOnlyList<ImportTargetResponse>>()
            .WithSummary("Every import target and its field catalogue.")
            .WithDescription(
                "What a mapping screen builds itself from: the field names, their types, which are "
                + "required, which together identify a row, an example of each, and the header texts "
                + "each field is matched against. Nothing about the import UI needs to be hard-coded.");

        RouteGroupBuilder batches = api.MapGroup("/imports/batches").WithTags("Imports");

        batches.MapPost("/", CreateBatchAsync)
            .RequirePermission(ImportsPermissions.BatchCreate)
            .Produces<CreateImportBatchResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Uploads a file, reads it, and maps it if it can.")
            .WithDescription(
                "Writes nothing outside the imports schema. 400 when the bytes are not the format "
                + "declared — including a scanned PDF, which is refused as IMPORTS_PDF_HAS_NO_TEXT_LAYER "
                + "rather than imported as zero rows. 409 when these exact bytes were already "
                + "committed by another batch, matched on content rather than on file name. The "
                + "response says whether a saved template or the header aliases mapped it; where "
                + "neither could, it names the required fields still bound to nothing.");

        batches.MapGet("/", ListBatchesAsync)
            .RequirePermission(ImportsPermissions.BatchView)
            .Produces<PageResponse<ImportBatchResponse>>()
            .WithSummary("The import history, a keyset-paginated page at a time.")
            .WithDescription("Rows are deliberately not loaded — a list of imports is a list of files.");

        batches.MapGet("/{batchId:guid}", GetBatchAsync)
            .RequirePermission(ImportsPermissions.BatchView)
            .Produces<ImportBatchResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("One batch, with its mapping and its counters.");

        batches.MapGet("/{batchId:guid}/preview", GetPreviewAsync)
            .RequirePermission(ImportsPermissions.BatchView)
            .Produces<PageResponse<ImportRowResponse>>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("The rows, in file order — the preview R5 promises.")
            .WithDescription(
                "Pass status=Invalid to see only the rows that will not import. Each row carries its "
                + "line number as the person's spreadsheet shows it, what the file actually said, what "
                + "that parsed to, and every reason it will not apply — not just the first.");

        batches.MapPut("/{batchId:guid}/mapping", SetMappingAsync)
            .RequirePermission(ImportsPermissions.BatchCreate)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Binds the target's fields to the file's columns.")
            .WithDescription(
                "A binding may name a source column, a constant default, or both — the constant is what "
                + "makes a supplier file with no currency column importable without adding 4,000 "
                + "identical cells. Setting a mapping discards every row's verdict, so validate again.");

        batches.MapPost("/{batchId:guid}/validate", ValidateAsync)
            .RequirePermission(ImportsPermissions.BatchCreate)
            .Produces<ImportBatchCountsResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Checks every row and produces the preview. Writes nothing outside imports.")
            .WithDescription(
                "A row is the unit of judgement: one bad barcode on line 400 does not cost the other "
                + "399. The counters in the response are the report to read before committing.");

        batches.MapPost("/{batchId:guid}/commit", CommitAsync)
            .RequirePermission(ImportsPermissions.BatchCommit)
            .Produces<ImportBatchCountsResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Applies every valid row to its target module, in one transaction.")
            .WithDescription(
                "The only call in this module that writes business data. All or nothing: if the "
                + "database refuses the four-hundredth valid row, none of the first 399 is kept. A "
                + "batch commits once: committing one that has already committed replays the original "
                + "counters rather than applying anything again, so a retried request is safe. "
                + "Committing a rolled-back batch is refused — that is a different intention, not a "
                + "retry.");

        batches.MapPost("/{batchId:guid}/rollback", RollbackAsync)
            .RequirePermission(ImportsPermissions.BatchRollback)
            .Produces<ImportBatchCountsResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Takes back everything a committed batch did (ADR-076).")
            .WithDescription(
                "Compensating, never destructive: a created record is soft-deleted, an updated one is "
                + "restored from its before-image, and a stock movement is reversed by a new ledger "
                + "entry rather than removed. All or nothing — if anything imported has since been "
                + "used, the whole rollback is refused as IMPORTS_ROLLBACK_BLOCKED, naming the rows "
                + "that are in the way and changing nothing.");

        batches.MapPost("/{batchId:guid}/discard", DiscardAsync)
            .RequirePermission(ImportsPermissions.BatchCreate)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Abandons a batch that has not committed.")
            .WithDescription(
                "The batch and its rows stay, saying what was uploaded and what it would have done. "
                + "Only the file's bytes go. A committed batch cannot be discarded — roll it back.");

        batches.MapPost("/{batchId:guid}/save-template", SaveTemplateAsync)
            .RequirePermission(ImportsPermissions.TemplateManage)
            .Produces<ImportsIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Saves this batch's mapping so the same file maps itself next month.")
            .WithDescription(
                "Matched on a hash of the normalised, sorted header row, so a supplier who reorders "
                + "their columns still matches. Amends the template that already matches these headers "
                + "rather than adding a second one; 409 only when the code belongs to an unrelated "
                + "template.");

        RouteGroupBuilder templates = api.MapGroup("/imports/templates").WithTags("Imports");

        templates.MapGet("/", ListTemplatesAsync)
            .RequirePermission(ImportsPermissions.BatchView)
            .Produces<IReadOnlyList<ImportMappingTemplateResponse>>()
            .WithSummary("The saved mappings.");

        templates.MapPost("/{templateId:guid}/deactivate", DeactivateTemplateAsync)
            .RequirePermission(ImportsPermissions.TemplateManage)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Retires a saved mapping from matching. Nothing is deleted.");

        return endpoints;
    }

    private static async Task<IResult> ListTargetsAsync(
        IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        IReadOnlyList<ImportTargetDescriptor> targets = await dispatcher
            .QueryAsync(new ListImportTargetsQuery(), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<ImportTargetResponse>>([.. targets.Select(ToResponse)]);
    }

    private static async Task<IResult> CreateBatchAsync(
        CreateImportBatchRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ImportBatchCreated created = await dispatcher
            .SendAsync(
                new CreateImportBatchCommand(
                    ParseEnum<ImportTargetKind>(request.TargetKind, nameof(request.TargetKind)),
                    ParseEnum<ImportSourceFormat>(request.SourceFormat, nameof(request.SourceFormat)),
                    request.FileName,
                    DecodeContent(request.ContentBase64),
                    request.DuplicateStrategy is null
                        ? ImportDuplicateStrategy.Skip
                        : ParseEnum<ImportDuplicateStrategy>(
                            request.DuplicateStrategy, nameof(request.DuplicateStrategy)),
                    request.StoreId,
                    request.Worksheet,
                    ParseDelimiter(request.Delimiter)),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created(
            $"/api/v1/imports/batches/{created.BatchId}",
            new CreateImportBatchResponse(
                created.BatchId,
                created.BatchNumber,
                created.Status.ToString(),
                created.SourceColumns,
                created.TotalRows,
                created.MappedAutomatically,
                created.TemplateCode,
                created.UnmappedRequiredFields));
    }

    private static async Task<IResult> ListBatchesAsync(
        IDispatcher dispatcher,
        CancellationToken cancellationToken,
        string? targetKind = null,
        string? status = null,
        string? after = null,
        int? limit = null)
    {
        PageResult<ImportBatch> page = await dispatcher
            .QueryAsync(
                new ListImportBatchesQuery(
                    targetKind is null ? null : ParseEnum<ImportTargetKind>(targetKind, nameof(targetKind)),
                    status is null ? null : ParseEnum<ImportBatchStatus>(status, nameof(status)),
                    after,
                    limit),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new PageResponse<ImportBatchResponse>(
            [.. page.Items.Select(ToResponse)], page.NextCursor, page.HasMore));
    }

    private static async Task<IResult> GetBatchAsync(
        Guid batchId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        ImportBatch batch = await dispatcher
            .QueryAsync(new GetImportBatchQuery(batchId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(ToResponse(batch));
    }

    private static async Task<IResult> GetPreviewAsync(
        Guid batchId,
        IDispatcher dispatcher,
        CancellationToken cancellationToken,
        string? status = null,
        string? after = null,
        int? limit = null)
    {
        PageResult<ImportRow> page = await dispatcher
            .QueryAsync(
                new GetImportPreviewQuery(
                    batchId,
                    status is null ? null : ParseEnum<ImportRowStatus>(status, nameof(status)),
                    after,
                    limit),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new PageResponse<ImportRowResponse>(
            [.. page.Items.Select(ToResponse)], page.NextCursor, page.HasMore));
    }

    private static async Task<IResult> SetMappingAsync(
        Guid batchId,
        SetImportMappingRequest request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await dispatcher
            .SendAsync(
                new SetImportMappingCommand(
                    batchId,
                    [
                        .. request.Bindings.Select(binding => new ImportTemplateBinding(
                            binding.TargetField, binding.SourceColumn, binding.DefaultValue)),
                    ]),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> ValidateAsync(
        Guid batchId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        ImportBatchCounts counts = await dispatcher
            .SendAsync(new ValidateImportBatchCommand(batchId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(ToResponse(counts));
    }

    private static async Task<IResult> CommitAsync(
        Guid batchId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        ImportBatchCounts counts = await dispatcher
            .SendAsync(new CommitImportBatchCommand(batchId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(ToResponse(counts));
    }

    private static async Task<IResult> RollbackAsync(
        Guid batchId,
        RollbackImportBatchRequest request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ImportBatchCounts counts = await dispatcher
            .SendAsync(new RollbackImportBatchCommand(batchId, request.Reason), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(ToResponse(counts));
    }

    private static async Task<IResult> DiscardAsync(
        Guid batchId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher
            .SendAsync(new DiscardImportBatchCommand(batchId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> SaveTemplateAsync(
        Guid batchId,
        SaveImportMappingTemplateRequest request,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid id = await dispatcher
            .SendAsync(
                new SaveImportMappingTemplateCommand(batchId, request.Code, request.Name),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/imports/templates/{id}", new ImportsIdResponse(id));
    }

    private static async Task<IResult> ListTemplatesAsync(
        IDispatcher dispatcher,
        CancellationToken cancellationToken,
        string? targetKind = null,
        bool includeInactive = false)
    {
        IReadOnlyList<ImportMappingTemplate> templates = await dispatcher
            .QueryAsync(
                new ListImportMappingTemplatesQuery(
                    targetKind is null ? null : ParseEnum<ImportTargetKind>(targetKind, nameof(targetKind)),
                    includeInactive),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<ImportMappingTemplateResponse>>(
            [.. templates.Select(ToResponse)]);
    }

    private static async Task<IResult> DeactivateTemplateAsync(
        Guid templateId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher
            .SendAsync(new DeleteImportMappingTemplateCommand(templateId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static ImportBatchResponse ToResponse(ImportBatch batch)
        => new(
            batch.Id,
            batch.BatchNumber,
            batch.TargetKind.ToString(),
            batch.SourceFormat.ToString(),
            batch.FileName,
            batch.ContentHash,
            batch.SizeBytes,
            batch.Status.ToString(),
            batch.DuplicateStrategy.ToString(),
            batch.StoreId,
            batch.Worksheet,
            batch.SourceColumns,
            [
                .. batch.Mappings.Select(mapping => new ImportBindingContract(
                    mapping.TargetField, mapping.SourceColumn, mapping.DefaultValue)),
            ],
            new ImportBatchCountsResponse(
                batch.TotalRows,
                batch.ValidRows,
                batch.InvalidRows,
                batch.CreatedRows,
                batch.UpdatedRows,
                batch.SkippedRows),
            batch.CommittedAt,
            batch.CommittedBy,
            batch.RolledBackAt,
            batch.RolledBackBy,
            batch.RollbackReason);

    private static ImportRowResponse ToResponse(ImportRow row)
        => new(
            row.Id,
            row.RowNumber,
            row.Status.ToString(),
            row.Outcome.ToString(),
            row.TargetEntityId,
            row.CompensationEntityId,
            row.RawValues,
            row.NormalisedValues,
            [
                .. row.Errors.Select(error => new ImportRowErrorResponse(
                    error.Code, error.Field, error.Message)),
            ]);

    private static ImportBatchCountsResponse ToResponse(ImportBatchCounts counts)
        => new(
            counts.TotalRows,
            counts.ValidRows,
            counts.InvalidRows,
            counts.CreatedRows,
            counts.UpdatedRows,
            counts.SkippedRows);

    private static ImportTargetResponse ToResponse(ImportTargetDescriptor target)
        => new(
            target.Kind.ToString(),
            target.Name,
            target.Description,
            target.RequiresStore,
            target.NaturalKeyFields,
            [
                .. target.Fields.Select(field => new ImportFieldResponse(
                    field.Name,
                    field.Type.ToString(),
                    field.IsRequired,
                    field.Description,
                    field.Example,
                    field.Aliases)),
            ]);

    private static ImportMappingTemplateResponse ToResponse(ImportMappingTemplate template)
        => new(
            template.Id,
            template.Code,
            template.Name,
            template.TargetKind.ToString(),
            template.SourceSignature,
            template.IsActive,
            template.TimesUsed,
            [
                .. template.Bindings.Select(binding => new ImportBindingContract(
                    binding.TargetField, binding.SourceColumn, binding.DefaultValue)),
            ]);

    /// <summary>
    /// Decodes the upload, refusing malformed base64 at the edge.
    /// </summary>
    /// <param name="content">The encoded bytes.</param>
    /// <remarks>
    /// A 400 the caller can act on rather than a 500 out of the decoder — the same distinction the
    /// malformed-request fix drew across the rest of the API.
    /// </remarks>
    private static ReadOnlyMemory<byte> DecodeContent(string content)
    {
        try
        {
            return Convert.FromBase64String(content ?? string.Empty);
        }
        catch (FormatException)
        {
            throw new ValidationFailedException(
                nameof(CreateImportBatchCommand),
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["ContentBase64"] = ["The file content is not valid base64."],
                });
        }
    }

    /// <summary>Reads the optional single-character CSV delimiter.</summary>
    /// <param name="delimiter">The delimiter as sent, or <c>null</c> to detect it.</param>
    private static char? ParseDelimiter(string? delimiter)
    {
        if (string.IsNullOrEmpty(delimiter))
        {
            return null;
        }

        return delimiter.Length == 1
            ? delimiter[0]
            : throw new ValidationFailedException(
                nameof(CreateImportBatchCommand),
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["Delimiter"] = ["A CSV delimiter is exactly one character."],
                });
    }

    private static TEnum ParseEnum<TEnum>(string value, string propertyName)
        where TEnum : struct, Enum
    {
        if (Enum.TryParse(value, ignoreCase: true, out TEnum parsed) && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        throw new ValidationFailedException(
            nameof(CreateImportBatchCommand),
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [propertyName] = [$"'{value}' is not one of: {string.Join(", ", Enum.GetNames<TEnum>())}."],
            });
    }
}
