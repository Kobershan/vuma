using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Stage29ScheduledReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "scheduled_reports",
                schema: "reporting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    report_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    interval_minutes = table.Column<int>(type: "integer", nullable: false),
                    next_run_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("pk_scheduled_reports", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_reports_sync_state",
                schema: "reporting",
                table: "scheduled_reports",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_reports_tenant_id",
                schema: "reporting",
                table: "scheduled_reports",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_reports_tenant_id_company_id",
                schema: "reporting",
                table: "scheduled_reports",
                columns: new[] { "tenant_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_reports_tenant_id_company_id_next_run_at_utc",
                schema: "reporting",
                table: "scheduled_reports",
                columns: new[] { "tenant_id", "company_id", "next_run_at_utc" },
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_reports_tenant_id_store_id",
                schema: "reporting",
                table: "scheduled_reports",
                columns: new[] { "tenant_id", "store_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "scheduled_reports",
                schema: "reporting");
        }
    }
}
