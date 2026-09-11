using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.RegistryMigrations
{
    /// <inheritdoc />
    public partial class Stage22RelatedTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "related_transfer_id",
                schema: "registry",
                table: "stock_transfer_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "relation",
                schema: "registry",
                table: "stock_transfer_requests",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Original");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "related_transfer_id",
                schema: "registry",
                table: "stock_transfer_requests");

            migrationBuilder.DropColumn(
                name: "relation",
                schema: "registry",
                table: "stock_transfer_requests");
        }
    }
}
