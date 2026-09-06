using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.RegistryMigrations
{
    /// <inheritdoc />
    public partial class Stage08c_GroupAvailability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "group_availability_cursors",
                schema: "registry",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_outbox_row_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_group_availability_cursors", x => new { x.tenant_id, x.company_id });
                });

            migrationBuilder.CreateTable(
                name: "group_availability_rows",
                schema: "registry",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    on_hand = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    reserved = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    in_staging = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    available = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    unit_of_measure = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    as_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_group_availability_rows", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_group_availability_rows_tenant_id_item_id_item_variant_id",
                schema: "registry",
                table: "group_availability_rows",
                columns: new[] { "tenant_id", "item_id", "item_variant_id" });

            migrationBuilder.CreateIndex(
                name: "ux_group_availability_company_location_item",
                schema: "registry",
                table: "group_availability_rows",
                columns: new[] { "tenant_id", "company_id", "location_id", "item_id" },
                unique: true,
                filter: "item_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_group_availability_company_location_variant",
                schema: "registry",
                table: "group_availability_rows",
                columns: new[] { "tenant_id", "company_id", "location_id", "item_variant_id" },
                unique: true,
                filter: "item_variant_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "group_availability_cursors",
                schema: "registry");

            migrationBuilder.DropTable(
                name: "group_availability_rows",
                schema: "registry");
        }
    }
}
