using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Imports;
using VumaRetail.Domain.Imports;

namespace VumaRetail.Application.Imports.Queries;

/// <summary>One batch, with its mappings and its counters.</summary>
/// <param name="BatchId">The batch.</param>
/// <remarks>
/// The batch's rows come back on it, because the aggregate loads them — which is exactly why the
/// preview is <see cref="GetImportPreviewQuery"/> and not a field on this response. A caller wanting
/// the rows of a fifty-thousand-row file must page them.
/// </remarks>
public sealed record GetImportBatchQuery(Guid BatchId) : IQuery<ImportBatch>;

/// <summary>Reads the batch.</summary>
/// <param name="batches">Batch lookup.</param>
public sealed class GetImportBatchQueryHandler(IImportBatchRepository batches)
    : IQueryHandler<GetImportBatchQuery, ImportBatch>
{
    /// <inheritdoc />
    public async Task<ImportBatch> HandleAsync(
        GetImportBatchQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await batches.FindAsync(query.BatchId, cancellationToken).ConfigureAwait(false)
            ?? throw new ImportNotFoundException("import batch", query.BatchId);
    }
}

/// <summary>A page of batches — the import history.</summary>
/// <param name="TargetKind">Narrow to one target, or <c>null</c> for all.</param>
/// <param name="Status">Narrow to one status, or <c>null</c> for all.</param>
/// <param name="After">The cursor of the last row the caller already has.</param>
/// <param name="Limit">How many rows, clamped by <c>Paging</c>.</param>
public sealed record ListImportBatchesQuery(
    ImportTargetKind? TargetKind = null,
    ImportBatchStatus? Status = null,
    string? After = null,
    int? Limit = null) : IQuery<PageResult<ImportBatch>>;

/// <summary>Reads a page of batches, without their rows.</summary>
/// <param name="batches">Batch lookup.</param>
public sealed class ListImportBatchesQueryHandler(IImportBatchRepository batches)
    : IQueryHandler<ListImportBatchesQuery, PageResult<ImportBatch>>
{
    /// <inheritdoc />
    public async Task<PageResult<ImportBatch>> HandleAsync(
        ListImportBatchesQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        int limit = Paging.Clamp(query.Limit);

        (IReadOnlyList<ImportBatch> items, bool hasMore) = await batches
            .ListPageAsync(query.TargetKind, query.Status, KeysetCursor.TryDecode(query.After), limit, cancellationToken)
            .ConfigureAwait(false);

        return new PageResult<ImportBatch>(
            items,
            hasMore && items.Count > 0
                ? new KeysetCursor(items[^1].BatchNumber, items[^1].Id).Encode()
                : null,
            hasMore);
    }
}

/// <summary>
/// A page of one batch's rows — the preview R5 promises.
/// </summary>
/// <param name="BatchId">The batch.</param>
/// <param name="Status">Narrow to one row status, or <c>null</c> for all.</param>
/// <param name="After">The cursor of the last row the caller already has.</param>
/// <param name="Limit">How many rows, clamped by <c>Paging</c>.</param>
/// <remarks>
/// <b>The <paramref name="Status"/> filter is what makes the preview usable.</b> Nobody scrolls forty
/// thousand rows looking for the eleven bad ones; they ask for the invalid ones, fix those lines in
/// their spreadsheet, and upload again.
/// </remarks>
public sealed record GetImportPreviewQuery(
    Guid BatchId,
    ImportRowStatus? Status = null,
    string? After = null,
    int? Limit = null) : IQuery<PageResult<ImportRow>>;

/// <summary>Reads a page of the batch's rows.</summary>
/// <param name="batches">Batch and row lookup.</param>
public sealed class GetImportPreviewQueryHandler(IImportBatchRepository batches)
    : IQueryHandler<GetImportPreviewQuery, PageResult<ImportRow>>
{
    /// <inheritdoc />
    public async Task<PageResult<ImportRow>> HandleAsync(
        GetImportPreviewQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        int limit = Paging.Clamp(query.Limit);

        (IReadOnlyList<ImportRow> items, bool hasMore) = await batches
            .ListRowsPageAsync(
                query.BatchId, query.Status, KeysetCursor.TryDecode(query.After), limit, cancellationToken)
            .ConfigureAwait(false);

        return new PageResult<ImportRow>(
            items,
            hasMore && items.Count > 0
                // The sort key is the row number, zero-padded so it compares the same as a string in
                // .NET and in PostgreSQL — an unpadded "10" sorts before "9" and the page after the
                // ninth row would skip everything from ten to ninety-nine.
                ? new KeysetCursor(RowCursorKey(items[^1].RowNumber), items[^1].Id).Encode()
                : null,
            hasMore);
    }

    /// <summary>The sort key a row cursor carries.</summary>
    /// <param name="rowNumber">The row's line number.</param>
    public static string RowCursorKey(int rowNumber)
        => rowNumber.ToString("D9", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// Every target and its field catalogue — what a mapping screen builds itself from.
/// </summary>
/// <remarks>
/// This endpoint is why there is no hard-coded field list anywhere in the eventual import screen. It
/// serves the names, the types, the required flags, the natural key, an example and the header aliases
/// for every target the host has wired, so a sixth target is a registration rather than a UI change.
/// </remarks>
public sealed record ListImportTargetsQuery : IQuery<IReadOnlyList<ImportTargetDescriptor>>;

/// <summary>Reads the registered targets' catalogues.</summary>
/// <param name="handlers">The registered target handlers.</param>
public sealed class ListImportTargetsQueryHandler(IImportTargetHandlerFactory handlers)
    : IQueryHandler<ListImportTargetsQuery, IReadOnlyList<ImportTargetDescriptor>>
{
    /// <inheritdoc />
    public Task<IReadOnlyList<ImportTargetDescriptor>> HandleAsync(
        ListImportTargetsQuery query, CancellationToken cancellationToken = default)
        => Task.FromResult(handlers.Descriptors);
}

/// <summary>The saved mappings.</summary>
/// <param name="TargetKind">Narrow to one target, or <c>null</c> for all.</param>
/// <param name="IncludeInactive">True to include retired templates.</param>
public sealed record ListImportMappingTemplatesQuery(
    ImportTargetKind? TargetKind = null, bool IncludeInactive = false)
    : IQuery<IReadOnlyList<ImportMappingTemplate>>;

/// <summary>Reads the templates.</summary>
/// <param name="templates">Template lookup.</param>
public sealed class ListImportMappingTemplatesQueryHandler(IImportMappingTemplateRepository templates)
    : IQueryHandler<ListImportMappingTemplatesQuery, IReadOnlyList<ImportMappingTemplate>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ImportMappingTemplate>> HandleAsync(
        ListImportMappingTemplatesQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await templates
            .ListAsync(query.TargetKind, query.IncludeInactive, cancellationToken)
            .ConfigureAwait(false);
    }
}
