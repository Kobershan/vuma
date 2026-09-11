using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VumaRetail.Domain.Inventory;

namespace VumaRetail.Infrastructure.Persistence.Configurations.Inventory;

/// <summary>
/// The six <c>inventory</c> tables (Stage 08), grouped in one file the way <c>catalog</c>'s four and
/// <c>identity</c>'s six are — one module's schema read as a unit.
/// </summary>
/// <remarks>
/// Every item/variant reference below is a plain <see cref="Guid"/> column with an index, never a
/// foreign key into <c>catalog</c> — <c>CONVENTIONS.md</c> §2 forbids the cross-schema foreign key, and
/// the application layer validates the reference instead (<c>StockKeepingUnitResolver</c>). The
/// "exactly one of item or variant" rule each of these shares is enforced twice: by
/// <c>StockItemReference.Validate</c> in the domain, and by a check constraint here, the same
/// belt-and-braces shape <c>BarcodeConfiguration</c> uses.
/// </remarks>
internal sealed class StockLocationConfiguration : EntityConfiguration<StockLocation>
{
    protected override string Schema => Schemas.Inventory;

    protected override string TableName => "stock_locations";

    protected override void ConfigureEntity(EntityTypeBuilder<StockLocation> builder)
    {
        builder.Property(location => location.Code).IsRequired().HasMaxLength(32);
        builder.Property(location => location.Name).IsRequired().HasMaxLength(256);

        // Stored as text (docs/DATA_MODEL.md §2): an enum persisted by ordinal turns a reordered
        // member into silently relabelled history.
        builder.Property(location => location.Type)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(location => location.IsActive).IsRequired();

        builder.HasIndex(location => new { location.TenantId, location.Code })
            .IsUnique()
            .HasDatabaseName("ux_stock_locations_tenant_id_code")
            .HasFilter("deleted_at IS NULL");
    }
}

/// <summary><c>inventory.stock_ledger_entries</c> — the append-only ledger (ADR-005).</summary>
internal sealed class StockLedgerEntryConfiguration : EntityConfiguration<StockLedgerEntry>
{
    protected override string Schema => Schemas.Inventory;

    protected override string TableName => "stock_ledger_entries";

    protected override void ConfigureEntity(EntityTypeBuilder<StockLedgerEntry> builder)
    {
        builder.Property(entry => entry.LocationId).IsRequired();

        // Nullable, added in Stage 13, additively — see BinId's own remarks. No migration touches a
        // row that predates it; every historical entry simply reads null here.
        builder.Property(entry => entry.BinId);

        builder.Property(entry => entry.ItemId);
        builder.Property(entry => entry.ItemVariantId);

        builder.Property(entry => entry.MovementType)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(24);

        builder.HasQuantity(entry => entry.Quantity, "quantity");
        builder.HasMoney(entry => entry.UnitCost, "unit_cost");

        builder.Property(entry => entry.ReferenceType)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(entry => entry.ReferenceId);

        builder.Property(entry => entry.ReasonCode)
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(entry => entry.Note).HasMaxLength(1000);
        builder.Property(entry => entry.BatchReference).HasMaxLength(128);
        builder.Property(entry => entry.ExpiryDate);
        builder.Property(entry => entry.SerialNumber).HasMaxLength(128);

        builder.HasIndex(entry => new { entry.LocationId, entry.ItemId, entry.BatchReference, entry.ExpiryDate, entry.SerialNumber })
            .HasDatabaseName("ix_stock_ledger_entries_tracking_item")
            .HasFilter("batch_reference IS NOT NULL OR expiry_date IS NOT NULL OR serial_number IS NOT NULL");

        // The module's hot read: "what has moved for this stock-keeping unit at this location, newest
        // first" — the keyset page IStockLedgerRepository.ListPageAsync serves. Ordered (created_at,
        // id) descending to match the cursor's own comparison pair exactly, so the page boundary is an
        // index seek rather than a sort of everything the location has ever done.
        builder.HasIndex(entry => new { entry.LocationId, entry.CreatedAt, entry.Id })
            .HasDatabaseName("ix_stock_ledger_entries_location_id_created_at_id")
            .IsDescending(false, true, true);

        builder.HasIndex(entry => entry.BinId)
            .HasDatabaseName("ix_stock_ledger_entries_bin_id")
            .HasFilter("bin_id IS NOT NULL");

        builder.HasIndex(entry => entry.ItemId)
            .HasDatabaseName("ix_stock_ledger_entries_item_id");

        builder.HasIndex(entry => entry.ItemVariantId)
            .HasDatabaseName("ix_stock_ledger_entries_item_variant_id");

        // "Show me both sides of this transfer" / "show me every variance this stocktake posted" —
        // the correlation StockTransfer and StocktakeSession are read back through.
        builder.HasIndex(entry => entry.ReferenceId)
            .HasDatabaseName("ix_stock_ledger_entries_reference_id")
            .HasFilter("reference_id IS NOT NULL");

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_stock_ledger_entries_exactly_one_sku",
                "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int) = 1");

            // A zero-quantity row is not a movement (StockLedgerEntry.Post refuses it). Asserted here
            // too because this table is append-only: a bad row can never be corrected by an update,
            // only compensated by another row, so it is worth refusing at the boundary.
            table.HasCheckConstraint(
                "ck_stock_ledger_entries_quantity_non_zero",
                "quantity_value <> 0");
        });
    }
}

/// <summary><c>inventory.stock_balances</c> — the projection the ledger sums to (ADR-005, ADR-069).</summary>
internal sealed class StockBalanceConfiguration : EntityConfiguration<StockBalance>
{
    protected override string Schema => Schemas.Inventory;

    protected override string TableName => "stock_balances";

    protected override void ConfigureEntity(EntityTypeBuilder<StockBalance> builder)
    {
        builder.Property(balance => balance.LocationId).IsRequired();
        builder.Property(balance => balance.ItemId);
        builder.Property(balance => balance.ItemVariantId);

        builder.HasQuantity(balance => balance.QuantityOnHand, "quantity_on_hand");
        builder.HasMoney(balance => balance.AverageCost, "average_cost");

        // One balance per location per stock-keeping unit — the invariant StockLedgerPoster relies on
        // when it looks a balance up and opens one if it finds none. Two concurrent first receipts for
        // the same pair would otherwise each open their own row and the projection would silently
        // report half the stock. Two partial indexes rather than one over both nullable columns,
        // because PostgreSQL treats NULLs as distinct in a unique index and the pair (loc, item, NULL)
        // would not collide with itself.
        builder.HasIndex(balance => new { balance.LocationId, balance.ItemId })
            .IsUnique()
            .HasDatabaseName("ux_stock_balances_location_id_item_id")
            .HasFilter("item_id IS NOT NULL AND deleted_at IS NULL");

        builder.HasIndex(balance => new { balance.LocationId, balance.ItemVariantId })
            .IsUnique()
            .HasDatabaseName("ux_stock_balances_location_id_item_variant_id")
            .HasFilter("item_variant_id IS NOT NULL AND deleted_at IS NULL");

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_stock_balances_exactly_one_sku",
            "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int) = 1"));
    }
}

/// <summary><c>inventory.stock_transfers</c> — the document correlating a transfer's two ledger entries.</summary>
internal sealed class StockTransferConfiguration : EntityConfiguration<StockTransfer>
{
    protected override string Schema => Schemas.Inventory;

    protected override string TableName => "stock_transfers";

    protected override void ConfigureEntity(EntityTypeBuilder<StockTransfer> builder)
    {
        builder.Property(transfer => transfer.SourceLocationId).IsRequired();
        builder.Property(transfer => transfer.DestinationLocationId).IsRequired();
        builder.Property(transfer => transfer.ItemId);
        builder.Property(transfer => transfer.ItemVariantId);

        builder.HasQuantity(transfer => transfer.Quantity, "quantity");
        builder.HasMoney(transfer => transfer.UnitCost, "unit_cost");

        builder.Property(transfer => transfer.OutEntryId).IsRequired();
        builder.Property(transfer => transfer.InEntryId).IsRequired();
        builder.Property(transfer => transfer.Note).HasMaxLength(1000);

        builder.HasIndex(transfer => transfer.SourceLocationId)
            .HasDatabaseName("ix_stock_transfers_source_location_id");

        builder.HasIndex(transfer => transfer.DestinationLocationId)
            .HasDatabaseName("ix_stock_transfers_destination_location_id");

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_stock_transfers_exactly_one_sku",
                "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int) = 1");

            table.HasCheckConstraint(
                "ck_stock_transfers_locations_differ",
                "source_location_id <> destination_location_id");
        });
    }
}

/// <summary><c>inventory.stocktake_sessions</c>.</summary>
internal sealed class StocktakeSessionConfiguration : EntityConfiguration<StocktakeSession>
{
    protected override string Schema => Schemas.Inventory;

    protected override string TableName => "stocktake_sessions";

    protected override void ConfigureEntity(EntityTypeBuilder<StocktakeSession> builder)
    {
        builder.Property(session => session.LocationId).IsRequired();

        builder.Property(session => session.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(session => session.FinalizedAt);

        builder.HasIndex(session => session.LocationId)
            .HasDatabaseName("ix_stocktake_sessions_location_id");

        // "Is a count already running here?" — the question a second operator opening a session at the
        // same location needs answered, and a partial index is what makes it cheap once a location has
        // years of finalized sessions behind it.
        builder.HasIndex(session => new { session.TenantId, session.LocationId })
            .HasDatabaseName("ix_stocktake_sessions_open_by_location")
            .HasFilter("status = 'Open' AND deleted_at IS NULL");
    }
}

/// <summary><c>inventory.stocktake_lines</c>.</summary>
internal sealed class StocktakeLineConfiguration : EntityConfiguration<StocktakeLine>
{
    protected override string Schema => Schemas.Inventory;

    protected override string TableName => "stocktake_lines";

    protected override void ConfigureEntity(EntityTypeBuilder<StocktakeLine> builder)
    {
        builder.Property(line => line.StocktakeSessionId).IsRequired();
        builder.Property(line => line.ItemId);
        builder.Property(line => line.ItemVariantId);

        builder.HasQuantity(line => line.SystemQuantity, "system_quantity");
        builder.HasQuantity(line => line.CountedQuantity, "counted_quantity");

        builder.HasIndex(line => line.StocktakeSessionId)
            .HasDatabaseName("ix_stocktake_lines_stocktake_session_id");

        // One line per stock-keeping unit per session — a second count of the same item is a recount
        // of the existing line (StocktakeLine.Recount), never a second row that would double its
        // variance when the session is finalized. Split by nullable column for the same reason
        // stock_balances is.
        builder.HasIndex(line => new { line.StocktakeSessionId, line.ItemId })
            .IsUnique()
            .HasDatabaseName("ux_stocktake_lines_session_id_item_id")
            .HasFilter("item_id IS NOT NULL AND deleted_at IS NULL");

        builder.HasIndex(line => new { line.StocktakeSessionId, line.ItemVariantId })
            .IsUnique()
            .HasDatabaseName("ux_stocktake_lines_session_id_item_variant_id")
            .HasFilter("item_variant_id IS NOT NULL AND deleted_at IS NULL");

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_stocktake_lines_exactly_one_sku",
            "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int) = 1"));
    }
}

/// <summary><c>inventory.stock_reservations</c> — the append-only hold ledger (Stage 08c, ADR-103).</summary>
internal sealed class StockReservationConfiguration : EntityConfiguration<StockReservation>
{
    protected override string Schema => Schemas.Inventory;

    protected override string TableName => "stock_reservations";

    protected override void ConfigureEntity(EntityTypeBuilder<StockReservation> builder)
    {
        builder.Property(reservation => reservation.LocationId).IsRequired();
        builder.Property(reservation => reservation.ItemId);
        builder.Property(reservation => reservation.ItemVariantId);

        builder.HasQuantity(reservation => reservation.Quantity, "quantity");

        builder.Property(reservation => reservation.Source)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(24);

        builder.Property(reservation => reservation.SourceDocumentId).IsRequired();
        builder.Property(reservation => reservation.GroupDocumentRef).HasMaxLength(64);

        builder.Property(reservation => reservation.ReservationId).IsRequired();
        builder.Property(reservation => reservation.SequenceNumber).IsRequired();

        builder.Property(reservation => reservation.State)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(reservation => reservation.ExpiresAt);
        builder.Property(reservation => reservation.IntentId);
        builder.Property(reservation => reservation.LegId);
        builder.Property(reservation => reservation.ConsumedByReferenceId);
        builder.Property(reservation => reservation.Reason).HasMaxLength(500);
        builder.Property(reservation => reservation.BatchReference).HasMaxLength(128);
        builder.Property(reservation => reservation.ExpiryDate);
        builder.Property(reservation => reservation.SerialNumber).HasMaxLength(128);

        // Live holds for one stock-keeping unit at one location — the re-check's working set.
        builder.HasIndex(reservation => new { reservation.LocationId, reservation.ItemId, reservation.State })
            .HasDatabaseName("ix_stock_reservations_location_id_item_id_state");

        builder.HasIndex(reservation => new { reservation.LocationId, reservation.ItemVariantId, reservation.State })
            .HasDatabaseName("ix_stock_reservations_location_id_item_variant_id_state");

        builder.HasIndex(reservation => new
        {
            reservation.LocationId,
            reservation.ItemId,
            reservation.ItemVariantId,
            reservation.BatchReference,
            reservation.ExpiryDate,
            reservation.SerialNumber,
            reservation.State
        }).HasDatabaseName("ix_stock_reservations_tracking_state");

        // One chain's history.
        builder.HasIndex(reservation => new { reservation.ReservationId, reservation.SequenceNumber })
            .HasDatabaseName("ix_stock_reservations_reservation_id_sequence_number");

        // The expiry job's working set: live holds past their time, oldest first.
        builder.HasIndex(reservation => new { reservation.TenantId, reservation.State, reservation.ExpiresAt })
            .HasDatabaseName("ix_stock_reservations_expiry")
            .HasFilter("state = 'Held' AND expires_at IS NOT NULL");

        // Exactly one live row per logical reservation: a chain is a hold (sequence 0) plus at
        // most one terminal row (sequence 1), so a second Held row for the same ReservationId is
        // always a bug. Partial so closed chains cost nothing.
        builder.HasIndex(reservation => new { reservation.ReservationId, reservation.State })
            .IsUnique()
            .HasDatabaseName("ux_stock_reservations_open")
            .HasFilter("state = 'Held'");

        // Saga idempotency (ADR-116): retrying a leg replays its (intent, leg, line), which
        // collides here instead of double-holding. A leg holds at most one row per line — a
        // re-sourced remainder is a plain hold under the same group reference, never a second row
        // on the leg's key — so the key covers the line's identity, not the chain sequence.
        // Split in two like every other per-SKU unique in this schema, because PostgreSQL treats
        // NULLs as distinct and one index over both nullable columns would not collide with itself.
        // Partial on live holds (Stage 09b fix): the terminal row that closes a chain carries the
        // same key by design (it IS that hold's history), and a full index makes every
        // intent-keyed hold unconsumable, unreleasable and unexpirable — CloseOnce always dies on
        // its own hold's key. Replay safety is unchanged: an open hold still collides, and a
        // resumed leg whose chain already closed replays from its stored documents, never by
        // re-holding (MixedBasketCompletionService.ExecuteAsync).
        builder.HasIndex(reservation => new { reservation.IntentId, reservation.LegId, reservation.LocationId, reservation.ItemId })
            .IsUnique()
            .HasDatabaseName("ux_stock_reservations_intent_leg_item")
            .HasFilter("intent_id IS NOT NULL AND item_id IS NOT NULL AND state = 'Held'");

        builder.HasIndex(reservation => new { reservation.IntentId, reservation.LegId, reservation.LocationId, reservation.ItemVariantId })
            .IsUnique()
            .HasDatabaseName("ux_stock_reservations_intent_leg_variant")
            .HasFilter("intent_id IS NOT NULL AND item_variant_id IS NOT NULL AND state = 'Held'");

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_stock_reservations_exactly_one_sku",
                "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int) = 1");

            // A chain is a hold plus at most one terminal row — the domain never writes a third.
            table.HasCheckConstraint(
                "ck_stock_reservations_sequence_range",
                "sequence_number IN (0, 1)");
        });
    }
}

/// <summary><c>inventory.available_balances</c> — the reservation-half projection (Stage 08c).</summary>
internal sealed class AvailableBalanceConfiguration : EntityConfiguration<AvailableBalance>
{
    protected override string Schema => Schemas.Inventory;

    protected override string TableName => "available_balances";

    protected override void ConfigureEntity(EntityTypeBuilder<AvailableBalance> builder)
    {
        builder.Property(balance => balance.LocationId).IsRequired();
        builder.Property(balance => balance.ItemId);
        builder.Property(balance => balance.ItemVariantId);

        builder.HasQuantity(balance => balance.Reserved, "reserved");
        builder.HasQuantity(balance => balance.InStaging, "in_staging");
        builder.HasQuantity(balance => balance.Incoming, "incoming");

        // One position per location per stock-keeping unit — the same split-unique-index pattern
        // as stock_balances, for the same reason: PostgreSQL treats NULLs as distinct.
        builder.HasIndex(balance => new { balance.LocationId, balance.ItemId })
            .IsUnique()
            .HasDatabaseName("ux_available_balances_location_id_item_id")
            .HasFilter("item_id IS NOT NULL AND deleted_at IS NULL");

        builder.HasIndex(balance => new { balance.LocationId, balance.ItemVariantId })
            .IsUnique()
            .HasDatabaseName("ux_available_balances_location_id_item_variant_id")
            .HasFilter("item_variant_id IS NOT NULL AND deleted_at IS NULL");

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_available_balances_exactly_one_sku",
            "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int) = 1"));
    }
}
