using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.RegistryMigrations
{
    /// <inheritdoc />
    public partial class Stage22b_ContactBindings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contact_bindings",
                schema: "registry",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    verification_state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    consent_state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: true),
                    company_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: false),
                    row_version = table.Column<byte[]>(type: "bytea", nullable: false),
                    sync_state = table.Column<int>(type: "integer", nullable: false),
                    sync_stamp = table.Column<string>(type: "text", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contact_bindings", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_contact_bindings_tenant_id_channel_address",
                schema: "registry",
                table: "contact_bindings",
                columns: new[] { "tenant_id", "channel", "address" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_contact_bindings_tenant_id_contact_id",
                schema: "registry",
                table: "contact_bindings",
                columns: new[] { "tenant_id", "contact_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contact_bindings",
                schema: "registry");
        }
    }
}
