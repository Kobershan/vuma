using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VumaRetail.Domain.Manufacturing;

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
