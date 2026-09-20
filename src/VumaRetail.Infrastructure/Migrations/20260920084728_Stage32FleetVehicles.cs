using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Stage32FleetVehicles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "vehicles",
                schema: "logistics",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    registration = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    make = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    model = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    vin = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    capacity_kg = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    odometer_km = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    next_service_on = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
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
                    table.PrimaryKey("pk_vehicles", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_vehicles_sync_state",
                schema: "logistics",
                table: "vehicles",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_vehicles_tenant_id",
                schema: "logistics",
                table: "vehicles",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_vehicles_tenant_id_company_id",
                schema: "logistics",
                table: "vehicles",
                columns: new[] { "tenant_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_vehicles_tenant_id_store_id",
                schema: "logistics",
                table: "vehicles",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ux_vehicles_tenant_id_registration",
                schema: "logistics",
                table: "vehicles",
                columns: new[] { "tenant_id", "registration" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_vehicles_tenant_id_vin",
                schema: "logistics",
                table: "vehicles",
                columns: new[] { "tenant_id", "vin" },
                unique: true,
                filter: "vin IS NOT NULL AND deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "vehicles",
                schema: "logistics");

        }
    }
}
