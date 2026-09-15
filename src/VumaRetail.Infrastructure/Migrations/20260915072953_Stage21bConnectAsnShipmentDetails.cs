using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Stage21bConnectAsnShipmentDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "batch_number",
                schema: "connect",
                table: "order_lines",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "expiry_date",
                schema: "connect",
                table: "order_lines",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "package_reference",
                schema: "connect",
                table: "order_lines",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "serial_numbers",
                schema: "connect",
                table: "order_lines",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "batch_number",
                schema: "connect",
                table: "order_lines");

            migrationBuilder.DropColumn(
                name: "expiry_date",
                schema: "connect",
                table: "order_lines");

            migrationBuilder.DropColumn(
                name: "package_reference",
                schema: "connect",
                table: "order_lines");

            migrationBuilder.DropColumn(
                name: "serial_numbers",
                schema: "connect",
                table: "order_lines");
        }
    }
}
