using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Stage23_ServiceTicketOperationId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "operation_id",
                schema: "service",
                table: "service_tickets",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "ux_service_tickets_tenant_operation",
                schema: "service",
                table: "service_tickets",
                columns: new[] { "tenant_id", "operation_id" },
                unique: true,
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_service_tickets_tenant_operation",
                schema: "service",
                table: "service_tickets");

            migrationBuilder.DropColumn(
                name: "operation_id",
                schema: "service",
                table: "service_tickets");
        }
    }
}
