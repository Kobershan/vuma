using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AuditTerminalPinLockout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "failed_pin_attempts",
                schema: "identity",
                table: "terminals",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "pin_locked_until",
                schema: "identity",
                table: "terminals",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "failed_pin_attempts",
                schema: "identity",
                table: "terminals");

            migrationBuilder.DropColumn(
                name: "pin_locked_until",
                schema: "identity",
                table: "terminals");
        }
    }
}
