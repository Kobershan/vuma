using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VumaRetail.Domain.Warehouse;

namespace VumaRetail.Infrastructure.Persistence.Configurations.Warehouse;

/// <summary>The eleven <c>warehouse</c> tables (Stage 13), grouped in one file the way <c>inventory</c>'s six are.</summary>
internal sealed class ZoneConfiguration : EntityConfiguration<Zone>
{
    protected override string Schema => Schemas.Warehouse;

    protected override string TableName => "zones";

    protected override void ConfigureEntity(EntityTypeBuilder<Zone> builder)
    {
        builder.Property(zone => zone.LocationId).IsRequired();
        builder.Property(zone => zone.Code).IsRequired().HasMaxLength(32);
        builder.Property(zone => zone.Name).IsRequired().HasMaxLength(256);

        builder.Property(zone => zone.Type)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(zone => zone.IsActive).IsRequired();

        builder.HasIndex(zone => new { zone.LocationId, zone.Code })
            .IsUnique()
            .HasDatabaseName("ux_zones_location_id_code")
            .HasFilter("deleted_at IS NULL");
    }
}

/// <summary><c>warehouse.bins</c>.</summary>
internal sealed class BinConfiguration : EntityConfiguration<Bin>
{
    protected override string Schema => Schemas.Warehouse;

    protected override string TableName => "bins";

    protected override void ConfigureEntity(EntityTypeBuilder<Bin> builder)
    {
        builder.Property(bin => bin.LocationId).IsRequired();
        builder.Property(bin => bin.ZoneId).IsRequired();
        builder.Property(bin => bin.Code).IsRequired().HasMaxLength(32);
        builder.Property(bin => bin.Name).IsRequired().HasMaxLength(256);

        builder.Property(bin => bin.Type)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        // Plain nullable columns, not a complex property — EF Core 9 cannot configure a complex
        // property as optional (ADR-067). See Bin.CapacityValue's own remarks.
        builder.Property(bin => bin.CapacityValue).HasColumnName("capacity_value").HasColumnType(ValueObjectMapping.QuantityColumnType);
        builder.Property(bin => bin.CapacityUnitOfMeasure).HasColumnName("capacity_unit_of_measure").HasMaxLength(16);

        builder.Property(bin => bin.IsActive).IsRequired();

        builder.HasIndex(bin => bin.ZoneId).HasDatabaseName("ix_bins_zone_id");

        builder.HasIndex(bin => new { bin.LocationId, bin.Code })
            .IsUnique()
            .HasDatabaseName("ux_bins_location_id_code")
            .HasFilter("deleted_at IS NULL");
    }
}

/// <summary><c>warehouse.bin_stock</c> — the bin-level projection (node-local, ADR-069's reasoning applied one level down).</summary>
internal sealed class BinStockConfiguration : EntityConfiguration<BinStock>
{
    protected override string Schema => Schemas.Warehouse;

    protected override string TableName => "bin_stock";

    protected override void ConfigureEntity(EntityTypeBuilder<BinStock> builder)
    {
        builder.Property(stock => stock.BinId).IsRequired();
        builder.Property(stock => stock.ItemId);
        builder.Property(stock => stock.ItemVariantId);

        builder.HasQuantity(stock => stock.QuantityOnHand, "quantity_on_hand");
        builder.HasQuantity(stock => stock.QuantityReserved, "quantity_reserved");

        builder.HasIndex(stock => new { stock.BinId, stock.ItemId })
            .IsUnique()
            .HasDatabaseName("ux_bin_stock_bin_id_item_id")
            .HasFilter("item_id IS NOT NULL AND deleted_at IS NULL");

        builder.HasIndex(stock => new { stock.BinId, stock.ItemVariantId })
            .IsUnique()
            .HasDatabaseName("ux_bin_stock_bin_id_item_variant_id")
            .HasFilter("item_variant_id IS NOT NULL AND deleted_at IS NULL");

        // The allocator's hot read: "which bins at this location hold this SKU, largest first" —
        // IBinStockRepository.ListCandidatesAsync. No location column here (a bin's own location is a
        // join through Bin), so this indexes what the query actually filters on.
        builder.HasIndex(stock => stock.ItemId).HasDatabaseName("ix_bin_stock_item_id");
        builder.HasIndex(stock => stock.ItemVariantId).HasDatabaseName("ix_bin_stock_item_variant_id");

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_bin_stock_exactly_one_sku",
            "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int) = 1"));
    }
}

/// <summary><c>warehouse.bin_stock_movements</c> — the append-only bin-level ledger.</summary>
internal sealed class BinStockMovementConfiguration : EntityConfiguration<BinStockMovement>
{
    protected override string Schema => Schemas.Warehouse;

    protected override string TableName => "bin_stock_movements";

    protected override void ConfigureEntity(EntityTypeBuilder<BinStockMovement> builder)
    {
        builder.Property(movement => movement.BinId).IsRequired();
        builder.Property(movement => movement.ItemId);
        builder.Property(movement => movement.ItemVariantId);

        builder.Property(movement => movement.MovementType)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(24);

        builder.HasQuantity(movement => movement.Quantity, "quantity");

        builder.Property(movement => movement.ReferenceType)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(24);

        builder.Property(movement => movement.ReferenceId).IsRequired();
        builder.Property(movement => movement.Note).HasMaxLength(1000);

        builder.HasIndex(movement => new { movement.BinId, movement.CreatedAt })
            .HasDatabaseName("ix_bin_stock_movements_bin_id_created_at");

        builder.HasIndex(movement => movement.ReferenceId)
            .HasDatabaseName("ix_bin_stock_movements_reference_id");

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_bin_stock_movements_exactly_one_sku",
                "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int) = 1");

            table.HasCheckConstraint("ck_bin_stock_movements_quantity_non_zero", "quantity_value <> 0");
        });
    }
}

/// <summary><c>warehouse.putaway_tasks</c>.</summary>
internal sealed class PutawayTaskConfiguration : EntityConfiguration<PutawayTask>
{
    protected override string Schema => Schemas.Warehouse;

    protected override string TableName => "putaway_tasks";

    protected override void ConfigureEntity(EntityTypeBuilder<PutawayTask> builder)
    {
        builder.Property(task => task.LocationId).IsRequired();
        builder.Property(task => task.ItemId);
        builder.Property(task => task.ItemVariantId);

        builder.HasQuantity(task => task.Quantity, "quantity");
        builder.HasQuantity(task => task.ConfirmedQuantity, "confirmed_quantity");

        builder.Property(task => task.SourceReferenceType)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(24);

        builder.Property(task => task.SourceReferenceId);
        builder.Property(task => task.SuggestedBinId);
        builder.Property(task => task.ConfirmedBinId);
        builder.Property(task => task.ConfirmedAt);

        builder.Property(task => task.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.HasIndex(task => new { task.TenantId, task.LocationId })
            .HasDatabaseName("ix_putaway_tasks_pending_by_location")
            .HasFilter("status = 'Pending' AND deleted_at IS NULL");

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_putaway_tasks_exactly_one_sku",
            "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int) = 1"));
    }
}

/// <summary><c>warehouse.pick_waves</c>.</summary>
internal sealed class PickWaveConfiguration : EntityConfiguration<PickWave>
{
    protected override string Schema => Schemas.Warehouse;

    protected override string TableName => "pick_waves";

    protected override void ConfigureEntity(EntityTypeBuilder<PickWave> builder)
    {
        builder.Property(wave => wave.LocationId).IsRequired();

        builder.Property(wave => wave.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(wave => wave.ReleasedAt);
        builder.Property(wave => wave.PickedAt);
        builder.Property(wave => wave.PackedAt);
        builder.Property(wave => wave.ShippedAt);

        builder.HasIndex(wave => wave.LocationId).HasDatabaseName("ix_pick_waves_location_id");
    }
}

/// <summary><c>warehouse.pick_tasks</c>.</summary>
internal sealed class PickTaskConfiguration : EntityConfiguration<PickTask>
{
    protected override string Schema => Schemas.Warehouse;

    protected override string TableName => "pick_tasks";

    protected override void ConfigureEntity(EntityTypeBuilder<PickTask> builder)
    {
        builder.Property(task => task.PickWaveId).IsRequired();
        builder.Property(task => task.ItemId);
        builder.Property(task => task.ItemVariantId);

        builder.HasQuantity(task => task.RequestedQuantity, "requested_quantity");

        // Plain nullable columns, not complex properties — see BinConfiguration's identical note
        // (ADR-067). Both share RequestedQuantity's own unit of measure, so no separate uom column.
        builder.Property(task => task.AllocatedQuantityValue).HasColumnName("allocated_quantity_value").HasColumnType(ValueObjectMapping.QuantityColumnType);
        builder.Property(task => task.PickedQuantityValue).HasColumnName("picked_quantity_value").HasColumnType(ValueObjectMapping.QuantityColumnType);

        builder.Property(task => task.OutboundReference).IsRequired().HasMaxLength(128);
        builder.Property(task => task.AllocatedBinId);

        builder.Property(task => task.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.HasIndex(task => task.PickWaveId).HasDatabaseName("ix_pick_tasks_pick_wave_id");
        builder.HasIndex(task => task.AllocatedBinId).HasDatabaseName("ix_pick_tasks_allocated_bin_id");

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_pick_tasks_exactly_one_sku",
            "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int) = 1"));
    }
}

/// <summary><c>warehouse.pack_tasks</c>.</summary>
internal sealed class PackTaskConfiguration : EntityConfiguration<PackTask>
{
    protected override string Schema => Schemas.Warehouse;

    protected override string TableName => "pack_tasks";

    protected override void ConfigureEntity(EntityTypeBuilder<PackTask> builder)
    {
        builder.Property(task => task.PickWaveId).IsRequired();
        builder.Property(task => task.PackageCount).IsRequired();
        builder.Property(task => task.Note).HasMaxLength(1000);
        builder.Property(task => task.PackedAt).IsRequired();

        builder.HasIndex(task => task.PickWaveId)
            .IsUnique()
            .HasDatabaseName("ux_pack_tasks_pick_wave_id")
            .HasFilter("deleted_at IS NULL");
    }
}

/// <summary><c>warehouse.shipment_confirmations</c>.</summary>
internal sealed class ShipmentConfirmationConfiguration : EntityConfiguration<ShipmentConfirmation>
{
    protected override string Schema => Schemas.Warehouse;

    protected override string TableName => "shipment_confirmations";

    protected override void ConfigureEntity(EntityTypeBuilder<ShipmentConfirmation> builder)
    {
        builder.Property(shipment => shipment.PickWaveId).IsRequired();
        builder.Property(shipment => shipment.Carrier).HasMaxLength(128);
        builder.Property(shipment => shipment.TrackingNumber).HasMaxLength(128);
        builder.Property(shipment => shipment.ShippedAt).IsRequired();

        builder.HasIndex(shipment => shipment.PickWaveId)
            .IsUnique()
            .HasDatabaseName("ux_shipment_confirmations_pick_wave_id")
            .HasFilter("deleted_at IS NULL");
    }
}

/// <summary><c>warehouse.cycle_counts</c>.</summary>
internal sealed class CycleCountConfiguration : EntityConfiguration<CycleCount>
{
    protected override string Schema => Schemas.Warehouse;

    protected override string TableName => "cycle_counts";

    protected override void ConfigureEntity(EntityTypeBuilder<CycleCount> builder)
    {
        builder.Property(count => count.LocationId).IsRequired();
        builder.Property(count => count.ZoneId);
        builder.Property(count => count.ScheduledAt);
        builder.Property(count => count.FinalizedAt);

        builder.Property(count => count.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.HasIndex(count => new { count.TenantId, count.LocationId })
            .HasDatabaseName("ix_cycle_counts_open_by_location")
            .HasFilter("status = 'Open' AND deleted_at IS NULL");
    }
}

/// <summary><c>warehouse.cycle_count_lines</c>.</summary>
internal sealed class CycleCountLineConfiguration : EntityConfiguration<CycleCountLine>
{
    protected override string Schema => Schemas.Warehouse;

    protected override string TableName => "cycle_count_lines";

    protected override void ConfigureEntity(EntityTypeBuilder<CycleCountLine> builder)
    {
        builder.Property(line => line.CycleCountId).IsRequired();
        builder.Property(line => line.BinId).IsRequired();
        builder.Property(line => line.ItemId);
        builder.Property(line => line.ItemVariantId);

        builder.HasQuantity(line => line.SystemQuantity, "system_quantity");
        builder.HasQuantity(line => line.CountedQuantity, "counted_quantity");

        builder.HasIndex(line => line.CycleCountId).HasDatabaseName("ix_cycle_count_lines_cycle_count_id");

        builder.HasIndex(line => new { line.CycleCountId, line.BinId, line.ItemId })
            .IsUnique()
            .HasDatabaseName("ux_cycle_count_lines_count_bin_item_id")
            .HasFilter("item_id IS NOT NULL AND deleted_at IS NULL");

        builder.HasIndex(line => new { line.CycleCountId, line.BinId, line.ItemVariantId })
            .IsUnique()
            .HasDatabaseName("ux_cycle_count_lines_count_bin_item_variant_id")
            .HasFilter("item_variant_id IS NOT NULL AND deleted_at IS NULL");

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_cycle_count_lines_exactly_one_sku",
            "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int) = 1"));
    }
}
