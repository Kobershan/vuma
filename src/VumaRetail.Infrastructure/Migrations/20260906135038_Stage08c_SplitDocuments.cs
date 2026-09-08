using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Stage08c_SplitDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_stock_reservations_intent_leg_sequence",
                schema: "inventory",
                table: "stock_reservations");

            migrationBuilder.AddColumn<string>(
                name: "group_document_ref",
                schema: "orders",
                table: "sales_orders",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_stock_reservations_intent_leg_item",
                schema: "inventory",
                table: "stock_reservations",
                columns: new[] { "intent_id", "leg_id", "location_id", "item_id" },
                unique: true,
                filter: "intent_id IS NOT NULL AND item_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_stock_reservations_intent_leg_variant",
                schema: "inventory",
                table: "stock_reservations",
                columns: new[] { "intent_id", "leg_id", "location_id", "item_variant_id" },
                unique: true,
                filter: "intent_id IS NOT NULL AND item_variant_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_sales_orders_group_document_ref",
                schema: "orders",
                table: "sales_orders",
                columns: new[] { "tenant_id", "group_document_ref" },
                filter: "group_document_ref IS NOT NULL AND deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_stock_reservations_intent_leg_item",
                schema: "inventory",
                table: "stock_reservations");

            migrationBuilder.DropIndex(
                name: "ux_stock_reservations_intent_leg_variant",
                schema: "inventory",
                table: "stock_reservations");

            migrationBuilder.DropIndex(
                name: "ix_sales_orders_group_document_ref",
                schema: "orders",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "group_document_ref",
                schema: "orders",
                table: "sales_orders");

            migrationBuilder.CreateIndex(
                name: "ux_stock_reservations_intent_leg_sequence",
                schema: "inventory",
                table: "stock_reservations",
                columns: new[] { "intent_id", "leg_id", "sequence_number" },
                unique: true,
                filter: "intent_id IS NOT NULL");
        }
    }
}
