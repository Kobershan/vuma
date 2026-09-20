using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations;

/// <summary>Adds one-time POS dispatch state and store contact details used on receipts.</summary>
public partial class PosDispatchAndStoreContact : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "email",
            schema: "platform",
            table: "stores",
            type: "character varying(256)",
            maxLength: 256,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "phone",
            schema: "platform",
            table: "stores",
            type: "character varying(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset?>(
            name: "dispatched_at",
            schema: "pos",
            table: "sales",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "dispatched_by_user_id",
            schema: "pos",
            table: "sales",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ix_sales_tenant_id_dispatched_at",
            schema: "pos",
            table: "sales",
            columns: new[] { "tenant_id", "dispatched_at" },
            filter: "dispatched_at IS NOT NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_sales_tenant_id_dispatched_at",
            schema: "pos",
            table: "sales");

        migrationBuilder.DropColumn(name: "email", schema: "platform", table: "stores");
        migrationBuilder.DropColumn(name: "phone", schema: "platform", table: "stores");
        migrationBuilder.DropColumn(name: "dispatched_at", schema: "pos", table: "sales");
        migrationBuilder.DropColumn(name: "dispatched_by_user_id", schema: "pos", table: "sales");
    }
}
