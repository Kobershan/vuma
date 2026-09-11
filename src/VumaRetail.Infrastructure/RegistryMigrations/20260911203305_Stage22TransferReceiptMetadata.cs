using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.RegistryMigrations
{
    /// <inheritdoc />
    public partial class Stage22TransferReceiptMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "receiver_location_id",
                schema: "registry",
                table: "stock_transfer_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "unit_cost_at_transfer_amount",
                schema: "registry",
                table: "stock_transfer_lines",
                type: "numeric(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "unit_cost_at_transfer_currency",
                schema: "registry",
                table: "stock_transfer_lines",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "receiver_location_id",
                schema: "registry",
                table: "stock_transfer_lines");

            migrationBuilder.DropColumn(
                name: "unit_cost_at_transfer_amount",
                schema: "registry",
                table: "stock_transfer_lines");

            migrationBuilder.DropColumn(
                name: "unit_cost_at_transfer_currency",
                schema: "registry",
                table: "stock_transfer_lines");
        }
    }
}
