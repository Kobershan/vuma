using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Stage21PaymentAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payment_attempts",
                schema: "ecommerce",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    checkout_intent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    payload_fingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    provider_payment_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    provider_reference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    received_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: true),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    row_version = table.Column<byte[]>(type: "bytea", nullable: false),
                    sync_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    sync_stamp = table.Column<string>(type: "character varying(86)", maxLength: 86, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_attempts", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payment_attempts_sync_state",
                schema: "ecommerce",
                table: "payment_attempts",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_payment_attempts_tenant_id",
                schema: "ecommerce",
                table: "payment_attempts",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_attempts_tenant_id_checkout_intent_id",
                schema: "ecommerce",
                table: "payment_attempts",
                columns: new[] { "tenant_id", "checkout_intent_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_attempts_tenant_id_company_id",
                schema: "ecommerce",
                table: "payment_attempts",
                columns: new[] { "tenant_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_attempts_tenant_id_event_id",
                schema: "ecommerce",
                table: "payment_attempts",
                columns: new[] { "tenant_id", "event_id" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_payment_attempts_tenant_id_store_id",
                schema: "ecommerce",
                table: "payment_attempts",
                columns: new[] { "tenant_id", "store_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payment_attempts",
                schema: "ecommerce");
        }
    }
}
