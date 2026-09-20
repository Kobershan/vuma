using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations;

public partial class Stage32FleetVehicleRunLink : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "vehicle_id",
            schema: "logistics",
            table: "delivery_runs",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ix_delivery_runs_vehicle_id",
            schema: "logistics",
            table: "delivery_runs",
            column: "vehicle_id",
            filter: "vehicle_id IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("ix_delivery_runs_vehicle_id", "logistics", "delivery_runs");
        migrationBuilder.DropColumn("vehicle_id", "logistics", "delivery_runs");
    }
}
