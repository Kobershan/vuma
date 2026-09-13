using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Stage27DepreciationRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "depreciation_runs",
                schema: "assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_book_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period = table.Column<DateOnly>(type: "date", nullable: false),
                    amount_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    amount_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    closing_net_book_value_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    closing_net_book_value_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
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
                    table.PrimaryKey("pk_depreciation_runs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_depreciation_runs_sync_state",
                schema: "assets",
                table: "depreciation_runs",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_depreciation_runs_tenant_id",
                schema: "assets",
                table: "depreciation_runs",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_depreciation_runs_tenant_id_company_id",
                schema: "assets",
                table: "depreciation_runs",
                columns: new[] { "tenant_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_depreciation_runs_tenant_id_store_id",
                schema: "assets",
                table: "depreciation_runs",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ux_depreciation_runs_tenant_company_book_period",
                schema: "assets",
                table: "depreciation_runs",
                columns: new[] { "tenant_id", "company_id", "asset_book_id", "period" },
                unique: true,
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "depreciation_runs",
                schema: "assets");
        }
    }
}
