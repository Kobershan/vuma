using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Stage17_ProductionExecutionRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "bill_of_materials_id",
                schema: "manufacturing",
                table: "production_orders",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "material_issues",
                schema: "manufacturing",
                table: "production_orders",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "output_receipts",
                schema: "manufacturing",
                table: "production_orders",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "scrap_records",
                schema: "manufacturing",
                table: "production_orders",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "last_webhook_event_id",
                schema: "loyalty",
                table: "members",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "last_webhook_version",
                schema: "loyalty",
                table: "members",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "bill_of_materials_id",
                schema: "manufacturing",
                table: "production_orders");

            migrationBuilder.DropColumn(
                name: "material_issues",
                schema: "manufacturing",
                table: "production_orders");

            migrationBuilder.DropColumn(
                name: "output_receipts",
                schema: "manufacturing",
                table: "production_orders");

            migrationBuilder.DropColumn(
                name: "scrap_records",
                schema: "manufacturing",
                table: "production_orders");

            migrationBuilder.DropColumn(
                name: "last_webhook_event_id",
                schema: "loyalty",
                table: "members");

            migrationBuilder.DropColumn(
                name: "last_webhook_version",
                schema: "loyalty",
                table: "members");
        }
    }
}
