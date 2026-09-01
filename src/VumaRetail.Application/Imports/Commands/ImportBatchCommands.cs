using System.Security.Cryptography;
using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Abstractions.Imports;
using VumaRetail.Domain.Imports;

namespace VumaRetail.Application.Imports.Commands;

/// <summary>
/// Uploads a file, reads it, and maps it if it can — the whole of "here is my spreadsheet".
/// </summary>
/// <param name="TargetKind">What the rows are aimed at.</param>
/// <param name="SourceFormat">The format the bytes are in.</param>
/// <param name="FileName">The name it was uploaded under.</param>
/// <param name="Content">The bytes.</param>
/// <param name="DuplicateStrategy">What to do about rows that already exist.</param>
/// <param name="StoreId">The store, where the target is store-scoped.</param>
/// <param name="Worksheet">The worksheet to read, for a multi-sheet workbook.</param>
/// <param name="Delimiter">The CSV delimiter, or <c>null</c> to detect it from the header row.</param>
/// <remarks>
/// Upload, parse and auto-map are one command rather than three because they are one thing a person
/// does, and because a batch that is uploaded but unparsed is a state nobody can act on. Nothing
/// outside the <c>imports</c> schema is written by any of the three (business rule 1).
/// </remarks>
[CommandSideEffect(SideEffect.Write)]
public sealed record CreateImportBatchCommand(
    ImportTargetKind TargetKind,
    ImportSourceFormat SourceFormat,
    string FileName,
    ReadOnlyMemory<byte> Content,
    ImportDuplicateStrategy DuplicateStrategy = ImportDuplicateStrategy.Skip,
    Guid? StoreId = null,
    string? Worksheet = null,
    char? Delimiter = null) : ICommand<ImportBatchCreated>;

/// <summary>What an upload produced, and whether it still needs a person.</summary>
/// <param name="BatchId">The new batch.</param>
/// <param name="BatchNumber">Its number.</param>
/// <param name="Status">Where it stands — <c>Parsed</c> if it still needs mapping, <c>Mapped</c> if not.</param>
/// <param name="SourceColumns">The headers the reader found.</param>
/// <param name="TotalRows">How many data rows it has.</param>
/// <param name="MappedAutomatically">True when the batch is already mapped and can go straight to validation.</param>
/// <param name="TemplateCode">The saved template that mapped it, when one did.</param>
/// <param name="UnmappedRequiredFields">The required fields still bound to nothing.</param>
public sealed record ImportBatchCreated(
    Guid BatchId,
    string BatchNumber,
    ImportBatchStatus Status,
    IReadOnlyList<string> SourceColumns,
    int TotalRows,
    bool MappedAutomatically,
    string? TemplateCode,
    IReadOnlyList<string> UnmappedRequiredFields);

/// <summary>Rejects a malformed upload before it reaches the handler.</summary>
public sealed class CreateImportBatchCommandValidator : AbstractValidator<CreateImportBatchCommand>
{
    /// <summary>Builds the rules.</summary>
    public CreateImportBatchCommandValidator()
    {
        RuleFor(command => command.FileName).NotEmpty().MaximumLength(260);
        RuleFor(command => command.TargetKind).IsInEnum();
        RuleFor(command => command.SourceFormat).IsInEnum();
        RuleFor(command => command.DuplicateStrategy).IsInEnum();
        RuleFor(command => command.Content).Must(content => content.Length > 0)
            .WithMessage("The uploaded file is empty.");
    }
}

/// <summary>
/// Hashes the upload, refuses a re-run of a file already committed, parses it and maps what it can.
/// </summary>
/// <param name="batches">Batch lookup and insertion.</param>
/// <param name="templates">Saved mapping lookup.</param>
/// <param name="readers">The reader for the declared format.</param>
/// <param name="handlers">The target's field catalogue.</param>
/// <param name="files">Where the uploaded bytes are kept.</param>
/// <param name="numbers">ADR-065's document number sequence.</param>
/// <param name="tenant">The ambient tenant.</param>
/// <param name="options">The upload and row ceilings.</param>
public sealed class CreateImportBatchCommandHandler(
    IImportBatchRepository batches,
    IImportMappingTemplateRepository templates,
    IImportSourceReaderFactory readers,
    IImportTargetHandlerFactory handlers,
    IImportFileStore files,
    IDocumentNumberSequence numbers,
    ITenantContext tenant,
    ImportOptions options) : ICommandHandler<CreateImportBatchCommand, ImportBatchCreated>
{
    /// <summary>The document number series an import batch is numbered from (ADR-065).</summary>
    public const string NumberSeries = "IMP";

    /// <inheritdoc />
    public async Task<ImportBatchCreated> HandleAsync(
        CreateImportBatchCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Content.Length > options.MaxUploadBytes)
        {
            throw ImportRuleException.UploadTooLarge(command.Content.Length, options.MaxUploadBytes);
        }

        ImportTargetDescriptor target = handlers.For(command.TargetKind).Descriptor;

        if (target.RequiresStore && command.StoreId is null)
        {
            throw new ImportRuleException(
                "IMPORTS_STORE_REQUIRED",
                $"The {target.Name} target counts things that are somewhere, so the batch has to say "
                + "which store it is for.");
        }

        string contentHash = Convert.ToHexString(SHA256.HashData(command.Content.Span)).ToLowerInvariant();

        // Content, not file name. The same sheet saved twice under two names is the same sheet, and
        // re-importing it is how somebody doubles their opening stock.
        ImportBatch? alreadyCommitted = await batches
            .FindCommittedByContentHashAsync(contentHash, cancellationToken)
            .ConfigureAwait(false);

        if (alreadyCommitted is not null)
        {
            throw ImportConflictException.FileAlreadyCommitted(
                alreadyCommitted.FileName, alreadyCommitted.BatchNumber);
        }

        string batchNumber = await numbers.NextAsync(NumberSeries, cancellationToken).ConfigureAwait(false);

        ImportBatch batch = ImportBatch.Create(
            tenant.TenantId,
            command.StoreId,
            batchNumber,
            command.TargetKind,
            command.SourceFormat,
            command.FileName,
            contentHash,
            command.Content.Length,
            command.DuplicateStrategy,
            command.Worksheet);

        batches.Add(batch);

        await files.StoreAsync(batch.Id, command.Content, cancellationToken).ConfigureAwait(false);

        ImportSheet sheet = await ReadAsync(command, batch, cancellationToken).ConfigureAwait(false);

        batch.RecordParse(
            sheet.Headers,
            [
                .. sheet.Rows.Select(row => ImportRow.Create(
                    tenant.TenantId, command.StoreId, batch.Id, row.RowNumber, row.Values)),
            ]);

        (bool mapped, string? templateCode) = await TryMapAsync(batch, target, cancellationToken)
            .ConfigureAwait(false);

        return new ImportBatchCreated(
            batch.Id,
            batch.BatchNumber,
            batch.Status,
            batch.SourceColumns,
            batch.TotalRows,
            mapped,
            templateCode,
            ImportMapper.UnmappedRequiredFields(target, BindingsOf(batch)));
    }

    /// <summary>Reads the upload with the reader its declared format names.</summary>
    /// <param name="command">The upload.</param>
    /// <param name="batch">The batch, for its worksheet.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    private async Task<ImportSheet> ReadAsync(
        CreateImportBatchCommand command, ImportBatch batch, CancellationToken cancellationToken)
    {
        // A MemoryStream over the same bytes rather than a second copy: the readers all take a Stream
        // because that is what an upload is, and a 32 MB array duplicated per import is 32 MB a till
        // does not have.
        using MemoryStream content = new(command.Content.ToArray(), writable: false);

        return await readers
            .For(command.SourceFormat)
            .ReadAsync(
                content,
                new ImportReadOptions(batch.Worksheet, command.Delimiter, options.MaxRows),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Maps the batch from a saved template, or by header alias, or leaves it for a person.</summary>
    /// <param name="batch">The parsed batch.</param>
    /// <param name="target">The target's field catalogue.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Whether the batch is mapped, and the template that did it.</returns>
    /// <remarks>
    /// A template beats aliases because it carries what aliases cannot: the constant defaults somebody
    /// set for the columns the file does not have. Alias matching leaves those unbound by definition —
    /// a missing column has no header to match on.
    /// </remarks>
    private async Task<(bool Mapped, string? TemplateCode)> TryMapAsync(
        ImportBatch batch, ImportTargetDescriptor target, CancellationToken cancellationToken)
    {
        string[] required = [.. target.Fields.Where(field => field.IsRequired).Select(field => field.Name)];

        ImportMappingTemplate? template = await templates
            .FindMatchAsync(
                batch.TargetKind,
                ImportMappingTemplate.SignatureOf(batch.SourceColumns),
                cancellationToken)
            .ConfigureAwait(false);

        if (template is not null
            && ImportMapper.UnmappedRequiredFields(target, template.Bindings).Count == 0
            && ImportMapper.UnknownTargetFields(target, template.Bindings).Count == 0)
        {
            batch.SetMapping(template.ToMappings(batch.StoreId, batch.Id), required);
            template.RecordUse();

            return (true, template.Code);
        }

        IReadOnlyList<ImportTemplateBinding> bindings = ImportMapper.AutoMap(target, batch.SourceColumns);

        if (ImportMapper.UnmappedRequiredFields(target, bindings).Count > 0)
        {
            // Left at Parsed on purpose. A partial mapping applied silently is a mapping nobody has
            // seen, and the person is about to be shown the bindings anyway — the field catalogue and
            // the headers are both on the response.
            return (false, null);
        }

        batch.SetMapping(
            [
                .. bindings.Select(binding => ImportColumnMapping.Create(
                    batch.TenantId, batch.StoreId, batch.Id,
                    binding.TargetField, binding.SourceColumn, binding.DefaultValue)),
            ],
            required);

        return (true, null);
    }

    /// <summary>The batch's mappings as template bindings, for the unmapped-fields report.</summary>
    /// <param name="batch">The batch.</param>
    private static IReadOnlyList<ImportTemplateBinding> BindingsOf(ImportBatch batch)
        => [.. batch.Mappings.Select(mapping => new ImportTemplateBinding(
            mapping.TargetField, mapping.SourceColumn, mapping.DefaultValue))];
}

/// <summary>Binds the target's fields to the file's columns.</summary>
/// <param name="BatchId">The batch.</param>
/// <param name="Bindings">One binding per field being bound. Replaces whatever was there.</param>
/// <remarks>
/// Replaces rather than merges, and every row's verdict is thrown away with the old mapping — see
/// <c>ImportBatch.SetMapping</c>. A row judged against bindings that no longer exist is a preview
/// somebody would act on and a commit that would do something else.
/// </remarks>
[CommandSideEffect(SideEffect.Write)]
public sealed record SetImportMappingCommand(
    Guid BatchId, IReadOnlyList<ImportTemplateBinding> Bindings) : ICommand;

/// <summary>Rejects a malformed mapping before it reaches the handler.</summary>
public sealed class SetImportMappingCommandValidator : AbstractValidator<SetImportMappingCommand>
{
    /// <summary>Builds the rules.</summary>
    public SetImportMappingCommandValidator()
    {
        RuleFor(command => command.BatchId).NotEmpty();
        RuleFor(command => command.Bindings).NotEmpty();
        RuleForEach(command => command.Bindings)
            .ChildRules(binding => binding.RuleFor(item => item.TargetField).NotEmpty().MaximumLength(64));
    }
}

/// <summary>Sets the mapping, refusing a field the target does not have.</summary>
/// <param name="batches">Batch lookup.</param>
/// <param name="handlers">The target's field catalogue.</param>
public sealed class SetImportMappingCommandHandler(
    IImportBatchRepository batches, IImportTargetHandlerFactory handlers)
    : ICommandHandler<SetImportMappingCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(
        SetImportMappingCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        ImportBatch batch = await batches.FindAsync(command.BatchId, cancellationToken).ConfigureAwait(false)
            ?? throw new ImportNotFoundException("import batch", command.BatchId);

        ImportTargetDescriptor target = handlers.For(batch.TargetKind).Descriptor;

        // Checked here rather than left to produce a row error per row: a mapping naming `unitprice`
        // where the field is `unitPrice` would otherwise fail four thousand rows for a missing price
        // and leave somebody staring at a spreadsheet that is perfectly fine.
        if (ImportMapper.UnknownTargetFields(target, command.Bindings) is { Count: > 0 } unknown)
        {
            throw ImportRuleException.UnknownTargetField(unknown[0], batch.TargetKind);
        }

        batch.SetMapping(
            [
                .. command.Bindings.Select(binding => ImportColumnMapping.Create(
                    batch.TenantId, batch.StoreId, batch.Id,
                    binding.TargetField, binding.SourceColumn, binding.DefaultValue)),
            ],
            [.. target.Fields.Where(field => field.IsRequired).Select(field => field.Name)]);

        return Unit.Value;
    }
}

/// <summary>Checks every row and produces the preview.</summary>
/// <param name="BatchId">The batch.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record ValidateImportBatchCommand(Guid BatchId) : ICommand<ImportBatchCounts>;

/// <summary>What a validation pass concluded about a whole batch.</summary>
/// <param name="TotalRows">How many data rows the file had.</param>
/// <param name="ValidRows">How many would apply cleanly.</param>
/// <param name="InvalidRows">How many would not.</param>
/// <param name="CreatedRows">How many created something. Zero until the batch commits.</param>
/// <param name="UpdatedRows">How many changed something. Zero until the batch commits.</param>
/// <param name="SkippedRows">How many were deliberately left alone.</param>
public sealed record ImportBatchCounts(
    int TotalRows, int ValidRows, int InvalidRows, int CreatedRows, int UpdatedRows, int SkippedRows)
{
    /// <summary>Reads a batch's counters.</summary>
    /// <param name="batch">The batch.</param>
    public static ImportBatchCounts Of(ImportBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        return new ImportBatchCounts(
            batch.TotalRows,
            batch.ValidRows,
            batch.InvalidRows,
            batch.CreatedRows,
            batch.UpdatedRows,
            batch.SkippedRows);
    }
}

/// <summary>Rejects a malformed validate command before it reaches the handler.</summary>
public sealed class ValidateImportBatchCommandValidator : AbstractValidator<ValidateImportBatchCommand>
{
    /// <summary>Builds the rules.</summary>
    public ValidateImportBatchCommandValidator() => RuleFor(command => command.BatchId).NotEmpty();
}

/// <summary>Runs the validator over the batch and records the verdicts.</summary>
/// <param name="batches">Batch lookup.</param>
/// <param name="validator">The one validator (see <see cref="IImportValidator"/>).</param>
/// <param name="contexts">Where the batch's currency and duplicate strategy come from.</param>
public sealed class ValidateImportBatchCommandHandler(
    IImportBatchRepository batches, IImportValidator validator, IImportContextFactory contexts)
    : ICommandHandler<ValidateImportBatchCommand, ImportBatchCounts>
{
    /// <inheritdoc />
    public async Task<ImportBatchCounts> HandleAsync(
        ValidateImportBatchCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        ImportBatch batch = await batches.FindAsync(command.BatchId, cancellationToken).ConfigureAwait(false)
            ?? throw new ImportNotFoundException("import batch", command.BatchId);

        ImportContext context = await contexts.ForAsync(batch, cancellationToken).ConfigureAwait(false);

        batch.RecordValidation(
            await validator.ValidateAsync(batch, context, cancellationToken).ConfigureAwait(false));

        return ImportBatchCounts.Of(batch);
    }
}

/// <summary>Applies every valid row to its target module. The one command that writes business data.</summary>
/// <param name="BatchId">The batch.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record CommitImportBatchCommand(Guid BatchId) : ICommand<ImportBatchCounts>;

/// <summary>Rejects a malformed commit before it reaches the handler.</summary>
public sealed class CommitImportBatchCommandValidator : AbstractValidator<CommitImportBatchCommand>
{
    /// <summary>Builds the rules.</summary>
    public CommitImportBatchCommandValidator() => RuleFor(command => command.BatchId).NotEmpty();
}

/// <summary>
/// Applies the batch, inside the pipeline's single transaction.
/// </summary>
/// <remarks>
/// <para>
/// <b>One transaction, all or nothing (business rule 3).</b> The handler writes nothing itself and
/// commits nothing — the pipeline's <c>IUnitOfWork</c> does, once, after this returns. So if the
/// database refuses the four-hundredth row, none of the first 399 is kept. Rule 2 is about
/// <em>validity</em>, not about partial writes: invalid rows are skipped, but a failure part-way
/// through is not a half-import, because half an import is the one outcome a rollback cannot describe.
/// </para>
/// <para>
/// <b>The status is re-read under a lock (business rule 4).</b> <c>FindForUpdateAsync</c> holds a row
/// lock for the life of the transaction, so two callers committing one batch serialise and the second
/// finds it already <c>Committed</c>. An unlocked read lets both see <c>Validated</c> and both apply
/// their rows, and the second commit is invisible in the counters.
/// </para>
/// </remarks>
/// <param name="batches">Batch lookup, under a row lock.</param>
/// <param name="handlers">The target handler that does the writing.</param>
/// <param name="contexts">Where the batch's currency and duplicate strategy come from.</param>
/// <param name="files">Where the uploaded bytes are kept, so they can be dropped.</param>
/// <param name="clock">The only source of time (<c>CONVENTIONS.md</c> §6).</param>
/// <param name="principal">Who committed.</param>
/// <param name="options">Whether the file's bytes are dropped on commit.</param>
public sealed class CommitImportBatchCommandHandler(
    IImportBatchRepository batches,
    IImportTargetHandlerFactory handlers,
    IImportContextFactory contexts,
    IImportFileStore files,
    IClock clock,
    IPrincipalAccessor principal,
    ImportOptions options) : ICommandHandler<CommitImportBatchCommand, ImportBatchCounts>
{
    /// <inheritdoc />
    public async Task<ImportBatchCounts> HandleAsync(
        CommitImportBatchCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        ImportBatch batch = await batches.FindForUpdateAsync(command.BatchId, cancellationToken).ConfigureAwait(false)
            ?? throw new ImportNotFoundException("import batch", command.BatchId);

        if (batch.Status is ImportBatchStatus.Committed)
        {
            // A replay, not an error. The batch id *is* this operation's idempotency key
            // (API_STANDARDS.md §9): committing batch X is the same operation whoever asks and however
            // often, so the second call returns the first call's answer rather than a refusal. A
            // person whose browser timed out on a forty-thousand-row commit will press the button
            // again, and the honest answer to that press is the counters, not a 422 that reads as
            // though the import failed.
            //
            // Only this status replays. A rolled-back batch is refused below, because "commit" after
            // somebody deliberately took the import back is not a retry — it is a different intention,
            // and re-applying it silently would undo their rollback.
            return ImportBatchCounts.Of(batch);
        }

        batch.BeginCommit();

        ImportContext context = await contexts.ForAsync(batch, cancellationToken).ConfigureAwait(false);
        IImportTargetHandler handler = handlers.For(batch.TargetKind);

        foreach (ImportRow row in batch.CommittableRows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Asked again at commit time rather than trusted from the preview. Between the two, a
            // partner may have been created by hand, or an item sold out — and the duplicate strategy
            // is a decision about what exists *now*, not what existed when somebody clicked preview.
            ImportSemanticVerdict verdict = await handler
                .ValidateAsync(row.NormalisedValues, context, cancellationToken)
                .ConfigureAwait(false);

            if (verdict.WouldSkip)
            {
                batch.RecordRowSkipped(row.RowNumber);
                continue;
            }

            if (verdict.Errors.Count > 0)
            {
                // Something changed under the preview. The row is not applied and says why, which is
                // an honest answer; failing the whole batch for it would throw away 3,999 good rows
                // because one partner was created by hand five minutes ago.
                batch.RecordRowSkipped(row.RowNumber);
                continue;
            }

            ImportApplyResult applied = await handler
                .ApplyAsync(row.NormalisedValues, context, cancellationToken)
                .ConfigureAwait(false);

            batch.RecordRowCommitted(
                row.RowNumber, applied.Outcome, applied.TargetEntityId, applied.BeforeImage);
        }

        batch.Commit(clock.UtcNow, principal.Principal);

        if (options.DeleteFileOnCommit)
        {
            // The batch, its rows and their raw values are the record of what was imported and are
            // kept. The file itself is only needed to re-parse a batch that has not committed.
            await files.DeleteAsync(batch.Id, cancellationToken).ConfigureAwait(false);
        }

        return ImportBatchCounts.Of(batch);
    }
}

/// <summary>Takes back everything a committed batch did (ADR-076).</summary>
/// <param name="BatchId">The batch.</param>
/// <param name="Reason">Why — required, because a rollback undoes work somebody may be relying on.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record RollbackImportBatchCommand(Guid BatchId, string Reason) : ICommand<ImportBatchCounts>;

/// <summary>Rejects a malformed rollback before it reaches the handler.</summary>
public sealed class RollbackImportBatchCommandValidator : AbstractValidator<RollbackImportBatchCommand>
{
    /// <summary>Builds the rules.</summary>
    public RollbackImportBatchCommandValidator()
    {
        RuleFor(command => command.BatchId).NotEmpty();
        RuleFor(command => command.Reason).NotEmpty().MaximumLength(512);
    }
}

/// <summary>
/// Compensates every committed row, or refuses the whole rollback.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two passes, and the first one writes nothing.</b> Every row is asked whether it <em>could</em>
/// be compensated before any of them is. That is what makes ADR-076's all-or-nothing promise real
/// rather than aspirational: a single-pass rollback that discovered an obstacle on row 900 would have
/// already un-imported 899 rows, and there would be no way back.
/// </para>
/// <para>
/// <b>Rows are compensated in reverse file order.</b> An item created on row 12 and priced on row 40
/// must lose its price before it loses its existence, or row 40's compensation runs against something
/// already soft-deleted.
/// </para>
/// </remarks>
/// <param name="batches">Batch lookup, under a row lock.</param>
/// <param name="handlers">The target handler that does the compensating.</param>
/// <param name="contexts">Where the batch's currency comes from.</param>
/// <param name="clock">The only source of time.</param>
/// <param name="principal">Who rolled it back.</param>
public sealed class RollbackImportBatchCommandHandler(
    IImportBatchRepository batches,
    IImportTargetHandlerFactory handlers,
    IImportContextFactory contexts,
    IClock clock,
    IPrincipalAccessor principal) : ICommandHandler<RollbackImportBatchCommand, ImportBatchCounts>
{
    /// <inheritdoc />
    public async Task<ImportBatchCounts> HandleAsync(
        RollbackImportBatchCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        ImportBatch batch = await batches.FindForUpdateAsync(command.BatchId, cancellationToken).ConfigureAwait(false)
            ?? throw new ImportNotFoundException("import batch", command.BatchId);

        if (batch.Status is ImportBatchStatus.RolledBack)
        {
            // A replay, not an error — the same idempotency contract Commit's own handler gives itself
            // (§4.26). The batch id is this operation's idempotency key: a second "roll back X" after a
            // dropped connection returns the first call's counters instead of a refusal that reads as
            // though the rollback failed.
            return ImportBatchCounts.Of(batch);
        }

        if (batch.Status is not ImportBatchStatus.Committed)
        {
            throw ImportRuleException.UnexpectedBatchStatus(batch.Status, "rolled back");
        }

        ImportContext context = await contexts.ForAsync(batch, cancellationToken).ConfigureAwait(false);
        IImportTargetHandler handler = handlers.For(batch.TargetKind);
        IReadOnlyList<ImportRow> rows = batch.CompensatableRows;

        List<ImportRollbackObstacle> obstacles = [];

        foreach (ImportRow row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await handler.CanCompensateAsync(row, context, cancellationToken).ConfigureAwait(false)
                is { } obstacle)
            {
                obstacles.Add(obstacle);
            }
        }

        if (obstacles.Count > 0)
        {
            throw ImportRuleException.RollbackBlocked(
                [.. obstacles.Select(obstacle => obstacle.RowNumber)],
                string.Join("; ", obstacles.Select(obstacle => obstacle.Reason).Distinct(StringComparer.Ordinal)));
        }

        foreach (ImportRow row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Guid? compensation = await handler
                .CompensateAsync(row, context, cancellationToken)
                .ConfigureAwait(false);

            batch.RecordRowRolledBack(row.RowNumber, compensation);
        }

        batch.RollBack(clock.UtcNow, principal.Principal, command.Reason);

        return ImportBatchCounts.Of(batch);
    }
}

/// <summary>Abandons a batch that has not committed. Nothing outside <c>imports</c> is touched.</summary>
/// <param name="BatchId">The batch.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record DiscardImportBatchCommand(Guid BatchId) : ICommand;

/// <summary>Rejects a malformed discard before it reaches the handler.</summary>
public sealed class DiscardImportBatchCommandValidator : AbstractValidator<DiscardImportBatchCommand>
{
    /// <summary>Builds the rules.</summary>
    public DiscardImportBatchCommandValidator() => RuleFor(command => command.BatchId).NotEmpty();
}

/// <summary>Discards the batch and drops its bytes.</summary>
/// <param name="batches">Batch lookup.</param>
/// <param name="files">Where the uploaded bytes are kept.</param>
public sealed class DiscardImportBatchCommandHandler(
    IImportBatchRepository batches, IImportFileStore files)
    : ICommandHandler<DiscardImportBatchCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(
        DiscardImportBatchCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        ImportBatch batch = await batches.FindAsync(command.BatchId, cancellationToken).ConfigureAwait(false)
            ?? throw new ImportNotFoundException("import batch", command.BatchId);

        batch.Discard();

        // The row keeps saying what was uploaded, when, by whom and what it would have done. Only the
        // bytes go, and only because a discarded batch will never be re-parsed.
        await files.DeleteAsync(batch.Id, cancellationToken).ConfigureAwait(false);

        return Unit.Value;
    }
}
