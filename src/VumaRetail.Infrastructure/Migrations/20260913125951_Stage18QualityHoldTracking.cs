using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Stage18QualityHoldTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "batch_reference",
                schema: "quality",
                table: "quality_holds",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "expiry_date",
                schema: "quality",
                table: "quality_holds",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "serial_number",
                schema: "quality",
                table: "quality_holds",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "batch_reference",
                schema: "quality",
                table: "quality_holds");

            migrationBuilder.DropColumn(
                name: "expiry_date",
                schema: "quality",
                table: "quality_holds");

            migrationBuilder.DropColumn(
                name: "serial_number",
                schema: "quality",
                table: "quality_holds");
        }
    }
}
