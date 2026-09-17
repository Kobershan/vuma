using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Stage22MarketingRetryScheduling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "next_attempt_at_utc",
                schema: "marketing",
                table: "outbound_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_outbound_messages_tenant_company_provider_event",
                schema: "marketing",
                table: "outbound_messages",
                columns: new[] { "tenant_id", "company_id", "provider_event_id" },
                unique: true,
                filter: "provider_event_id IS NOT NULL AND deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_outbound_messages_tenant_company_provider_event",
                schema: "marketing",
                table: "outbound_messages");

            migrationBuilder.DropColumn(
                name: "next_attempt_at_utc",
                schema: "marketing",
                table: "outbound_messages");
        }
    }
}
