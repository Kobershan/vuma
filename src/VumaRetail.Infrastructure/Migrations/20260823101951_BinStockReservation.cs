using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BinStockReservation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "quantity_reserved_uom",
                schema: "warehouse",
                table: "bin_stock",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "quantity_reserved_value",
                schema: "warehouse",
                table: "bin_stock",
                type: "numeric(18,6)",
                nullable: false,
                defaultValue: 0m);

            // Every existing row's reservation is genuinely zero — nothing built before this migration
            // could reserve anything — but its unit of measure must still agree with quantity_on_hand's,
            // or EnsureSameUnitOfMeasure refuses the first Reserve() call against a bin nobody has
            // touched since. quantity_reserved_value stays 0; only the unit label is backfilled.
            migrationBuilder.Sql(
                """
                UPDATE warehouse.bin_stock
                SET quantity_reserved_uom = quantity_on_hand_uom
                WHERE quantity_reserved_uom = '';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "quantity_reserved_uom",
                schema: "warehouse",
                table: "bin_stock");

            migrationBuilder.DropColumn(
                name: "quantity_reserved_value",
                schema: "warehouse",
                table: "bin_stock");
        }
    }
}
