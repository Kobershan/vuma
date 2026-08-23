using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BinStockReservationCheckConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "ck_bin_stock_reserved_not_exceeding_on_hand",
                schema: "warehouse",
                table: "bin_stock",
                sql: "quantity_reserved_value <= quantity_on_hand_value");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_bin_stock_reserved_not_exceeding_on_hand",
                schema: "warehouse",
                table: "bin_stock");
        }
    }
}
