using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Stage26ShiftSwapPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "shift_swap_requests",
                schema: "hr_workforce",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shift_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
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
                    table.PrimaryKey("pk_shift_swap_requests", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_shift_swap_requests_sync_state",
                schema: "hr_workforce",
                table: "shift_swap_requests",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_shift_swap_requests_tenant_id",
                schema: "hr_workforce",
                table: "shift_swap_requests",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_shift_swap_requests_tenant_id_company_id",
                schema: "hr_workforce",
                table: "shift_swap_requests",
                columns: new[] { "tenant_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_shift_swap_requests_tenant_id_from_employee_id_requested_at",
                schema: "hr_workforce",
                table: "shift_swap_requests",
                columns: new[] { "tenant_id", "from_employee_id", "requested_at" });

            migrationBuilder.CreateIndex(
                name: "ix_shift_swap_requests_tenant_id_shift_id_status",
                schema: "hr_workforce",
                table: "shift_swap_requests",
                columns: new[] { "tenant_id", "shift_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_shift_swap_requests_tenant_id_store_id",
                schema: "hr_workforce",
                table: "shift_swap_requests",
                columns: new[] { "tenant_id", "store_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shift_swap_requests",
                schema: "hr_workforce");
        }
    }
}
