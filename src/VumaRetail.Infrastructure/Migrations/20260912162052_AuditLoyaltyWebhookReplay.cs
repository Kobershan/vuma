using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations;

/// <summary>Adds durable replay markers for loyalty webhook ordering.</summary>
public partial class AuditLoyaltyWebhookReplay : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "last_webhook_event_id",
            schema: "loyalty",
            table: "members",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "last_webhook_version",
            schema: "loyalty",
            table: "members",
            type: "bigint",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "last_webhook_event_id", schema: "loyalty", table: "members");
        migrationBuilder.DropColumn(name: "last_webhook_version", schema: "loyalty", table: "members");
    }
}
