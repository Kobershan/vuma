using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Imports;

/// <summary>
/// A saved mapping, so the same supplier's file maps itself next month.
/// </summary>
/// <remarks>
/// <para>
/// This is the entity that turns the import from a chore into a routine. A shop receives the same
/// three files from the same three suppliers every month, in the same shape, forever. Making somebody
/// re-bind twelve columns each time is how an import feature ends up unused.
/// </para>
/// <para>
/// <see cref="SourceSignature"/> is what makes the match automatic: a hash of the normalised header
/// row (trimmed, lower-cased, sorted). Sorted deliberately — a supplier who reorders their columns
/// has not changed their file in any way that matters to a mapping, and re-binding twelve columns
/// because column 3 and column 4 swapped would be exactly the chore this exists to remove.
/// </para>
/// <para>
/// The bindings are stored as JSON rather than as child rows. A template is copied onto a batch on
/// use and never read again during the import, so it has no need of the queryability that child rows
/// buy, and keeping it one row means applying a template is one read.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.Bidirectional, ConflictPolicy.CloudWins)]
public sealed class ImportMappingTemplate : Entity
{
    private readonly List<ImportTemplateBinding> _bindings = [];

    private ImportMappingTemplate(
        Guid tenantId,
        string code,
        string name,
        ImportTargetKind targetKind,
        string sourceSignature,
        IReadOnlyList<ImportTemplateBinding> bindings)
        : base(tenantId)
    {
        Code = code;
        Name = name;
        TargetKind = targetKind;
        SourceSignature = sourceSignature;
        _bindings.AddRange(bindings);
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private ImportMappingTemplate()
    {
    }

    /// <summary>The template's unique code within the tenant, upper-cased.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>The template's name, as a manager would recognise it — "Makro monthly price list".</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>The target kind this template maps to.</summary>
    public ImportTargetKind TargetKind { get; private set; }

    /// <summary>The hash of the normalised header row this template was saved against.</summary>
    public string SourceSignature { get; private set; } = string.Empty;

    /// <summary>True while the template may be matched. Deactivate, not delete (§7 rule 8).</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>How many batches have used it — the only honest measure of whether it is worth keeping.</summary>
    public int TimesUsed { get; private set; }

    /// <summary>The bindings this template applies.</summary>
    public IReadOnlyList<ImportTemplateBinding> Bindings => _bindings;

    /// <summary>Saves a mapping for reuse.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="code">The template code, upper-cased.</param>
    /// <param name="name">The template's name.</param>
    /// <param name="targetKind">The target it maps to.</param>
    /// <param name="sourceColumns">The headers it was saved against.</param>
    /// <param name="bindings">The bindings.</param>
    /// <returns>The new template.</returns>
    /// <exception cref="ImportRuleException">The template binds nothing.</exception>
    public static ImportMappingTemplate Create(
        Guid tenantId,
        string code,
        string name,
        ImportTargetKind targetKind,
        IReadOnlyList<string> sourceColumns,
        IReadOnlyList<ImportTemplateBinding> bindings)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A mapping template must belong to a tenant.", nameof(tenantId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(sourceColumns);
        ArgumentNullException.ThrowIfNull(bindings);

        if (bindings.Count == 0)
        {
            throw new ImportRuleException(
                "IMPORTS_TEMPLATE_BINDS_NOTHING",
                "A mapping template with no bindings would map nothing.");
        }

        return new ImportMappingTemplate(
            tenantId,
            code.Trim().ToUpperInvariant(),
            name.Trim(),
            targetKind,
            SignatureOf(sourceColumns),
            bindings);
    }

    /// <summary>
    /// The signature of a header row: trimmed, lower-cased, sorted, joined, SHA-256, lower-case hex.
    /// </summary>
    /// <param name="sourceColumns">The headers, in any order.</param>
    /// <returns>The signature.</returns>
    /// <remarks>
    /// Sorting before hashing is the decision worth knowing about — see the type's remarks. Empty
    /// headers are dropped rather than hashed, because a trailing empty column is an artefact of how
    /// a spreadsheet was saved, not a property of the file's shape.
    /// </remarks>
    public static string SignatureOf(IReadOnlyList<string> sourceColumns)
    {
        ArgumentNullException.ThrowIfNull(sourceColumns);

        // ASCII unit separator rather than a comma: a header that itself contains a comma would
        // otherwise let two genuinely different header rows hash to the same signature, and a
        // template would silently apply to a file it was never saved against.
        string canonical = string.Join(
            '\u001f',
            sourceColumns
                .Where(column => !string.IsNullOrWhiteSpace(column))
                .Select(column => column.Trim().ToLowerInvariant())
                .OrderBy(column => column, StringComparer.Ordinal));

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>True when this template applies to a file with these headers.</summary>
    /// <param name="sourceColumns">The headers the reader found.</param>
    /// <param name="targetKind">The target the batch is aimed at.</param>
    public bool Matches(IReadOnlyList<string> sourceColumns, ImportTargetKind targetKind)
        => IsActive
            && TargetKind == targetKind
            && string.Equals(SourceSignature, SignatureOf(sourceColumns), StringComparison.Ordinal);

    /// <summary>Replaces the bindings and the signature this template matches on.</summary>
    /// <param name="name">The new name.</param>
    /// <param name="sourceColumns">The headers it now matches.</param>
    /// <param name="bindings">The new bindings.</param>
    /// <exception cref="ImportRuleException">The template would bind nothing.</exception>
    public void Amend(
        string name, IReadOnlyList<string> sourceColumns, IReadOnlyList<ImportTemplateBinding> bindings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(sourceColumns);
        ArgumentNullException.ThrowIfNull(bindings);

        if (bindings.Count == 0)
        {
            throw new ImportRuleException(
                "IMPORTS_TEMPLATE_BINDS_NOTHING",
                "A mapping template with no bindings would map nothing.");
        }

        Name = name.Trim();
        SourceSignature = SignatureOf(sourceColumns);
        _bindings.Clear();
        _bindings.AddRange(bindings);
    }

    /// <summary>Counts one use.</summary>
    public void RecordUse() => TimesUsed++;

    /// <summary>Retires the template from matching.</summary>
    public void Deactivate() => IsActive = false;

    /// <summary>Brings a retired template back into use.</summary>
    public void Activate() => IsActive = true;

    /// <summary>This template's bindings as batch mappings.</summary>
    /// <param name="storeId">The batch's store.</param>
    /// <param name="importBatchId">The batch.</param>
    /// <returns>One mapping per binding.</returns>
    public IReadOnlyList<ImportColumnMapping> ToMappings(Guid? storeId, Guid importBatchId)
        => _bindings
            .Select(binding => ImportColumnMapping.Create(
                TenantId, storeId, importBatchId, binding.TargetField, binding.SourceColumn, binding.DefaultValue))
            .ToArray();

    /// <summary>The template's use count as text, for a log line.</summary>
    public override string ToString()
        => $"{Code} ({TargetKind}, used {TimesUsed.ToString(CultureInfo.InvariantCulture)}x)";
}

/// <summary>One binding inside a saved template.</summary>
/// <param name="TargetField">The target field.</param>
/// <param name="SourceColumn">The source column, or <c>null</c> for a constant-only binding.</param>
/// <param name="DefaultValue">The constant, or <c>null</c>.</param>
public sealed record ImportTemplateBinding(string TargetField, string? SourceColumn, string? DefaultValue);
