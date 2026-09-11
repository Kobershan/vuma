using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Stage22TrackedInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "batch_reference",
                schema: "inventory",
                table: "stock_reservations",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "expiry_date",
                schema: "inventory",
                table: "stock_reservations",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "serial_number",
                schema: "inventory",
                table: "stock_reservations",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "batch_reference",
                schema: "inventory",
                table: "stock_ledger_entries",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "expiry_date",
                schema: "inventory",
                table: "stock_ledger_entries",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "serial_number",
                schema: "inventory",
                table: "stock_ledger_entries",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_reservations_tracking_state",
                schema: "inventory",
                table: "stock_reservations",
                columns: new[] { "location_id", "item_id", "item_variant_id", "batch_reference", "expiry_date", "serial_number", "state" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_ledger_entries_tracking_item",
                schema: "inventory",
                table: "stock_ledger_entries",
                columns: new[] { "location_id", "item_id", "batch_reference", "expiry_date", "serial_number" },
                filter: "batch_reference IS NOT NULL OR expiry_date IS NOT NULL OR serial_number IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_stock_reservations_tracking_state",
                schema: "inventory",
                table: "stock_reservations");

            migrationBuilder.DropIndex(
                name: "ix_stock_ledger_entries_tracking_item",
                schema: "inventory",
                table: "stock_ledger_entries");

            migrationBuilder.DropColumn(
                name: "batch_reference",
                schema: "inventory",
                table: "stock_reservations");

            migrationBuilder.DropColumn(
                name: "expiry_date",
                schema: "inventory",
                table: "stock_reservations");

            migrationBuilder.DropColumn(
                name: "serial_number",
                schema: "inventory",
                table: "stock_reservations");

            migrationBuilder.DropColumn(
                name: "batch_reference",
                schema: "inventory",
                table: "stock_ledger_entries");

            migrationBuilder.DropColumn(
                name: "expiry_date",
                schema: "inventory",
                table: "stock_ledger_entries");

            migrationBuilder.DropColumn(
                name: "serial_number",
                schema: "inventory",
                table: "stock_ledger_entries");
        }
    }
}
