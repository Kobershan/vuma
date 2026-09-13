using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VumaRetail.Domain.Manufacturing;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Infrastructure.Persistence.Configurations.Manufacturing;

/// <summary>Maps Stage 16 manufacturing definitions.</summary>
internal sealed class BillOfMaterialsConfiguration : EntityConfiguration<BillOfMaterials>
{
    private static readonly JsonSerializerOptions SerializerOptions = new();

    private static readonly ValueComparer<IReadOnlyList<BillOfMaterialsLine>> LinesComparer = new(
        (left, right) => (left ?? Array.Empty<BillOfMaterialsLine>()).SequenceEqual(right ?? Array.Empty<BillOfMaterialsLine>()),
        lines => lines.Aggregate(0, (hash, line) => HashCode.Combine(hash, line.GetHashCode())),
        lines => lines.ToList());

    private static readonly ValueComparer<IReadOnlyList<RoutingStep>> RoutingComparer = new(
        (left, right) => (left ?? Array.Empty<RoutingStep>()).SequenceEqual(right ?? Array.Empty<RoutingStep>()),
        steps => steps.Aggregate(0, (hash, step) => HashCode.Combine(hash, step.GetHashCode())),
        steps => steps.ToList());

    protected override string Schema => Schemas.Manufacturing;

    protected override string TableName => "bills_of_materials";

    protected override void ConfigureEntity(EntityTypeBuilder<BillOfMaterials> builder)
    {
        builder.Property(bom => bom.FinishedItemId).IsRequired();
        builder.Property(bom => bom.FinishedVariantId);
        builder.Property(bom => bom.Version).IsRequired();
        builder.Property(bom => bom.Name).IsRequired().HasMaxLength(256);
        builder.Property(bom => bom.Status).IsRequired().HasConversion<string>().HasMaxLength(16);

        PropertyBuilder<IReadOnlyList<BillOfMaterialsLine>> lines = builder.Property(bom => bom.Lines)
            .HasColumnName("lines")
            .HasColumnType("jsonb")
            .IsRequired()
            .HasConversion(
                value => JsonSerializer.Serialize(value, SerializerOptions),
                json => JsonSerializer.Deserialize<List<BillOfMaterialsLine>>(json, SerializerOptions) ?? new List<BillOfMaterialsLine>());
        lines.Metadata.SetValueComparer(LinesComparer);

        PropertyBuilder<IReadOnlyList<RoutingStep>> routing = builder.Property(bom => bom.RoutingSteps)
            .HasColumnName("routing_steps")
            .HasColumnType("jsonb")
            .IsRequired()
            .HasConversion(
                value => JsonSerializer.Serialize(value, SerializerOptions),
                json => JsonSerializer.Deserialize<List<RoutingStep>>(json, SerializerOptions) ?? new List<RoutingStep>());
        routing.Metadata.SetValueComparer(RoutingComparer);

        builder.HasIndex(bom => new { bom.TenantId, bom.FinishedItemId, bom.FinishedVariantId, bom.Version })
            .IsUnique()
            .HasDatabaseName("ux_bills_of_materials_tenant_finished_version")
            .HasFilter("deleted_at IS NULL");

        builder.HasIndex(bom => new { bom.TenantId, bom.FinishedItemId, bom.FinishedVariantId, bom.Status })
            .HasDatabaseName("ix_bills_of_materials_tenant_finished_status")
            .HasFilter("deleted_at IS NULL");
    }
}

/// <summary>Maps immutable release-time production order data.</summary>
internal sealed class ProductionOrderConfiguration : EntityConfiguration<ProductionOrder>
{
    private static readonly JsonSerializerOptions SerializerOptions = new();

    private static readonly ValueComparer<IReadOnlyList<ProductionMaterialRequirement>> MaterialsComparer = CreateComparer<ProductionMaterialRequirement>();
    private static readonly ValueComparer<IReadOnlyList<ProductionMaterialIssue>> IssuesComparer = CreateComparer<ProductionMaterialIssue>();
    private static readonly ValueComparer<IReadOnlyList<ProductionOutputReceipt>> ReceiptsComparer = CreateComparer<ProductionOutputReceipt>();
    private static readonly ValueComparer<IReadOnlyList<ProductionScrap>> ScrapComparer = CreateComparer<ProductionScrap>();

    private static ValueComparer<IReadOnlyList<T>> CreateComparer<T>()
        => new(
            (left, right) => (left ?? Array.Empty<T>()).SequenceEqual(right ?? Array.Empty<T>()),
            values => (values ?? Array.Empty<T>()).Aggregate(0, (hash, value) => HashCode.Combine(hash, value.GetHashCode())),
            values => (values ?? Array.Empty<T>()).ToList());

    protected override string Schema => Schemas.Manufacturing;

    protected override string TableName => "production_orders";

    protected override void ConfigureEntity(EntityTypeBuilder<ProductionOrder> builder)
    {
        builder.Property(order => order.CompanyId).IsRequired();
        builder.Property(order => order.FinishedItemId).IsRequired();
        builder.Property(order => order.BillOfMaterialsId).IsRequired();
        builder.Property(order => order.OrderNumber).IsRequired().HasMaxLength(64);
        builder.Property(order => order.Status).IsRequired().HasConversion<string>().HasMaxLength(16);
        builder.Property(order => order.PlannedQuantity).HasColumnName("planned_quantity").HasColumnType("jsonb")
            .HasConversion(
                quantity => JsonSerializer.Serialize(quantity, SerializerOptions),
                json => JsonSerializer.Deserialize<Quantity>(json, SerializerOptions));
        builder.Property(order => order.Snapshot).HasColumnType("jsonb").HasConversion(
            value => JsonSerializer.Serialize(value, SerializerOptions),
            json => JsonSerializer.Deserialize<ProductionSnapshot>(json, SerializerOptions));
        PropertyBuilder<IReadOnlyList<ProductionMaterialRequirement>> materials = builder.Property(order => order.Materials).HasColumnType("jsonb").IsRequired().HasConversion(
            value => JsonSerializer.Serialize(value, SerializerOptions),
            json => JsonSerializer.Deserialize<List<ProductionMaterialRequirement>>(json, SerializerOptions)
                ?? new List<ProductionMaterialRequirement>());
        materials.Metadata.SetValueComparer(MaterialsComparer);
        PropertyBuilder<IReadOnlyList<ProductionMaterialIssue>> issues = builder.Property(order => order.Issues).HasColumnName("material_issues").HasColumnType("jsonb").IsRequired().HasConversion(
            value => JsonSerializer.Serialize(value, SerializerOptions),
            json => JsonSerializer.Deserialize<List<ProductionMaterialIssue>>(json, SerializerOptions)
                ?? new List<ProductionMaterialIssue>());
        issues.Metadata.SetValueComparer(IssuesComparer);
        PropertyBuilder<IReadOnlyList<ProductionOutputReceipt>> receipts = builder.Property(order => order.Receipts).HasColumnName("output_receipts").HasColumnType("jsonb").IsRequired().HasConversion(
            value => JsonSerializer.Serialize(value, SerializerOptions),
            json => JsonSerializer.Deserialize<List<ProductionOutputReceipt>>(json, SerializerOptions)
                ?? new List<ProductionOutputReceipt>());
        receipts.Metadata.SetValueComparer(ReceiptsComparer);
        PropertyBuilder<IReadOnlyList<ProductionScrap>> scrap = builder.Property(order => order.Scrap).HasColumnName("scrap_records").HasColumnType("jsonb").IsRequired().HasConversion(
            value => JsonSerializer.Serialize(value, SerializerOptions),
            json => JsonSerializer.Deserialize<List<ProductionScrap>>(json, SerializerOptions)
                ?? new List<ProductionScrap>());
        scrap.Metadata.SetValueComparer(ScrapComparer);
        builder.HasIndex(order => new { order.TenantId, order.CompanyId, order.OrderNumber })
            .IsUnique().HasDatabaseName("ux_production_orders_tenant_company_number")
            .HasFilter("deleted_at IS NULL");
    }
}
