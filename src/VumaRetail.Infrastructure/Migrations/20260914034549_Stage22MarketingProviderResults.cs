using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Stage22MarketingProviderResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "provider_event_id",
                schema: "marketing",
                table: "outbound_messages",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "provider_payload_fingerprint",
                schema: "marketing",
                table: "outbound_messages",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "provider_event_id",
                schema: "marketing",
                table: "outbound_messages");

            migrationBuilder.DropColumn(
                name: "provider_payload_fingerprint",
                schema: "marketing",
                table: "outbound_messages");
        }
    }
}
