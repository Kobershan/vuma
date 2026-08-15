using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VumaRetail.Domain.Catalog;

namespace VumaRetail.Infrastructure.Persistence.Configurations.Catalog;

/// <summary>
/// The four <c>catalog</c> tables (Stage 06), grouped in one file the way the six <c>identity</c>
/// tables are — one module's schema read as a unit.
/// </summary>
internal sealed class UnitOfMeasureConfiguration : EntityConfiguration<UnitOfMeasure>
{
    protected override string Schema => Schemas.Catalog;

    protected override string TableName => "units_of_measure";

    protected override void ConfigureEntity(EntityTypeBuilder<UnitOfMeasure> builder)
    {
        builder.Property(unit => unit.Code).IsRequired().HasMaxLength(16);
        builder.Property(unit => unit.Name).IsRequired().HasMaxLength(128);

        // Stored as text (docs/DATA_MODEL.md §2): an enum persisted by ordinal turns a reordered
        // member into silently relabelled history.
        builder.Property(unit => unit.Type)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(unit => unit.BaseUnitOfMeasureId);

        builder.Property(unit => unit.ConversionFactorToBase)
            .IsRequired()
            .HasColumnType("numeric(18,6)");

        builder.Property(unit => unit.IsActive).IsRequired();

        builder.HasIndex(unit => new { unit.TenantId, unit.Code })
            .IsUnique()
            .HasDatabaseName("ux_units_of_measure_tenant_id_code")
            .HasFilter("deleted_at IS NULL");
    }
}

/// <summary><c>catalog.items</c>.</summary>
internal sealed class ItemConfiguration : EntityConfiguration<Item>
{
    protected override string Schema => Schemas.Catalog;

    protected override string TableName => "items";

    protected override void ConfigureEntity(EntityTypeBuilder<Item> builder)
    {
        builder.Property(item => item.Code).IsRequired().HasMaxLength(32);
        builder.Property(item => item.Name).IsRequired().HasMaxLength(256);
        builder.Property(item => item.Description).HasMaxLength(2000);

        builder.Property(item => item.ItemType)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(item => item.UnitOfMeasureId).IsRequired();

        // A tax class is a string code, never a schema reference — CONVENTIONS.md §2 forbids the
        // cross-schema foreign key; Stage 07's posting rules engine decides what the code means.
        builder.Property(item => item.TaxClassCode).HasMaxLength(32);

        builder.Property(item => item.IsActive).IsRequired();
        builder.Property(item => item.HasVariants).IsRequired();

        builder.HasIndex(item => new { item.TenantId, item.Code })
            .IsUnique()
            .HasDatabaseName("ux_items_tenant_id_code")
            .HasFilter("deleted_at IS NULL");
    }
}

/// <summary><c>catalog.item_variants</c>.</summary>
internal sealed class ItemVariantConfiguration : EntityConfiguration<ItemVariant>
{
    private static readonly JsonSerializerOptions SerializerOptions = new();

    private static readonly ValueComparer<IReadOnlyList<VariantAttribute>> AttributeListComparer = new(
        (left, right) => AreEqual(left, right),
        list => list.Aggregate(0, (hash, attribute) => HashCode.Combine(hash, attribute.GetHashCode())),
        list => list.ToList());

    protected override string Schema => Schemas.Catalog;

    protected override string TableName => "item_variants";

    protected override void ConfigureEntity(EntityTypeBuilder<ItemVariant> builder)
    {
        builder.Property(variant => variant.ItemId).IsRequired();
        builder.Property(variant => variant.Sku).IsRequired().HasMaxLength(32);
        builder.Property(variant => variant.IsActive).IsRequired();

        // A value object stored as one ordered jsonb column rather than a child table — see
        // docs/DECISIONS.md for the ADR. Order matters (it is display order), which a JSON array
        // preserves for free and a child table would need its own sequence column to keep.
        PropertyBuilder<IReadOnlyList<VariantAttribute>> attributes = builder.Property(variant => variant.Attributes)
            .HasColumnName("attributes")
            .HasColumnType("jsonb")
            .IsRequired()
            .HasConversion(
                list => JsonSerializer.Serialize(list, SerializerOptions),
                json => Deserialize(json));

        attributes.Metadata.SetValueComparer(AttributeListComparer);

        builder.HasIndex(variant => new { variant.TenantId, variant.Sku })
            .IsUnique()
            .HasDatabaseName("ux_item_variants_tenant_id_sku")
            .HasFilter("deleted_at IS NULL");

        builder.HasIndex(variant => variant.ItemId)
            .HasDatabaseName("ix_item_variants_item_id");
    }

    private static IReadOnlyList<VariantAttribute> Deserialize(string json)
        => JsonSerializer.Deserialize<List<VariantAttribute>>(json, SerializerOptions) ?? [];

    private static bool AreEqual(IReadOnlyList<VariantAttribute>? left, IReadOnlyList<VariantAttribute>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return left.SequenceEqual(right);
    }
}

/// <summary><c>catalog.barcodes</c>.</summary>
internal sealed class BarcodeConfiguration : EntityConfiguration<Barcode>
{
    protected override string Schema => Schemas.Catalog;

    protected override string TableName => "barcodes";

    protected override void ConfigureEntity(EntityTypeBuilder<Barcode> builder)
    {
        builder.Property(barcode => barcode.Code).IsRequired().HasMaxLength(64);

        builder.Property(barcode => barcode.Symbology)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(barcode => barcode.ItemId);
        builder.Property(barcode => barcode.ItemVariantId);
        builder.Property(barcode => barcode.IsPrimary).IsRequired();

        // A scanner reads a code, not a type — unique tenant-wide regardless of symbology.
        builder.HasIndex(barcode => new { barcode.TenantId, barcode.Code })
            .IsUnique()
            .HasDatabaseName("ux_barcodes_tenant_id_code")
            .HasFilter("deleted_at IS NULL");

        builder.HasIndex(barcode => barcode.ItemId)
            .HasDatabaseName("ix_barcodes_item_id");

        builder.HasIndex(barcode => barcode.ItemVariantId)
            .HasDatabaseName("ix_barcodes_item_variant_id");

        // A database-level backstop for "exactly one primary per owner" alongside the domain's own
        // Barcode.SetPrimary bookkeeping — belt and braces on the one invariant a concurrent write from
        // two terminals could otherwise violate.
        builder.HasIndex(barcode => barcode.ItemId)
            .IsUnique()
            .HasDatabaseName("ux_barcodes_item_id_primary")
            .HasFilter("is_primary AND item_id IS NOT NULL AND deleted_at IS NULL");

        builder.HasIndex(barcode => barcode.ItemVariantId)
            .IsUnique()
            .HasDatabaseName("ux_barcodes_item_variant_id_primary")
            .HasFilter("is_primary AND item_variant_id IS NOT NULL AND deleted_at IS NULL");

        // Attaches to exactly one of an item or a variant — CONVENTIONS.md §2 forbids the cross-schema
        // foreign key, but this is a plain check constraint within one table.
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_barcodes_exactly_one_owner",
            "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int) = 1"));
    }
}
