using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.RegistryMigrations
{
    /// <inheritdoc />
    public partial class Stage22RelatedTransferIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ux_stock_transfer_requests_related_relation",
                schema: "registry",
                table: "stock_transfer_requests",
                columns: new[] { "tenant_id", "related_transfer_id", "relation" },
                unique: true,
                filter: "related_transfer_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_stock_transfer_requests_related_relation",
                schema: "registry",
                table: "stock_transfer_requests");
        }
    }
}
