using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Imports;
using VumaRetail.Domain.Imports;

namespace VumaRetail.Application.Imports.Commands;

/// <summary>
/// Saves a batch's mapping so the same supplier's file maps itself next month.
/// </summary>
/// <param name="BatchId">The batch whose mapping and headers are being saved.</param>
/// <param name="Code">The template's code, unique per tenant. Upper-cased on the way in.</param>
/// <param name="Name">What to call it, as a manager would recognise it.</param>
/// <remarks>
/// Saved <em>from a batch</em> rather than assembled field by field, deliberately. The mapping worth
/// keeping is the one somebody has just got right on a real file — including the constant defaults
/// they set for the columns that file does not have — and asking them to retype it into a second form
/// is how a feature that saves five minutes a month goes unused.
/// </remarks>
[CommandSideEffect(SideEffect.Write)]
public sealed record SaveImportMappingTemplateCommand(Guid BatchId, string Code, string Name)
    : ICommand<Guid>;

/// <summary>Rejects a malformed save before it reaches the handler.</summary>
public sealed class SaveImportMappingTemplateCommandValidator
    : AbstractValidator<SaveImportMappingTemplateCommand>
{
    /// <summary>Builds the rules.</summary>
    public SaveImportMappingTemplateCommandValidator()
    {
        RuleFor(command => command.BatchId).NotEmpty();
        RuleFor(command => command.Code).NotEmpty().MaximumLength(32);
        RuleFor(command => command.Name).NotEmpty().MaximumLength(128);
    }
}

/// <summary>
/// Saves the template, or amends the one that already matches these headers for this target.
/// </summary>
/// <remarks>
/// An amend rather than a conflict when a template already matches the same headers and target: a
/// signature is what makes a template apply, so two active templates with one signature would leave
/// which mapping wins up to row order. A <em>different</em> code on the same signature is still an
/// amend of the matching template's bindings, and a code already used by an unrelated template is the
/// conflict.
/// </remarks>
/// <param name="templates">Template lookup and insertion.</param>
/// <param name="batches">Batch lookup.</param>
/// <param name="tenant">The ambient tenant.</param>
public sealed class SaveImportMappingTemplateCommandHandler(
    IImportMappingTemplateRepository templates, IImportBatchRepository batches, ITenantContext tenant)
    : ICommandHandler<SaveImportMappingTemplateCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(
        SaveImportMappingTemplateCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        ImportBatch batch = await batches.FindAsync(command.BatchId, cancellationToken).ConfigureAwait(false)
            ?? throw new ImportNotFoundException("import batch", command.BatchId);

        if (batch.Mappings.Count == 0)
        {
            throw new ImportRuleException(
                "IMPORTS_TEMPLATE_BINDS_NOTHING",
                "This batch has no mapping yet, so there is nothing to save.");
        }

        IReadOnlyList<ImportTemplateBinding> bindings =
        [
            .. batch.Mappings.Select(mapping => new ImportTemplateBinding(
                mapping.TargetField, mapping.SourceColumn, mapping.DefaultValue)),
        ];

        ImportMappingTemplate? matching = await templates
            .FindMatchAsync(
                batch.TargetKind,
                ImportMappingTemplate.SignatureOf(batch.SourceColumns),
                cancellationToken)
            .ConfigureAwait(false);

        if (matching is not null)
        {
            matching.Amend(command.Name, batch.SourceColumns, bindings);

            return matching.Id;
        }

        if (await templates.CodeExistsAsync(command.Code, cancellationToken).ConfigureAwait(false))
        {
            throw ImportConflictException.TemplateCode(command.Code);
        }

        ImportMappingTemplate created = ImportMappingTemplate.Create(
            tenant.TenantId, command.Code, command.Name, batch.TargetKind, batch.SourceColumns, bindings);

        templates.Add(created);

        return created.Id;
    }
}

/// <summary>Retires a saved mapping from matching.</summary>
/// <param name="TemplateId">The template.</param>
/// <remarks>
/// A deactivation, not a delete (§7 rule 8). Batches that were mapped by it keep a mapping that names
/// nothing missing, and a template retired by mistake can be brought back rather than rebuilt.
/// </remarks>
[CommandSideEffect(SideEffect.Write)]
public sealed record DeleteImportMappingTemplateCommand(Guid TemplateId) : ICommand;

/// <summary>Rejects a malformed delete before it reaches the handler.</summary>
public sealed class DeleteImportMappingTemplateCommandValidator
    : AbstractValidator<DeleteImportMappingTemplateCommand>
{
    /// <summary>Builds the rules.</summary>
    public DeleteImportMappingTemplateCommandValidator()
        => RuleFor(command => command.TemplateId).NotEmpty();
}

/// <summary>Deactivates the template.</summary>
/// <param name="templates">Template lookup.</param>
public sealed class DeleteImportMappingTemplateCommandHandler(IImportMappingTemplateRepository templates)
    : ICommandHandler<DeleteImportMappingTemplateCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(
        DeleteImportMappingTemplateCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        ImportMappingTemplate template = await templates
            .FindAsync(command.TemplateId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new ImportNotFoundException("import mapping template", command.TemplateId);

        template.Deactivate();

        return Unit.Value;
    }
}
