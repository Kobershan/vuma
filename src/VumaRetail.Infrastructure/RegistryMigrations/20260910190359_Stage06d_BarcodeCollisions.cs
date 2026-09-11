using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.RegistryMigrations
{
    /// <inheritdoc />
    public partial class Stage06d_BarcodeCollisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_catalog_routing_index_tenant_id_barcode",
                schema: "registry",
                table: "catalog_routing_index");

            migrationBuilder.DropIndex(
                name: "ix_catalog_routing_index_tenant_id_company_id_barcode",
                schema: "registry",
                table: "catalog_routing_index");

            migrationBuilder.CreateIndex(
                name: "ix_catalog_routing_index_tenant_id_barcode",
                schema: "registry",
                table: "catalog_routing_index",
                columns: new[] { "tenant_id", "barcode" });

            migrationBuilder.CreateIndex(
                name: "ix_catalog_routing_index_tenant_id_company_id_barcode",
                schema: "registry",
                table: "catalog_routing_index",
                columns: new[] { "tenant_id", "company_id", "barcode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_catalog_routing_index_tenant_id_barcode",
                schema: "registry",
                table: "catalog_routing_index");

            migrationBuilder.DropIndex(
                name: "ix_catalog_routing_index_tenant_id_company_id_barcode",
                schema: "registry",
                table: "catalog_routing_index");

            migrationBuilder.CreateIndex(
                name: "ix_catalog_routing_index_tenant_id_barcode",
                schema: "registry",
                table: "catalog_routing_index",
                columns: new[] { "tenant_id", "barcode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_catalog_routing_index_tenant_id_company_id_barcode",
                schema: "registry",
                table: "catalog_routing_index",
                columns: new[] { "tenant_id", "company_id", "barcode" });
        }
    }
}
