using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Stage18InspectionPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "plan_id",
                schema: "quality",
                table: "inspection_results",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "plan_version",
                schema: "quality",
                table: "inspection_results",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "inspection_plans",
                schema: "quality",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    sample_size = table.Column<int>(type: "integer", nullable: false),
                    acceptance_criteria = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
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
                    table.PrimaryKey("pk_inspection_plans", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_inspection_plans_sync_state",
                schema: "quality",
                table: "inspection_plans",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_inspection_plans_tenant_company_status",
                schema: "quality",
                table: "inspection_plans",
                columns: new[] { "tenant_id", "company_id", "status" },
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_inspection_plans_tenant_id",
                schema: "quality",
                table: "inspection_plans",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_inspection_plans_tenant_id_company_id",
                schema: "quality",
                table: "inspection_plans",
                columns: new[] { "tenant_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_inspection_plans_tenant_id_store_id",
                schema: "quality",
                table: "inspection_plans",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ux_inspection_plans_tenant_company_target_version",
                schema: "quality",
                table: "inspection_plans",
                columns: new[] { "tenant_id", "company_id", "item_id", "item_variant_id", "version" },
                unique: true,
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inspection_plans",
                schema: "quality");

            migrationBuilder.DropColumn(
                name: "plan_id",
                schema: "quality",
                table: "inspection_results");

            migrationBuilder.DropColumn(
                name: "plan_version",
                schema: "quality",
                table: "inspection_results");
        }
    }
}
