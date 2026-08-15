using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Inventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "inventory");

            migrationBuilder.CreateTable(
                name: "stock_balances",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    average_cost_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    average_cost_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
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
                    table.PrimaryKey("pk_stock_balances", x => x.id);
                    table.CheckConstraint("ck_stock_balances_exactly_one_sku", "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int) = 1");
                });

            migrationBuilder.CreateTable(
                name: "stock_ledger_entries",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    movement_type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    reference_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason_code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    quantity_uom = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    quantity_value = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    unit_cost_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    unit_cost_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
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
                    table.PrimaryKey("pk_stock_ledger_entries", x => x.id);
                    table.CheckConstraint("ck_stock_ledger_entries_exactly_one_sku", "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int) = 1");
                    table.CheckConstraint("ck_stock_ledger_entries_quantity_non_zero", "quantity_value <> 0");
                });

            migrationBuilder.CreateTable(
                name: "stock_locations",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("pk_stock_locations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stock_transfers",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    destination_location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    out_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    in_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    quantity_uom = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    quantity_value = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    unit_cost_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    unit_cost_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
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
                    table.PrimaryKey("pk_stock_transfers", x => x.id);
                    table.CheckConstraint("ck_stock_transfers_exactly_one_sku", "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int) = 1");
                    table.CheckConstraint("ck_stock_transfers_locations_differ", "source_location_id <> destination_location_id");
                });

            migrationBuilder.CreateTable(
                name: "stocktake_lines",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    stocktake_session_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("pk_stocktake_lines", x => x.id);
                    table.CheckConstraint("ck_stocktake_lines_exactly_one_sku", "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int) = 1");
                });

            migrationBuilder.CreateTable(
                name: "stocktake_sessions",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
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
                    table.PrimaryKey("pk_stocktake_sessions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_stock_balances_sync_state",
                schema: "inventory",
                table: "stock_balances",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_stock_balances_tenant_id",
                schema: "inventory",
                table: "stock_balances",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_balances_tenant_id_store_id",
                schema: "inventory",
                table: "stock_balances",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ux_stock_balances_location_id_item_id",
                schema: "inventory",
                table: "stock_balances",
                columns: new[] { "location_id", "item_id" },
                unique: true,
                filter: "item_id IS NOT NULL AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_stock_balances_location_id_item_variant_id",
                schema: "inventory",
                table: "stock_balances",
                columns: new[] { "location_id", "item_variant_id" },
                unique: true,
                filter: "item_variant_id IS NOT NULL AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_stock_ledger_entries_item_id",
                schema: "inventory",
                table: "stock_ledger_entries",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_ledger_entries_item_variant_id",
                schema: "inventory",
                table: "stock_ledger_entries",
                column: "item_variant_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_ledger_entries_location_id_created_at_id",
                schema: "inventory",
                table: "stock_ledger_entries",
                columns: new[] { "location_id", "created_at", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_stock_ledger_entries_reference_id",
                schema: "inventory",
                table: "stock_ledger_entries",
                column: "reference_id",
                filter: "reference_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_stock_ledger_entries_sync_state",
                schema: "inventory",
                table: "stock_ledger_entries",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_stock_ledger_entries_tenant_id",
                schema: "inventory",
                table: "stock_ledger_entries",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_ledger_entries_tenant_id_store_id",
                schema: "inventory",
                table: "stock_ledger_entries",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_locations_sync_state",
                schema: "inventory",
                table: "stock_locations",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_stock_locations_tenant_id",
                schema: "inventory",
                table: "stock_locations",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_locations_tenant_id_store_id",
                schema: "inventory",
                table: "stock_locations",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ux_stock_locations_tenant_id_code",
                schema: "inventory",
                table: "stock_locations",
                columns: new[] { "tenant_id", "code" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfers_destination_location_id",
                schema: "inventory",
                table: "stock_transfers",
                column: "destination_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfers_source_location_id",
                schema: "inventory",
                table: "stock_transfers",
                column: "source_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfers_sync_state",
                schema: "inventory",
                table: "stock_transfers",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfers_tenant_id",
                schema: "inventory",
                table: "stock_transfers",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfers_tenant_id_store_id",
                schema: "inventory",
                table: "stock_transfers",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stocktake_lines_stocktake_session_id",
                schema: "inventory",
                table: "stocktake_lines",
                column: "stocktake_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_stocktake_lines_sync_state",
                schema: "inventory",
                table: "stocktake_lines",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_stocktake_lines_tenant_id",
                schema: "inventory",
                table: "stocktake_lines",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_stocktake_lines_tenant_id_store_id",
                schema: "inventory",
                table: "stocktake_lines",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ux_stocktake_lines_session_id_item_id",
                schema: "inventory",
                table: "stocktake_lines",
                columns: new[] { "stocktake_session_id", "item_id" },
                unique: true,
                filter: "item_id IS NOT NULL AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_stocktake_lines_session_id_item_variant_id",
                schema: "inventory",
                table: "stocktake_lines",
                columns: new[] { "stocktake_session_id", "item_variant_id" },
                unique: true,
                filter: "item_variant_id IS NOT NULL AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_stocktake_sessions_location_id",
                schema: "inventory",
                table: "stocktake_sessions",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_stocktake_sessions_open_by_location",
                schema: "inventory",
                table: "stocktake_sessions",
                columns: new[] { "tenant_id", "location_id" },
                filter: "status = 'Open' AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_stocktake_sessions_sync_state",
                schema: "inventory",
                table: "stocktake_sessions",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_stocktake_sessions_tenant_id",
                schema: "inventory",
                table: "stocktake_sessions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_stocktake_sessions_tenant_id_store_id",
                schema: "inventory",
                table: "stocktake_sessions",
                columns: new[] { "tenant_id", "store_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stock_balances",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_ledger_entries",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_locations",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_transfers",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stocktake_lines",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stocktake_sessions",
                schema: "inventory");
        }
    }
}
