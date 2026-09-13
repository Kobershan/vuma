using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Stage23CustomerWaitPause : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "customer_wait_started_at_utc",
                schema: "service",
                table: "service_tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "customer_wait_working_hours",
                schema: "service",
                table: "service_tickets",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "customer_wait_started_at_utc",
                schema: "service",
                table: "service_tickets");

            migrationBuilder.DropColumn(
                name: "customer_wait_working_hours",
                schema: "service",
                table: "service_tickets");
        }
    }
}
