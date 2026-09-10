using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Stage14_Revision4Rework : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "delivery_city",
                schema: "orders",
                table: "sales_orders",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "delivery_postal_code",
                schema: "orders",
                table: "sales_orders",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "delivery_province",
                schema: "orders",
                table: "sales_orders",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "delivery_suburb",
                schema: "orders",
                table: "sales_orders",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "driver_collect_authorised_at",
                schema: "orders",
                table: "sales_orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "driver_collect_authorised_by",
                schema: "orders",
                table: "sales_orders",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "settlement_terms",
                schema: "orders",
                table: "sales_orders",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "reservation_id",
                schema: "orders",
                table: "sales_order_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "settlement_terms",
                schema: "sales",
                table: "invoices",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "delivery_city",
                schema: "orders",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "delivery_postal_code",
                schema: "orders",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "delivery_province",
                schema: "orders",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "delivery_suburb",
                schema: "orders",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "driver_collect_authorised_at",
                schema: "orders",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "driver_collect_authorised_by",
                schema: "orders",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "settlement_terms",
                schema: "orders",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "reservation_id",
                schema: "orders",
                table: "sales_order_lines");

            migrationBuilder.DropColumn(
                name: "settlement_terms",
                schema: "sales",
                table: "invoices");
        }
    }
}
