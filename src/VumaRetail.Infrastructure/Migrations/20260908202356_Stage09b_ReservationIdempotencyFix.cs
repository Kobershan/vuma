using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Stage09b_ReservationIdempotencyFix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_stock_reservations_intent_leg_item",
                schema: "inventory",
                table: "stock_reservations");

            migrationBuilder.DropIndex(
                name: "ux_stock_reservations_intent_leg_variant",
                schema: "inventory",
                table: "stock_reservations");

            migrationBuilder.CreateIndex(
                name: "ux_stock_reservations_intent_leg_item",
                schema: "inventory",
                table: "stock_reservations",
                columns: new[] { "intent_id", "leg_id", "location_id", "item_id" },
                unique: true,
                filter: "intent_id IS NOT NULL AND item_id IS NOT NULL AND state = 'Held'");

            migrationBuilder.CreateIndex(
                name: "ux_stock_reservations_intent_leg_variant",
                schema: "inventory",
                table: "stock_reservations",
                columns: new[] { "intent_id", "leg_id", "location_id", "item_variant_id" },
                unique: true,
                filter: "intent_id IS NOT NULL AND item_variant_id IS NOT NULL AND state = 'Held'");
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
        }
    }
}
