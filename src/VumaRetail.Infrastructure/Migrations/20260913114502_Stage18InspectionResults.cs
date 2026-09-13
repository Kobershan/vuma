using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Stage18InspectionResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inspection_results",
                schema: "quality",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    hold_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    passed = table.Column<bool>(type: "boolean", nullable: false),
                    sample_size = table.Column<int>(type: "integer", nullable: false),
                    evidence = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    inspected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("pk_inspection_results", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_inspection_results_sync_state",
                schema: "quality",
                table: "inspection_results",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_inspection_results_tenant_company_hold",
                schema: "quality",
                table: "inspection_results",
                columns: new[] { "tenant_id", "company_id", "hold_id" },
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_inspection_results_tenant_id",
                schema: "quality",
                table: "inspection_results",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_inspection_results_tenant_id_company_id",
                schema: "quality",
                table: "inspection_results",
                columns: new[] { "tenant_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_inspection_results_tenant_id_store_id",
                schema: "quality",
                table: "inspection_results",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ux_inspection_results_tenant_operation",
                schema: "quality",
                table: "inspection_results",
                columns: new[] { "tenant_id", "operation_id" },
                unique: true,
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inspection_results",
                schema: "quality");
        }
    }
}
