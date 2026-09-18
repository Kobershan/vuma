using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VumaRetail.Infrastructure.Persistence;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations;

/// <summary>Adds durable request ids for warehouse create-command replay.</summary>
[DbContext(typeof(VumaRetailDbContext))]
[Migration("20260918120000_WarehouseRequestIds")]
public partial class WarehouseRequestIds : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>("request_id", table: "pick_tasks", schema: "warehouse", nullable: true);
        migrationBuilder.AddColumn<Guid>("request_id", table: "putaway_tasks", schema: "warehouse", nullable: true);

        migrationBuilder.CreateIndex("ux_pick_tasks_request_id", table: "pick_tasks", column: "request_id", schema: "warehouse", unique: true,
            filter: "request_id IS NOT NULL AND deleted_at IS NULL");
        migrationBuilder.CreateIndex("ux_putaway_tasks_request_id", table: "putaway_tasks", column: "request_id", schema: "warehouse", unique: true,
            filter: "request_id IS NOT NULL AND deleted_at IS NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("ux_pick_tasks_request_id", table: "pick_tasks", schema: "warehouse");
        migrationBuilder.DropIndex("ux_putaway_tasks_request_id", table: "putaway_tasks", schema: "warehouse");
        migrationBuilder.DropColumn("request_id", table: "pick_tasks", schema: "warehouse");
        migrationBuilder.DropColumn("request_id", table: "putaway_tasks", schema: "warehouse");
    }
}
