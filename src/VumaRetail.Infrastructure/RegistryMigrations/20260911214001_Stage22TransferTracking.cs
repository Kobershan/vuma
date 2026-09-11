using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.RegistryMigrations
{
    /// <inheritdoc />
    public partial class Stage22TransferTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_stock_transfer_lines_tenant_id_transfer_id_item_id_item_var",
                schema: "registry",
                table: "stock_transfer_lines");

            migrationBuilder.AddColumn<string>(
                name: "batch_reference",
                schema: "registry",
                table: "stock_transfer_lines",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "expiry_date",
                schema: "registry",
                table: "stock_transfer_lines",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "serial_number",
                schema: "registry",
                table: "stock_transfer_lines",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "batch_reference",
                schema: "registry",
                table: "stock_transfer_delivery_note_lines",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "expiry_date",
                schema: "registry",
                table: "stock_transfer_delivery_note_lines",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "serial_number",
                schema: "registry",
                table: "stock_transfer_delivery_note_lines",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_lines_tenant_id_transfer_id_item_id_item_var",
                schema: "registry",
                table: "stock_transfer_lines",
                columns: new[] { "tenant_id", "transfer_id", "item_id", "item_variant_id" });

            migrationBuilder.CreateIndex(
                name: "ux_stock_transfer_lines_transfer_serial",
                schema: "registry",
                table: "stock_transfer_lines",
                columns: new[] { "tenant_id", "transfer_id", "serial_number" },
                unique: true,
                filter: "serial_number IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_stock_transfer_lines_tenant_id_transfer_id_item_id_item_var",
                schema: "registry",
                table: "stock_transfer_lines");

            migrationBuilder.DropIndex(
                name: "ux_stock_transfer_lines_transfer_serial",
                schema: "registry",
                table: "stock_transfer_lines");

            migrationBuilder.DropColumn(
                name: "batch_reference",
                schema: "registry",
                table: "stock_transfer_lines");

            migrationBuilder.DropColumn(
                name: "expiry_date",
                schema: "registry",
                table: "stock_transfer_lines");

            migrationBuilder.DropColumn(
                name: "serial_number",
                schema: "registry",
                table: "stock_transfer_lines");

            migrationBuilder.DropColumn(
                name: "batch_reference",
                schema: "registry",
                table: "stock_transfer_delivery_note_lines");

            migrationBuilder.DropColumn(
                name: "expiry_date",
                schema: "registry",
                table: "stock_transfer_delivery_note_lines");

            migrationBuilder.DropColumn(
                name: "serial_number",
                schema: "registry",
                table: "stock_transfer_delivery_note_lines");

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_lines_tenant_id_transfer_id_item_id_item_var",
                schema: "registry",
                table: "stock_transfer_lines",
                columns: new[] { "tenant_id", "transfer_id", "item_id", "item_variant_id" },
                unique: true);
        }
    }
}
