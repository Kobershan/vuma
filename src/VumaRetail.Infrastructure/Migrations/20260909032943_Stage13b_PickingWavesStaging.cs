using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Stage13b_PickingWavesStaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "company_scope_id",
                schema: "warehouse",
                table: "pick_waves",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "geography_level",
                schema: "warehouse",
                table: "pick_waves",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "geography_value",
                schema: "warehouse",
                table: "pick_waves",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "period_from",
                schema: "warehouse",
                table: "pick_waves",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "period_to",
                schema: "warehouse",
                table: "pick_waves",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "count_schedules",
                schema: "warehouse",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    cadence = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    scope = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    slow_mover_days = table.Column<int>(type: "integer", nullable: false),
                    random_sample_size = table.Column<int>(type: "integer", nullable: false),
                    next_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("pk_count_schedules", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pick_wave_line_breakdowns",
                schema: "warehouse",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pick_wave_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
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
                    table.PrimaryKey("pk_pick_wave_line_breakdowns", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pick_waves_consolidation",
                schema: "warehouse",
                table: "pick_waves",
                columns: new[] { "period_from", "period_to", "geography_level", "geography_value" });

            migrationBuilder.CreateIndex(
                name: "ix_count_schedules_active_due",
                schema: "warehouse",
                table: "count_schedules",
                columns: new[] { "is_active", "next_run_at" });

            migrationBuilder.CreateIndex(
                name: "ix_count_schedules_next_run",
                schema: "warehouse",
                table: "count_schedules",
                column: "next_run_at");

            migrationBuilder.CreateIndex(
                name: "ix_count_schedules_sync_state",
                schema: "warehouse",
                table: "count_schedules",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_count_schedules_tenant_id",
                schema: "warehouse",
                table: "count_schedules",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_count_schedules_tenant_id_company_id",
                schema: "warehouse",
                table: "count_schedules",
                columns: new[] { "tenant_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_count_schedules_tenant_id_store_id",
                schema: "warehouse",
                table: "count_schedules",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pick_wave_breakdowns_wave_line_id",
                schema: "warehouse",
                table: "pick_wave_line_breakdowns",
                column: "pick_wave_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_pick_wave_line_breakdowns_sync_state",
                schema: "warehouse",
                table: "pick_wave_line_breakdowns",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_pick_wave_line_breakdowns_tenant_id",
                schema: "warehouse",
                table: "pick_wave_line_breakdowns",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_pick_wave_line_breakdowns_tenant_id_company_id",
                schema: "warehouse",
                table: "pick_wave_line_breakdowns",
                columns: new[] { "tenant_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pick_wave_line_breakdowns_tenant_id_store_id",
                schema: "warehouse",
                table: "pick_wave_line_breakdowns",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ux_pick_wave_breakdowns_wave_order",
                schema: "warehouse",
                table: "pick_wave_line_breakdowns",
                columns: new[] { "pick_wave_line_id", "order_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "count_schedules",
                schema: "warehouse");

            migrationBuilder.DropTable(
                name: "pick_wave_line_breakdowns",
                schema: "warehouse");

            migrationBuilder.DropIndex(
                name: "ix_pick_waves_consolidation",
                schema: "warehouse",
                table: "pick_waves");

            migrationBuilder.DropColumn(
                name: "company_scope_id",
                schema: "warehouse",
                table: "pick_waves");

            migrationBuilder.DropColumn(
                name: "geography_level",
                schema: "warehouse",
                table: "pick_waves");

            migrationBuilder.DropColumn(
                name: "geography_value",
                schema: "warehouse",
                table: "pick_waves");

            migrationBuilder.DropColumn(
                name: "period_from",
                schema: "warehouse",
                table: "pick_waves");

            migrationBuilder.DropColumn(
                name: "period_to",
                schema: "warehouse",
                table: "pick_waves");
        }
    }
}
