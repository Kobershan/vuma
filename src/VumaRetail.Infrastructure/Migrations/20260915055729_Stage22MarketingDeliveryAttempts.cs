using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Stage22MarketingDeliveryAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "delivery_attempt_count",
                schema: "marketing",
                table: "outbound_messages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_delivery_attempt_at_utc",
                schema: "marketing",
                table: "outbound_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_delivery_failure",
                schema: "marketing",
                table: "outbound_messages",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "delivery_attempt_count",
                schema: "marketing",
                table: "outbound_messages");

            migrationBuilder.DropColumn(
                name: "last_delivery_attempt_at_utc",
                schema: "marketing",
                table: "outbound_messages");

            migrationBuilder.DropColumn(
                name: "last_delivery_failure",
                schema: "marketing",
                table: "outbound_messages");
        }
    }
}
