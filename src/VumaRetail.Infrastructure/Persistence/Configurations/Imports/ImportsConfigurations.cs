using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VumaRetail.Domain.Imports;

namespace VumaRetail.Infrastructure.Persistence.Configurations.Imports;

/// <summary>
/// The four <c>imports</c> tables (Stage 11), grouped in one file the way <c>sales</c>'s seven and
/// <c>pos</c>'s five are — one module's schema read as a unit.
/// </summary>
/// <remarks>
/// <para>
/// Every item, partner, price-list-line and ledger-entry reference in this schema is a plain
/// <see cref="Guid"/> column — <c>ImportRow.TargetEntityId</c> is deliberately untyped, because the
/// row it points at is in a different module's schema and changes with the target kind.
/// <c>CONVENTIONS.md</c> §2 forbids the cross-schema foreign key, and here the rule and the design
/// agree: a single typed reference could not describe five different targets anyway.
/// </para>
/// <para>
/// The <c>jsonb</c> columns are the module's defining choice and each earns it. A row's raw values are
/// whatever columns the shop's file happened to have — a shape no table can be declared for. Its
/// normalised values and errors are read as a whole row or not at all. Its before-image is opaque by
/// design (only the handler that wrote it reads it back), which is exactly what stops a later stage's
/// change to an entity's shape invalidating before-images already on disk.
/// </para>
/// </remarks>
internal sealed class ImportBatchConfiguration : EntityConfiguration<ImportBatch>
{
    private static readonly JsonSerializerOptions SerializerOptions = new();

    /// <summary>Matches the base configuration's principal columns. See the property remarks.</summary>
    private const int PrincipalLength = 128;

    private static readonly ValueComparer<IReadOnlyList<string>> StringListComparer = new(
        (left, right) => left != null && right != null && left.SequenceEqual(right, StringComparer.Ordinal),
        list => list.Aggregate(0, (hash, value) => HashCode.Combine(hash, StringComparer.Ordinal.GetHashCode(value))),
        list => list.ToList());

    protected override string Schema => Schemas.Imports;

    protected override string TableName => "import_batches";

    protected override void ConfigureEntity(EntityTypeBuilder<ImportBatch> builder)
    {
        builder.Property(batch => batch.BatchNumber).IsRequired().HasMaxLength(32);
        builder.Property(batch => batch.FileName).IsRequired().HasMaxLength(260);

        // Fixed 64: lower-case hex SHA-256 is always exactly that, and declaring the width is what
        // makes the unique index below a cheap one.
        builder.Property(batch => batch.ContentHash)
            .IsRequired()
            .HasMaxLength(64)
            .IsFixedLength();

        builder.Property(batch => batch.SizeBytes).IsRequired();
        builder.Property(batch => batch.Worksheet).HasMaxLength(128);

        // Stored as text (docs/DATA_MODEL.md §2): an enum persisted by ordinal turns a reordered
        // member into silently relabelled history — and a batch relabelled from Customers to Items
        // would make a rollback compensate against the wrong module.
        builder.Property(batch => batch.TargetKind)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(batch => batch.SourceFormat)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(batch => batch.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(batch => batch.DuplicateStrategy)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(batch => batch.CommittedAt);
        builder.Property(batch => batch.RolledBackAt);

        // Sized to match the base configuration's created_by/updated_by columns: these hold the same
        // kind of value — a `user:{uuid}` or `system:{component}` principal — and a narrower column
        // here would truncate the one field that answers "who did this to my prices".
        builder.Property(batch => batch.CommittedBy).HasMaxLength(PrincipalLength);
        builder.Property(batch => batch.RolledBackBy).HasMaxLength(PrincipalLength);
        builder.Property(batch => batch.RollbackReason).HasMaxLength(512);

        builder.Property(batch => batch.TotalRows).IsRequired();
        builder.Property(batch => batch.ValidRows).IsRequired();
        builder.Property(batch => batch.InvalidRows).IsRequired();
        builder.Property(batch => batch.CreatedRows).IsRequired();
        builder.Property(batch => batch.UpdatedRows).IsRequired();
        builder.Property(batch => batch.SkippedRows).IsRequired();

        // The headers in file order. An array rather than a child table because order is the whole
        // content — a header row is a sequence, and a child table would need a sequence column to say
        // so.
        PropertyBuilder<IReadOnlyList<string>> columns = builder.Property(batch => batch.SourceColumns)
            .HasColumnName("source_columns")
            .HasColumnType("jsonb")
            .IsRequired()
            .HasConversion(
                list => JsonSerializer.Serialize(list, SerializerOptions),
                json => DeserializeColumns(json));

        columns.Metadata.SetValueComparer(StringListComparer);

        builder.HasMany(batch => batch.Rows)
            .WithOne()
            .HasForeignKey(row => row.ImportBatchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(batch => batch.Mappings)
            .WithOne()
            .HasForeignKey(mapping => mapping.ImportBatchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(batch => batch.Rows).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(batch => batch.Mappings).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Both are LINQ over Rows, computed fresh on every read, and both must be invisible to EF.
        // Convention does not know that: it sees two more IReadOnlyList<ImportRow> properties and maps
        // each as its own collection navigation, which adds a second and third nullable foreign key to
        // import_rows and — far worse — hands the change tracker two collections it will try to keep
        // fixed up. They are arrays, so the first commit that moves a row between them dies inside
        // SaveChanges with "Collection was of a fixed size", from a stack trace that names the audit
        // interceptor and nothing about imports at all.
        builder.Ignore(batch => batch.CommittableRows);
        builder.Ignore(batch => batch.CompensatableRows);

        builder.HasIndex(batch => new { batch.TenantId, batch.BatchNumber })
            .IsUnique()
            .HasDatabaseName("ux_import_batches_tenant_id_batch_number")
            .HasFilter("deleted_at IS NULL");

        // The duplicate-file check, run once per upload. Partial on committed batches only, because
        // that is the only status the check cares about: uploading the same file twice is fine while
        // the first attempt is still a preview, and refusing it would strand somebody who discarded a
        // batch and started again.
        builder.HasIndex(batch => new { batch.TenantId, batch.ContentHash })
            .HasDatabaseName("ix_import_batches_tenant_id_content_hash_committed")
            .HasFilter("status = 'Committed' AND deleted_at IS NULL");

        // The history list, ordered by number then id — the keyset the list endpoint pages on.
        builder.HasIndex(batch => new { batch.TenantId, batch.TargetKind, batch.BatchNumber })
            .HasDatabaseName("ix_import_batches_tenant_id_target_kind_batch_number");

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_import_batches_counts_non_negative",
            "total_rows >= 0 AND valid_rows >= 0 AND invalid_rows >= 0 AND created_rows >= 0 "
            + "AND updated_rows >= 0 AND skipped_rows >= 0"));
    }

    private static IReadOnlyList<string> DeserializeColumns(string json)
        => JsonSerializer.Deserialize<List<string>>(json, SerializerOptions) ?? [];
}

/// <summary><c>imports.import_rows</c> — one source row and everything that happened to it.</summary>
internal sealed class ImportRowConfiguration : EntityConfiguration<ImportRow>
{
    private static readonly JsonSerializerOptions SerializerOptions = new();

    private static readonly ValueComparer<IReadOnlyDictionary<string, string?>> ValueMapComparer = new(
        (left, right) => AreEqual(left, right),
        map => HashOf(map),
        map => Snapshot(map));

    private static readonly ValueComparer<IReadOnlyList<ImportRowError>> ErrorListComparer = new(
        (left, right) => left != null && right != null && left.SequenceEqual(right),
        list => list.Aggregate(0, (hash, error) => HashCode.Combine(hash, error.GetHashCode())),
        list => list.ToList());

    protected override string Schema => Schemas.Imports;

    protected override string TableName => "import_rows";

    protected override void ConfigureEntity(EntityTypeBuilder<ImportRow> builder)
    {
        builder.Property(row => row.ImportBatchId).IsRequired();
        builder.Property(row => row.RowNumber).IsRequired();

        builder.Property(row => row.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(row => row.Outcome)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        // Untyped by design — the entity this points at is in another module's schema and its type
        // depends on the batch's target kind. See the file's remarks.
        builder.Property(row => row.TargetEntityId);
        builder.Property(row => row.CompensationEntityId);

        // Text rather than jsonb: nothing queries inside a before-image, only the handler that wrote
        // it reads it back, and jsonb would re-order its keys on the way through — which is fine for
        // a document and pointless overhead for an opaque blob.
        builder.Property(row => row.BeforeImage).HasColumnType("text");

        MapValues(builder, row => row.RawValues, "raw_values");
        MapValues(builder, row => row.NormalisedValues, "normalised_values");

        PropertyBuilder<IReadOnlyList<ImportRowError>> errors = builder.Property(row => row.Errors)
            .HasColumnName("errors")
            .HasColumnType("jsonb")
            .IsRequired()
            .HasConversion(
                list => JsonSerializer.Serialize(list, SerializerOptions),
                json => DeserializeErrors(json));

        errors.Metadata.SetValueComparer(ErrorListComparer);

        // The preview's keyset: one batch's rows in file order. Unique because two rows cannot share a
        // line number in one file, and saying so here is what makes a re-parse that duplicated a row
        // a database error rather than a preview showing the same line twice.
        builder.HasIndex(row => new { row.ImportBatchId, row.RowNumber })
            .IsUnique()
            .HasDatabaseName("ux_import_rows_import_batch_id_row_number")
            .HasFilter("deleted_at IS NULL");

        // The filtered preview — "show me the eleven bad rows out of forty thousand".
        builder.HasIndex(row => new { row.ImportBatchId, row.Status, row.RowNumber })
            .HasDatabaseName("ix_import_rows_import_batch_id_status_row_number");

        // A rollback's entry point, and an investigation's: "what did this import do to that item".
        builder.HasIndex(row => row.TargetEntityId)
            .HasDatabaseName("ix_import_rows_target_entity_id")
            .HasFilter("target_entity_id IS NOT NULL");

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_import_rows_row_number_after_header",
            "row_number >= 2"));
    }

    /// <summary>Maps one of the two value dictionaries to a <c>jsonb</c> column.</summary>
    /// <param name="builder">The entity type builder.</param>
    /// <param name="selector">Which dictionary.</param>
    /// <param name="columnName">The column.</param>
    private static void MapValues(
        EntityTypeBuilder<ImportRow> builder,
        System.Linq.Expressions.Expression<Func<ImportRow, IReadOnlyDictionary<string, string?>>> selector,
        string columnName)
    {
        PropertyBuilder<IReadOnlyDictionary<string, string?>> property = builder.Property(selector)
            .HasColumnName(columnName)
            .HasColumnType("jsonb")
            .IsRequired()
            .HasConversion(
                map => JsonSerializer.Serialize(map, SerializerOptions),
                json => DeserializeValues(json));

        property.Metadata.SetValueComparer(ValueMapComparer);
    }

    private static IReadOnlyDictionary<string, string?> DeserializeValues(string json)
    {
        Dictionary<string, string?>? map =
            JsonSerializer.Deserialize<Dictionary<string, string?>>(json, SerializerOptions);

        // Rebuilt case-insensitively rather than used as deserialised: a source column written
        // "SKU" in the header and "Sku" in a mapping is one column, and every lookup in the pipeline
        // assumes it can find it either way.
        return map is null
            ? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string?>(map, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Hashes a value map, order-independently.</summary>
    /// <param name="map">The map.</param>
    /// <remarks>
    /// Combined with addition rather than by folding <c>HashCode.Combine</c> over the enumeration,
    /// because a dictionary has no order and an order-sensitive hash would make two equal maps hash
    /// differently — which EF reads as a change and writes back on every save.
    /// </remarks>
    private static int HashOf(IReadOnlyDictionary<string, string?> map)
    {
        int hash = 0;

        foreach ((string key, string? value) in map)
        {
            hash += HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(key),
                value is null ? 0 : StringComparer.Ordinal.GetHashCode(value));
        }

        return hash;
    }

    /// <summary>Copies a value map, keeping its case-insensitive lookup.</summary>
    /// <param name="map">The map.</param>
    private static IReadOnlyDictionary<string, string?> Snapshot(IReadOnlyDictionary<string, string?> map)
        => new Dictionary<string, string?>(map.ToDictionary(pair => pair.Key, pair => pair.Value), StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<ImportRowError> DeserializeErrors(string json)
        => JsonSerializer.Deserialize<List<ImportRowError>>(json, SerializerOptions) ?? [];

    private static bool AreEqual(
        IReadOnlyDictionary<string, string?>? left, IReadOnlyDictionary<string, string?>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        foreach ((string key, string? value) in left)
        {
            if (!right.TryGetValue(key, out string? other)
                || !string.Equals(value, other, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary><c>imports.import_column_mappings</c> — one target field bound to one source column.</summary>
internal sealed class ImportColumnMappingConfiguration : EntityConfiguration<ImportColumnMapping>
{
    protected override string Schema => Schemas.Imports;

    protected override string TableName => "import_column_mappings";

    protected override void ConfigureEntity(EntityTypeBuilder<ImportColumnMapping> builder)
    {
        builder.Property(mapping => mapping.ImportBatchId).IsRequired();
        builder.Property(mapping => mapping.TargetField).IsRequired().HasMaxLength(64);
        builder.Property(mapping => mapping.SourceColumn).HasMaxLength(256);
        builder.Property(mapping => mapping.DefaultValue).HasMaxLength(512);

        builder.HasIndex(mapping => new { mapping.ImportBatchId, mapping.TargetField })
            .IsUnique()
            .HasDatabaseName("ux_import_column_mappings_batch_id_target_field")
            .HasFilter("deleted_at IS NULL");

        // The entity refuses a binding to neither, and so does the database. A mapping bound to
        // nothing imports every row as empty, and it is not the sort of thing that should depend on
        // which layer somebody happened to come in through.
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_import_column_mappings_binds_something",
            "source_column IS NOT NULL OR default_value IS NOT NULL"));
    }
}

/// <summary><c>imports.import_mapping_templates</c> — a saved mapping, matched on a header signature.</summary>
internal sealed class ImportMappingTemplateConfiguration : EntityConfiguration<ImportMappingTemplate>
{
    private static readonly JsonSerializerOptions SerializerOptions = new();

    private static readonly ValueComparer<IReadOnlyList<ImportTemplateBinding>> BindingListComparer = new(
        (left, right) => left != null && right != null && left.SequenceEqual(right),
        list => list.Aggregate(0, (hash, binding) => HashCode.Combine(hash, binding.GetHashCode())),
        list => list.ToList());

    protected override string Schema => Schemas.Imports;

    protected override string TableName => "import_mapping_templates";

    protected override void ConfigureEntity(EntityTypeBuilder<ImportMappingTemplate> builder)
    {
        builder.Property(template => template.Code).IsRequired().HasMaxLength(32);
        builder.Property(template => template.Name).IsRequired().HasMaxLength(128);

        builder.Property(template => template.TargetKind)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(template => template.SourceSignature)
            .IsRequired()
            .HasMaxLength(64)
            .IsFixedLength();

        builder.Property(template => template.IsActive).IsRequired();
        builder.Property(template => template.TimesUsed).IsRequired();

        // Bindings as one jsonb column rather than child rows: a template is copied onto a batch on
        // use and never read again during the import, so it has no need of the queryability child
        // rows buy, and keeping it one row makes applying a template one read.
        PropertyBuilder<IReadOnlyList<ImportTemplateBinding>> bindings =
            builder.Property(template => template.Bindings)
                .HasColumnName("bindings")
                .HasColumnType("jsonb")
                .IsRequired()
                .HasConversion(
                    list => JsonSerializer.Serialize(list, SerializerOptions),
                    json => DeserializeBindings(json));

        bindings.Metadata.SetValueComparer(BindingListComparer);

        builder.HasIndex(template => new { template.TenantId, template.Code })
            .IsUnique()
            .HasDatabaseName("ux_import_mapping_templates_tenant_id_code")
            .HasFilter("deleted_at IS NULL");

        // The auto-map lookup, run once per upload. Unique on the active set: two active templates
        // with one signature would leave which mapping wins up to row order, which is how a supplier's
        // file quietly starts importing under somebody else's bindings.
        builder.HasIndex(template => new { template.TenantId, template.TargetKind, template.SourceSignature })
            .IsUnique()
            .HasDatabaseName("ux_import_mapping_templates_signature_active")
            .HasFilter("is_active = true AND deleted_at IS NULL");
    }

    private static IReadOnlyList<ImportTemplateBinding> DeserializeBindings(string json)
        => JsonSerializer.Deserialize<List<ImportTemplateBinding>>(json, SerializerOptions) ?? [];
}
