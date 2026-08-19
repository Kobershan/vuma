using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Warehouse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "warehouse");

            migrationBuilder.AddColumn<Guid>(
                name: "bin_id",
                schema: "inventory",
                table: "stock_ledger_entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "bin_stock",
                schema: "warehouse",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    bin_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    quantity_on_hand_uom = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    quantity_on_hand_value = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    row_version = table.Column<byte[]>(type: "bytea", nullable: false),
                    sync_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    sync_stamp = table.Column<string>(type: "character varying(86)", maxLength: 86, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bin_stock", x => x.id);
                    table.CheckConstraint("ck_bin_stock_exactly_one_sku", "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int) = 1");
                });

            migrationBuilder.CreateTable(
                name: "bin_stock_movements",
                schema: "warehouse",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    bin_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    movement_type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    reference_type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    quantity_uom = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    quantity_value = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    row_version = table.Column<byte[]>(type: "bytea", nullable: false),
                    sync_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    sync_stamp = table.Column<string>(type: "character varying(86)", maxLength: 86, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bin_stock_movements", x => x.id);
                    table.CheckConstraint("ck_bin_stock_movements_exactly_one_sku", "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int) = 1");
                    table.CheckConstraint("ck_bin_stock_movements_quantity_non_zero", "quantity_value <> 0");
                });

            migrationBuilder.CreateTable(
                name: "bins",
                schema: "warehouse",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    zone_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    capacity_value = table.Column<decimal>(type: "numeric(18,6)", nullable: true),
                    capacity_unit_of_measure = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    row_version = table.Column<byte[]>(type: "bytea", nullable: false),
                    sync_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    sync_stamp = table.Column<string>(type: "character varying(86)", maxLength: 86, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bins", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cycle_count_lines",
                schema: "warehouse",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cycle_count_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bin_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    counted_quantity_uom = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    counted_quantity_value = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    system_quantity_uom = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    system_quantity_value = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    row_version = table.Column<byte[]>(type: "bytea", nullable: false),
                    sync_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    sync_stamp = table.Column<string>(type: "character varying(86)", maxLength: 86, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cycle_count_lines", x => x.id);
                    table.CheckConstraint("ck_cycle_count_lines_exactly_one_sku", "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int) = 1");
                });

            migrationBuilder.CreateTable(
                name: "cycle_counts",
                schema: "warehouse",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    zone_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    scheduled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finalized_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    row_version = table.Column<byte[]>(type: "bytea", nullable: false),
                    sync_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    sync_stamp = table.Column<string>(type: "character varying(86)", maxLength: 86, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cycle_counts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pack_tasks",
                schema: "warehouse",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pick_wave_id = table.Column<Guid>(type: "uuid", nullable: false),
                    package_count = table.Column<int>(type: "integer", nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    packed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    row_version = table.Column<byte[]>(type: "bytea", nullable: false),
                    sync_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    sync_stamp = table.Column<string>(type: "character varying(86)", maxLength: 86, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pack_tasks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pick_tasks",
                schema: "warehouse",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pick_wave_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    outbound_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    allocated_bin_id = table.Column<Guid>(type: "uuid", nullable: true),
                    allocated_quantity_value = table.Column<decimal>(type: "numeric(18,6)", nullable: true),
                    picked_quantity_value = table.Column<decimal>(type: "numeric(18,6)", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    requested_quantity_uom = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    requested_quantity_value = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    row_version = table.Column<byte[]>(type: "bytea", nullable: false),
                    sync_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    sync_stamp = table.Column<string>(type: "character varying(86)", maxLength: 86, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pick_tasks", x => x.id);
                    table.CheckConstraint("ck_pick_tasks_exactly_one_sku", "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int) = 1");
                });

            migrationBuilder.CreateTable(
                name: "pick_waves",
                schema: "warehouse",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    picked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    packed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    shipped_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    row_version = table.Column<byte[]>(type: "bytea", nullable: false),
                    sync_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    sync_stamp = table.Column<string>(type: "character varying(86)", maxLength: 86, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pick_waves", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "putaway_tasks",
                schema: "warehouse",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_reference_type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    source_reference_id = table.Column<Guid>(type: "uuid", nullable: true),
                    suggested_bin_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    confirmed_bin_id = table.Column<Guid>(type: "uuid", nullable: true),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    confirmed_quantity_uom = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    confirmed_quantity_value = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    quantity_uom = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    quantity_value = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    row_version = table.Column<byte[]>(type: "bytea", nullable: false),
                    sync_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    sync_stamp = table.Column<string>(type: "character varying(86)", maxLength: 86, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_putaway_tasks", x => x.id);
                    table.CheckConstraint("ck_putaway_tasks_exactly_one_sku", "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int) = 1");
                });

            migrationBuilder.CreateTable(
                name: "shipment_confirmations",
                schema: "warehouse",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pick_wave_id = table.Column<Guid>(type: "uuid", nullable: false),
                    carrier = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    tracking_number = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    shipped_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    row_version = table.Column<byte[]>(type: "bytea", nullable: false),
                    sync_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    sync_stamp = table.Column<string>(type: "character varying(86)", maxLength: 86, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shipment_confirmations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "zones",
                schema: "warehouse",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    row_version = table.Column<byte[]>(type: "bytea", nullable: false),
                    sync_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    sync_stamp = table.Column<string>(type: "character varying(86)", maxLength: 86, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_zones", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_stock_ledger_entries_bin_id",
                schema: "inventory",
                table: "stock_ledger_entries",
                column: "bin_id",
                filter: "bin_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_bin_stock_item_id",
                schema: "warehouse",
                table: "bin_stock",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_bin_stock_item_variant_id",
                schema: "warehouse",
                table: "bin_stock",
                column: "item_variant_id");

            migrationBuilder.CreateIndex(
                name: "ix_bin_stock_sync_state",
                schema: "warehouse",
                table: "bin_stock",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_bin_stock_tenant_id",
                schema: "warehouse",
                table: "bin_stock",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_bin_stock_tenant_id_store_id",
                schema: "warehouse",
                table: "bin_stock",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ux_bin_stock_bin_id_item_id",
                schema: "warehouse",
                table: "bin_stock",
                columns: new[] { "bin_id", "item_id" },
                unique: true,
                filter: "item_id IS NOT NULL AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_bin_stock_bin_id_item_variant_id",
                schema: "warehouse",
                table: "bin_stock",
                columns: new[] { "bin_id", "item_variant_id" },
                unique: true,
                filter: "item_variant_id IS NOT NULL AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_bin_stock_movements_bin_id_created_at",
                schema: "warehouse",
                table: "bin_stock_movements",
                columns: new[] { "bin_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_bin_stock_movements_reference_id",
                schema: "warehouse",
                table: "bin_stock_movements",
                column: "reference_id");

            migrationBuilder.CreateIndex(
                name: "ix_bin_stock_movements_sync_state",
                schema: "warehouse",
                table: "bin_stock_movements",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_bin_stock_movements_tenant_id",
                schema: "warehouse",
                table: "bin_stock_movements",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_bin_stock_movements_tenant_id_store_id",
                schema: "warehouse",
                table: "bin_stock_movements",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ix_bins_sync_state",
                schema: "warehouse",
                table: "bins",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_bins_tenant_id",
                schema: "warehouse",
                table: "bins",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_bins_tenant_id_store_id",
                schema: "warehouse",
                table: "bins",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ix_bins_zone_id",
                schema: "warehouse",
                table: "bins",
                column: "zone_id");

            migrationBuilder.CreateIndex(
                name: "ux_bins_location_id_code",
                schema: "warehouse",
                table: "bins",
                columns: new[] { "location_id", "code" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_cycle_count_lines_cycle_count_id",
                schema: "warehouse",
                table: "cycle_count_lines",
                column: "cycle_count_id");

            migrationBuilder.CreateIndex(
                name: "ix_cycle_count_lines_sync_state",
                schema: "warehouse",
                table: "cycle_count_lines",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_cycle_count_lines_tenant_id",
                schema: "warehouse",
                table: "cycle_count_lines",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_cycle_count_lines_tenant_id_store_id",
                schema: "warehouse",
                table: "cycle_count_lines",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ux_cycle_count_lines_count_bin_item_id",
                schema: "warehouse",
                table: "cycle_count_lines",
                columns: new[] { "cycle_count_id", "bin_id", "item_id" },
                unique: true,
                filter: "item_id IS NOT NULL AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_cycle_count_lines_count_bin_item_variant_id",
                schema: "warehouse",
                table: "cycle_count_lines",
                columns: new[] { "cycle_count_id", "bin_id", "item_variant_id" },
                unique: true,
                filter: "item_variant_id IS NOT NULL AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_cycle_counts_open_by_location",
                schema: "warehouse",
                table: "cycle_counts",
                columns: new[] { "tenant_id", "location_id" },
                filter: "status = 'Open' AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_cycle_counts_sync_state",
                schema: "warehouse",
                table: "cycle_counts",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_cycle_counts_tenant_id",
                schema: "warehouse",
                table: "cycle_counts",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_cycle_counts_tenant_id_store_id",
                schema: "warehouse",
                table: "cycle_counts",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pack_tasks_sync_state",
                schema: "warehouse",
                table: "pack_tasks",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_pack_tasks_tenant_id",
                schema: "warehouse",
                table: "pack_tasks",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_pack_tasks_tenant_id_store_id",
                schema: "warehouse",
                table: "pack_tasks",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ux_pack_tasks_pick_wave_id",
                schema: "warehouse",
                table: "pack_tasks",
                column: "pick_wave_id",
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_pick_tasks_allocated_bin_id",
                schema: "warehouse",
                table: "pick_tasks",
                column: "allocated_bin_id");

            migrationBuilder.CreateIndex(
                name: "ix_pick_tasks_pick_wave_id",
                schema: "warehouse",
                table: "pick_tasks",
                column: "pick_wave_id");

            migrationBuilder.CreateIndex(
                name: "ix_pick_tasks_sync_state",
                schema: "warehouse",
                table: "pick_tasks",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_pick_tasks_tenant_id",
                schema: "warehouse",
                table: "pick_tasks",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_pick_tasks_tenant_id_store_id",
                schema: "warehouse",
                table: "pick_tasks",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pick_waves_location_id",
                schema: "warehouse",
                table: "pick_waves",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_pick_waves_sync_state",
                schema: "warehouse",
                table: "pick_waves",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_pick_waves_tenant_id",
                schema: "warehouse",
                table: "pick_waves",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_pick_waves_tenant_id_store_id",
                schema: "warehouse",
                table: "pick_waves",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ix_putaway_tasks_pending_by_location",
                schema: "warehouse",
                table: "putaway_tasks",
                columns: new[] { "tenant_id", "location_id" },
                filter: "status = 'Pending' AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_putaway_tasks_sync_state",
                schema: "warehouse",
                table: "putaway_tasks",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_putaway_tasks_tenant_id",
                schema: "warehouse",
                table: "putaway_tasks",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_putaway_tasks_tenant_id_store_id",
                schema: "warehouse",
                table: "putaway_tasks",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ix_shipment_confirmations_sync_state",
                schema: "warehouse",
                table: "shipment_confirmations",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_shipment_confirmations_tenant_id",
                schema: "warehouse",
                table: "shipment_confirmations",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_shipment_confirmations_tenant_id_store_id",
                schema: "warehouse",
                table: "shipment_confirmations",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ux_shipment_confirmations_pick_wave_id",
                schema: "warehouse",
                table: "shipment_confirmations",
                column: "pick_wave_id",
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_zones_sync_state",
                schema: "warehouse",
                table: "zones",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_zones_tenant_id",
                schema: "warehouse",
                table: "zones",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_zones_tenant_id_store_id",
                schema: "warehouse",
                table: "zones",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ux_zones_location_id_code",
                schema: "warehouse",
                table: "zones",
                columns: new[] { "location_id", "code" },
                unique: true,
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bin_stock",
                schema: "warehouse");

            migrationBuilder.DropTable(
                name: "bin_stock_movements",
                schema: "warehouse");

            migrationBuilder.DropTable(
                name: "bins",
                schema: "warehouse");

            migrationBuilder.DropTable(
                name: "cycle_count_lines",
                schema: "warehouse");

            migrationBuilder.DropTable(
                name: "cycle_counts",
                schema: "warehouse");

            migrationBuilder.DropTable(
                name: "pack_tasks",
                schema: "warehouse");

            migrationBuilder.DropTable(
                name: "pick_tasks",
                schema: "warehouse");

            migrationBuilder.DropTable(
                name: "pick_waves",
                schema: "warehouse");

            migrationBuilder.DropTable(
                name: "putaway_tasks",
                schema: "warehouse");

            migrationBuilder.DropTable(
                name: "shipment_confirmations",
                schema: "warehouse");

            migrationBuilder.DropTable(
                name: "zones",
                schema: "warehouse");

            migrationBuilder.DropIndex(
                name: "ix_stock_ledger_entries_bin_id",
                schema: "inventory",
                table: "stock_ledger_entries");

            migrationBuilder.DropColumn(
                name: "bin_id",
                schema: "inventory",
                table: "stock_ledger_entries");
        }
    }
}
