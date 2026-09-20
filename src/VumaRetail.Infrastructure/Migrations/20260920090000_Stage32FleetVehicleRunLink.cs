using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VumaRetail.Infrastructure.Persistence;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations;

[DbContext(typeof(VumaRetailDbContext))]
[Migration("20260920090000_Stage32FleetVehicleRunLink")]
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
        migrationBuilder.DropIndex(name: "ix_delivery_runs_vehicle_id", schema: "logistics", table: "delivery_runs");
        migrationBuilder.DropColumn(name: "vehicle_id", schema: "logistics", table: "delivery_runs");
    }
}
