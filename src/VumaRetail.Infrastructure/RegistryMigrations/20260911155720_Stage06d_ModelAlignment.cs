using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.RegistryMigrations
{
    /// <inheritdoc />
    public partial class Stage06d_ModelAlignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_catalog_routing_index_tenant_id_company_id_barcode",
                schema: "registry",
                table: "catalog_routing_index");

            migrationBuilder.CreateIndex(
                name: "ix_catalog_routing_index_tenant_id_company_id_barcode",
                schema: "registry",
                table: "catalog_routing_index",
                columns: new[] { "tenant_id", "company_id", "barcode" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_catalog_routing_index_tenant_id_company_id_barcode",
                schema: "registry",
                table: "catalog_routing_index");

            migrationBuilder.CreateIndex(
                name: "ix_catalog_routing_index_tenant_id_company_id_barcode",
                schema: "registry",
                table: "catalog_routing_index",
                columns: new[] { "tenant_id", "company_id", "barcode" },
                unique: true);
        }
    }
}
