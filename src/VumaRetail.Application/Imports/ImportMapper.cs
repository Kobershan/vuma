using VumaRetail.Application.Abstractions.Imports;
using VumaRetail.Domain.Imports;

namespace VumaRetail.Application.Imports;

/// <summary>
/// Works out, without asking anybody, which source column means which target field.
/// </summary>
/// <remarks>
/// <para>
/// This is the difference between an import that takes ten seconds and one that takes ten minutes per
/// file, and shops receive the same files every month forever. Two mechanisms, tried in order:
/// </para>
/// <para>
/// <b>A saved template</b>, matched on the hash of the normalised header row. A file that has been
/// imported before maps itself completely, including the constant defaults somebody set for columns
/// the file does not have — which alias matching can never reproduce, because a missing column has no
/// header to match on.
/// </para>
/// <para>
/// <b>Alias matching</b>, otherwise. Each target field carries the header texts that mean it, and the
/// comparison strips everything that is not a letter or a digit, so <c>Unit Price</c>,
/// <c>unit_price</c> and <c>UNIT-PRICE</c> are one header.
/// </para>
/// <para>
/// <b>What it will not do is guess.</b> Where two source columns both match one field, neither is
/// bound and the field is left for a person — binding the first would be a coin toss that looks like a
/// decision. Auto-mapping is allowed to leave required fields unbound; the transition to
/// <c>Mapped</c> is what refuses that, and it refuses it by name.
/// </para>
/// </remarks>
public static class ImportMapper
{
    /// <summary>Binds what can be bound by header alias.</summary>
    /// <param name="target">The target's field catalogue.</param>
    /// <param name="sourceColumns">The headers the reader found.</param>
    /// <returns>One binding per unambiguously matched field, in catalogue order.</returns>
    public static IReadOnlyList<ImportTemplateBinding> AutoMap(
        ImportTargetDescriptor target, IReadOnlyList<string> sourceColumns)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(sourceColumns);

        List<ImportTemplateBinding> bindings = [];
        HashSet<string> claimed = new(StringComparer.OrdinalIgnoreCase);

        // Required fields first, so that where a column could serve two fields it goes to the one the
        // import cannot proceed without. A "code" column matching both `code` and an optional
        // `supplierCode` should bind the required one.
        foreach (ImportFieldDescriptor field in target.Fields.OrderByDescending(field => field.IsRequired))
        {
            string[] matches = sourceColumns
                .Where(column => !claimed.Contains(column) && field.MatchesHeader(column))
                .ToArray();

            if (matches.Length != 1)
            {
                continue;
            }

            claimed.Add(matches[0]);
            bindings.Add(new ImportTemplateBinding(field.Name, matches[0], null));
        }

        return [.. bindings.OrderBy(binding =>
            target.Fields.ToList().FindIndex(field
                => string.Equals(field.Name, binding.TargetField, StringComparison.Ordinal)))];
    }

    /// <summary>The target fields a mapping leaves bound to nothing.</summary>
    /// <param name="target">The target's field catalogue.</param>
    /// <param name="bindings">The bindings.</param>
    /// <returns>The names of the required fields with no column and no constant.</returns>
    public static IReadOnlyList<string> UnmappedRequiredFields(
        ImportTargetDescriptor target, IReadOnlyList<ImportTemplateBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(bindings);

        return
        [
            .. target.Fields
                .Where(field => field.IsRequired)
                .Where(field => !bindings.Any(binding
                    => string.Equals(binding.TargetField, field.Name, StringComparison.OrdinalIgnoreCase)))
                .Select(field => field.Name),
        ];
    }

    /// <summary>The names of every field a mapping binds that the target does not have.</summary>
    /// <param name="target">The target's field catalogue.</param>
    /// <param name="bindings">The bindings.</param>
    /// <returns>The unrecognised field names, in the order they were bound.</returns>
    /// <remarks>
    /// Caught before the batch is touched rather than at commit, because a mapping naming
    /// <c>unitprice</c> where the field is <c>unitPrice</c> would otherwise validate every row as
    /// missing a required price and leave somebody looking at their spreadsheet for the fault.
    /// </remarks>
    public static IReadOnlyList<string> UnknownTargetFields(
        ImportTargetDescriptor target, IReadOnlyList<ImportTemplateBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(bindings);

        return
        [
            .. bindings
                .Where(binding => !target.Fields.Any(field
                    => string.Equals(field.Name, binding.TargetField, StringComparison.OrdinalIgnoreCase)))
                .Select(binding => binding.TargetField),
        ];
    }

    /// <summary>The field a binding names, matched case-insensitively.</summary>
    /// <param name="target">The target's field catalogue.</param>
    /// <param name="fieldName">The field name a binding carries.</param>
    /// <returns>The descriptor, or <c>null</c> when the target has no such field.</returns>
    public static ImportFieldDescriptor? FieldNamed(ImportTargetDescriptor target, string fieldName)
    {
        ArgumentNullException.ThrowIfNull(target);

        return target.Fields.FirstOrDefault(field
            => string.Equals(field.Name, fieldName, StringComparison.OrdinalIgnoreCase));
    }
}
