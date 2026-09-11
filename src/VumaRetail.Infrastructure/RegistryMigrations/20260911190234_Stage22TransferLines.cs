using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.RegistryMigrations
{
    /// <inheritdoc />
    public partial class Stage22TransferLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stock_transfer_lines",
                schema: "registry",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transfer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    unit_of_measure = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    sender_location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    received_quantity = table.Column<decimal>(type: "numeric(18,6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_transfer_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_stock_transfer_lines_stock_transfer_requests_transfer_id",
                        column: x => x.transfer_id,
                        principalSchema: "registry",
                        principalTable: "stock_transfer_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_lines_tenant_id_transfer_id",
                schema: "registry",
                table: "stock_transfer_lines",
                columns: new[] { "tenant_id", "transfer_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_lines_tenant_id_transfer_id_item_id_item_var",
                schema: "registry",
                table: "stock_transfer_lines",
                columns: new[] { "tenant_id", "transfer_id", "item_id", "item_variant_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_lines_transfer_id",
                schema: "registry",
                table: "stock_transfer_lines",
                column: "transfer_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stock_transfer_lines",
                schema: "registry");
        }
    }
}
