using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Stage08c_AvailabilityReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "available_balances",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    in_staging_uom = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    in_staging_value = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    incoming_uom = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    incoming_value = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    reserved_uom = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    reserved_value = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: true),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("pk_available_balances", x => x.id);
                    table.CheckConstraint("ck_available_balances_exactly_one_sku", "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int) = 1");
                });

            migrationBuilder.CreateTable(
                name: "stock_reservations",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    source_document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_document_ref = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    reservation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence_number = table.Column<int>(type: "integer", nullable: false),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    intent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    leg_id = table.Column<Guid>(type: "uuid", nullable: true),
                    consumed_by_reference_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    quantity_uom = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    quantity_value = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: true),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("pk_stock_reservations", x => x.id);
                    table.CheckConstraint("ck_stock_reservations_exactly_one_sku", "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int) = 1");
                    table.CheckConstraint("ck_stock_reservations_sequence_range", "sequence_number IN (0, 1)");
                });

            migrationBuilder.CreateIndex(
                name: "ix_available_balances_sync_state",
                schema: "inventory",
                table: "available_balances",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_available_balances_tenant_id",
                schema: "inventory",
                table: "available_balances",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_available_balances_tenant_id_company_id",
                schema: "inventory",
                table: "available_balances",
                columns: new[] { "tenant_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_available_balances_tenant_id_store_id",
                schema: "inventory",
                table: "available_balances",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ux_available_balances_location_id_item_id",
                schema: "inventory",
                table: "available_balances",
                columns: new[] { "location_id", "item_id" },
                unique: true,
                filter: "item_id IS NOT NULL AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_available_balances_location_id_item_variant_id",
                schema: "inventory",
                table: "available_balances",
                columns: new[] { "location_id", "item_variant_id" },
                unique: true,
                filter: "item_variant_id IS NOT NULL AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_stock_reservations_expiry",
                schema: "inventory",
                table: "stock_reservations",
                columns: new[] { "tenant_id", "state", "expires_at" },
                filter: "state = 'Held' AND expires_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_stock_reservations_location_id_item_id_state",
                schema: "inventory",
                table: "stock_reservations",
                columns: new[] { "location_id", "item_id", "state" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_reservations_location_id_item_variant_id_state",
                schema: "inventory",
                table: "stock_reservations",
                columns: new[] { "location_id", "item_variant_id", "state" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_reservations_reservation_id_sequence_number",
                schema: "inventory",
                table: "stock_reservations",
                columns: new[] { "reservation_id", "sequence_number" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_reservations_sync_state",
                schema: "inventory",
                table: "stock_reservations",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_stock_reservations_tenant_id",
                schema: "inventory",
                table: "stock_reservations",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_reservations_tenant_id_company_id",
                schema: "inventory",
                table: "stock_reservations",
                columns: new[] { "tenant_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_reservations_tenant_id_store_id",
                schema: "inventory",
                table: "stock_reservations",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ux_stock_reservations_intent_leg_sequence",
                schema: "inventory",
                table: "stock_reservations",
                columns: new[] { "intent_id", "leg_id", "sequence_number" },
                unique: true,
                filter: "intent_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_stock_reservations_open",
                schema: "inventory",
                table: "stock_reservations",
                columns: new[] { "reservation_id", "state" },
                unique: true,
                filter: "state = 'Held'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "available_balances",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_reservations",
                schema: "inventory");
        }
    }
}
