using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.RegistryMigrations
{
    /// <inheritdoc />
    public partial class Stage22TransferDeliveryNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stock_transfer_delivery_notes",
                schema: "registry",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transfer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sender_company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receiver_company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    driver_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_transfer_delivery_notes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stock_transfer_delivery_note_lines",
                schema: "registry",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delivery_note_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    unit_of_measure = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    sender_location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receiver_location_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_transfer_delivery_note_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_stock_transfer_delivery_note_lines_stock_transfer_delivery_",
                        column: x => x.delivery_note_id,
                        principalSchema: "registry",
                        principalTable: "stock_transfer_delivery_notes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_delivery_note_lines_delivery_note_id",
                schema: "registry",
                table: "stock_transfer_delivery_note_lines",
                column: "delivery_note_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_delivery_note_lines_tenant_id_delivery_note_",
                schema: "registry",
                table: "stock_transfer_delivery_note_lines",
                columns: new[] { "tenant_id", "delivery_note_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_delivery_notes_tenant_id_number",
                schema: "registry",
                table: "stock_transfer_delivery_notes",
                columns: new[] { "tenant_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_delivery_notes_tenant_id_transfer_id",
                schema: "registry",
                table: "stock_transfer_delivery_notes",
                columns: new[] { "tenant_id", "transfer_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stock_transfer_delivery_note_lines",
                schema: "registry");

            migrationBuilder.DropTable(
                name: "stock_transfer_delivery_notes",
                schema: "registry");
        }
    }
}
