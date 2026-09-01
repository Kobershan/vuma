using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Imports;

/// <summary>
/// One target field bound to one source column — the row that makes "the shop's file has the shop's
/// columns" a configuration problem rather than a support call.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SourceColumn"/> is nullable and <see cref="DefaultValue"/> exists for the case that
/// makes real files work: the field is required, and the file simply does not have it. A supplier's
/// price sheet has no <c>currency</c> column because that supplier quotes in one currency and
/// everybody knows it. Rather than forcing somebody to add a column of 4,000 identical cells, the
/// mapping carries the constant.
/// </para>
/// <para>
/// One of the two must be present, and the entity enforces it rather than trusting the caller — a
/// mapping bound to neither is a required field that silently imports as empty.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class ImportColumnMapping : Entity
{
    private ImportColumnMapping(
        Guid tenantId,
        Guid? storeId,
        Guid importBatchId,
        string targetField,
        string? sourceColumn,
        string? defaultValue)
        : base(tenantId, storeId)
    {
        ImportBatchId = importBatchId;
        TargetField = targetField;
        SourceColumn = sourceColumn;
        DefaultValue = defaultValue;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private ImportColumnMapping()
    {
    }

    /// <summary>The batch this mapping belongs to.</summary>
    public Guid ImportBatchId { get; private set; }

    /// <summary>The target field this binds, as named in the target's field catalogue.</summary>
    public string TargetField { get; private set; } = string.Empty;

    /// <summary>The source column it reads, or <c>null</c> when the value is a constant.</summary>
    public string? SourceColumn { get; private set; }

    /// <summary>
    /// The constant to use when the file has no such column, or when a cell is empty.
    /// </summary>
    /// <remarks>
    /// It applies to an empty cell as well as to a missing column, deliberately. A sheet where most
    /// rows carry a unit of measure and a few are blank wants the blank ones defaulted, not rejected
    /// — and the person who set the default has already said what they mean by an empty cell.
    /// </remarks>
    public string? DefaultValue { get; private set; }

    /// <summary>Binds a target field to a source column, a constant, or both.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="storeId">The owning store, where the batch has one.</param>
    /// <param name="importBatchId">The batch.</param>
    /// <param name="targetField">The target field.</param>
    /// <param name="sourceColumn">The source column, or <c>null</c> for a constant-only binding.</param>
    /// <param name="defaultValue">The constant, or <c>null</c>.</param>
    /// <returns>The new mapping.</returns>
    /// <exception cref="ImportRuleException">The binding names neither a column nor a constant.</exception>
    public static ImportColumnMapping Create(
        Guid tenantId,
        Guid? storeId,
        Guid importBatchId,
        string targetField,
        string? sourceColumn,
        string? defaultValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetField);

        if (string.IsNullOrWhiteSpace(sourceColumn) && string.IsNullOrWhiteSpace(defaultValue))
        {
            throw new ImportRuleException(
                "IMPORTS_MAPPING_BINDS_NOTHING",
                $"The mapping for '{targetField}' names neither a source column nor a default value, "
                + "so it would import every row as empty. Remove it, or give it one of the two.");
        }

        return new ImportColumnMapping(
            tenantId,
            storeId,
            importBatchId,
            targetField.Trim(),
            string.IsNullOrWhiteSpace(sourceColumn) ? null : sourceColumn.Trim(),
            defaultValue);
    }

    /// <summary>
    /// Reads this mapping's value out of a source row, falling back to the constant.
    /// </summary>
    /// <param name="rawValues">The row's cells, keyed by source column.</param>
    /// <returns>The value, or <c>null</c> when there is none and no default.</returns>
    public string? ValueFrom(IReadOnlyDictionary<string, string?> rawValues)
    {
        ArgumentNullException.ThrowIfNull(rawValues);

        if (SourceColumn is not null
            && rawValues.TryGetValue(SourceColumn, out string? cell)
            && !string.IsNullOrWhiteSpace(cell))
        {
            return cell;
        }

        return DefaultValue;
    }
}
