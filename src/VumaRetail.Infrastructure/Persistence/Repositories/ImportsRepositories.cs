using System.Globalization;
using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Imports;
using VumaRetail.Domain.Finance;
using VumaRetail.Domain.Imports;
using VumaRetail.Domain.Inventory;

namespace VumaRetail.Infrastructure.Persistence.Repositories;

/// <summary>EF Core implementation of <see cref="IImportBatchRepository"/>.</summary>
/// <param name="context">The database context.</param>
/// <param name="tenant">The ambient tenant, for the locking read's explicit predicate.</param>
/// <remarks>
/// <para>
/// <see cref="FindAsync"/> loads the rows and the mappings because every writing caller needs both —
/// an aggregate whose children are missing quietly behaves as though it had none, and here that would
/// mean a commit that applied nothing and reported success.
/// </para>
/// <para>
/// <see cref="ListPageAsync"/> deliberately does not. A list of batches is a list of filenames, and
/// eagerly loading fifty thousand rows per batch to render a table of twenty-five of them is the
/// mistake the port's own remarks exist to prevent.
/// </para>
/// </remarks>
public sealed class ImportBatchRepository(VumaRetailDbContext context, ITenantContext tenant)
    : IImportBatchRepository
{
    /// <inheritdoc />
    public Task<ImportBatch?> FindAsync(Guid batchId, CancellationToken cancellationToken = default)
        => context.ImportBatches
            .Include(batch => batch.Rows)
            .Include(batch => batch.Mappings)
            .FirstOrDefaultAsync(batch => batch.Id == batchId, cancellationToken);

    /// <inheritdoc />
    public async Task<ImportBatch?> FindForUpdateAsync(
        Guid batchId, CancellationToken cancellationToken = default)
    {
        // The lock is taken on the batch row only, and held until the command's own commit — the
        // pipeline owns the transaction, and a repository may not open one (the rule that keeps every
        // write on one commit). Locking the batch is enough: business rule 4 is about two callers
        // committing one batch, and both of them have to pass through this row to do it.
        //
        // The tenant predicate is written out rather than left to the global query filter, because
        // FromSqlInterpolated is raw SQL and the filter does not reach inside it. Without it, a batch
        // id from another tenant would be locked and read before the composed LINQ filter dropped it.
        ImportBatch? batch = await context.ImportBatches
            .FromSqlInterpolated(
                $"""
                 SELECT * FROM imports.import_batches
                 WHERE id = {batchId} AND tenant_id = {tenant.TenantId} AND deleted_at IS NULL
                 FOR UPDATE
                 """)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (batch is null)
        {
            return null;
        }

        // Loaded after the lock rather than joined into it: FOR UPDATE across a join would lock every
        // row of a fifty-thousand-row file for the life of the commit, and nothing else contends for
        // those rows anyway — they are reachable only through the batch this caller now holds.
        await context.Entry(batch).Collection(item => item.Rows).LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        await context.Entry(batch).Collection(item => item.Mappings).LoadAsync(cancellationToken)
            .ConfigureAwait(false);

        return batch;
    }

    /// <inheritdoc />
    public Task<ImportBatch?> FindCommittedByContentHashAsync(
        string contentHash, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);

        string normalised = contentHash.Trim().ToLowerInvariant();

        return context.ImportBatches
            .AsNoTracking()
            .Where(batch => batch.ContentHash == normalised && batch.Status == ImportBatchStatus.Committed)
            .OrderByDescending(batch => batch.CommittedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<(IReadOnlyList<ImportBatch> Items, bool HasMore)> ListPageAsync(
        ImportTargetKind? targetKind,
        ImportBatchStatus? status,
        KeysetCursor? after,
        int limit,
        CancellationToken cancellationToken = default)
    {
        IQueryable<ImportBatch> query = context.ImportBatches.AsNoTracking();

        if (targetKind is { } kind)
        {
            query = query.Where(batch => batch.TargetKind == kind);
        }

        if (status is { } wanted)
        {
            query = query.Where(batch => batch.Status == wanted);
        }

        if (after is { } cursor)
        {
            query = query.Where(batch
                => string.Compare(batch.BatchNumber, cursor.SortKey, StringComparison.Ordinal) > 0
                || (batch.BatchNumber == cursor.SortKey && batch.Id > cursor.Id));
        }

        List<ImportBatch> page = await query
            .OrderBy(batch => batch.BatchNumber)
            .ThenBy(batch => batch.Id)
            .Take(limit + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        bool hasMore = page.Count > limit;

        return (hasMore ? page[..limit] : page, hasMore);
    }

    /// <inheritdoc />
    public async Task<(IReadOnlyList<ImportRow> Items, bool HasMore)> ListRowsPageAsync(
        Guid batchId,
        ImportRowStatus? status,
        KeysetCursor? after,
        int limit,
        CancellationToken cancellationToken = default)
    {
        IQueryable<ImportRow> query = context.ImportRows
            .AsNoTracking()
            .Where(row => row.ImportBatchId == batchId);

        if (status is { } wanted)
        {
            query = query.Where(row => row.Status == wanted);
        }

        if (after is { } cursor && int.TryParse(cursor.SortKey, NumberStyles.None, CultureInfo.InvariantCulture, out int afterRow))
        {
            query = query.Where(row => row.RowNumber > afterRow);
        }

        List<ImportRow> page = await query
            .OrderBy(row => row.RowNumber)
            .Take(limit + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        bool hasMore = page.Count > limit;

        return (hasMore ? page[..limit] : page, hasMore);
    }

    /// <inheritdoc />
    public void Add(ImportBatch batch) => context.ImportBatches.Add(batch);
}

/// <summary>EF Core implementation of <see cref="IImportMappingTemplateRepository"/>.</summary>
/// <param name="context">The database context.</param>
public sealed class ImportMappingTemplateRepository(VumaRetailDbContext context)
    : IImportMappingTemplateRepository
{
    /// <inheritdoc />
    public Task<ImportMappingTemplate?> FindAsync(
        Guid templateId, CancellationToken cancellationToken = default)
        => context.ImportMappingTemplates
            .FirstOrDefaultAsync(template => template.Id == templateId, cancellationToken);

    /// <inheritdoc />
    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        string normalised = code.Trim().ToUpperInvariant();

        return context.ImportMappingTemplates
            .AsNoTracking()
            .AnyAsync(template => template.Code == normalised, cancellationToken);
    }

    /// <inheritdoc />
    public Task<ImportMappingTemplate?> FindMatchAsync(
        ImportTargetKind targetKind, string sourceSignature, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceSignature);

        // Tracked, not AsNoTracking: a match is about to have RecordUse called on it, and a use count
        // that silently fails to persist is a use count nobody can trust to retire a template on.
        return context.ImportMappingTemplates
            .FirstOrDefaultAsync(
                template => template.TargetKind == targetKind
                    && template.SourceSignature == sourceSignature
                    && template.IsActive,
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ImportMappingTemplate>> ListAsync(
        ImportTargetKind? targetKind,
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
        IQueryable<ImportMappingTemplate> query = context.ImportMappingTemplates.AsNoTracking();

        if (targetKind is { } kind)
        {
            query = query.Where(template => template.TargetKind == kind);
        }

        if (!includeInactive)
        {
            query = query.Where(template => template.IsActive);
        }

        // Not paginated: a tenant maintains one template per supplier file, which is a number of rows
        // a person could count on their fingers, not a catalogue.
        return await query
            .OrderBy(template => template.TargetKind)
            .ThenBy(template => template.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Add(ImportMappingTemplate template) => context.ImportMappingTemplates.Add(template);
}

/// <summary>
/// Answers rollback rule 6's question by asking every schema that could hold an answer.
/// </summary>
/// <param name="context">The database context.</param>
/// <remarks>
/// <para>
/// <b>The one place in the build that deliberately reads across module boundaries</b>, and it lives
/// in Infrastructure precisely because Infrastructure is the layer that already composes every schema
/// (<c>CONVENTIONS.md</c> §2). Everything it does is a read, it returns a sentence rather than a row,
/// and no other module's type appears in the port it implements — so nothing here creates a
/// dependency between modules, only a query that spans them.
/// </para>
/// <para>
/// <b>When a later stage adds a document that references an item or a partner, it is added here.</b>
/// A missing check does not fail loudly: it makes a rollback succeed that should have been refused,
/// and leaves a posted document pointing at a soft-deleted record. The list below is therefore
/// deliberately explicit rather than clever, so that a person reading it can see what is and is not
/// covered.
/// </para>
/// </remarks>
public sealed class ImportUsageProbe(VumaRetailDbContext context) : IImportUsageProbe
{
    /// <inheritdoc />
    public async Task<string?> DescribePartnerUsageAsync(
        Guid partnerId, CancellationToken cancellationToken = default)
    {
        PartnerId partner = PartnerId.From(partnerId);

        string? invoice = await context.ArInvoices
            .AsNoTracking()
            .Where(document => document.PartnerId == partner)
            .Select(document => document.InvoiceNumber)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (invoice is not null)
        {
            return $"invoiced on {invoice}";
        }

        string? bill = await context.ApInvoices
            .AsNoTracking()
            .Where(document => document.PartnerId == partner)
            .Select(document => document.SupplierInvoiceNumber)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (bill is not null)
        {
            return $"billed on supplier invoice {bill}";
        }

        string? sale = await context.Sales
            .AsNoTracking()
            .Where(document => document.CustomerId == partnerId)
            .Select(document => document.SaleNumber)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return sale is null ? null : $"sold to on {sale}";
    }

    /// <inheritdoc />
    public async Task<string?> DescribeItemUsageAsync(
        Guid itemId, CancellationToken cancellationToken = default)
    {
        Guid? soldOn = await context.SaleLines
            .AsNoTracking()
            .Where(line => line.ItemId == itemId)
            .Select(line => (Guid?)line.SaleId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (soldOn is { } saleId)
        {
            string? number = await context.Sales
                .AsNoTracking()
                .Where(sale => sale.Id == saleId)
                .Select(sale => sale.SaleNumber)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return $"sold on {number ?? saleId.ToString()}";
        }

        // An import's own stock movements are not usage. A file that creates an item and opens its
        // stock is one document, and treating its own second half as a blocker would make every
        // combined import unrollbackable.
        bool moved = await context.StockLedgerEntries
            .AsNoTracking()
            .AnyAsync(
                entry => entry.ItemId == itemId && entry.ReferenceType != StockReferenceType.Import,
                cancellationToken)
            .ConfigureAwait(false);

        if (moved)
        {
            return "it has stock movements against it";
        }

        bool counted = await context.StocktakeLines
            .AsNoTracking()
            .AnyAsync(line => line.ItemId == itemId, cancellationToken)
            .ConfigureAwait(false);

        return counted ? "it has been counted in a stocktake" : null;
    }
}
