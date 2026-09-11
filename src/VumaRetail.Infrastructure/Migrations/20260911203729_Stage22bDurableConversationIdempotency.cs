using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Stage22bDurableConversationIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "idempotency_key",
                schema: "conversations",
                table: "conversation_turns",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_conversation_turns_tenant_id_conversation_id_idempotency_key",
                schema: "conversations",
                table: "conversation_turns",
                columns: new[] { "tenant_id", "conversation_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_conversation_turns_tenant_id_conversation_id_idempotency_key",
                schema: "conversations",
                table: "conversation_turns");

            migrationBuilder.DropColumn(
                name: "idempotency_key",
                schema: "conversations",
                table: "conversation_turns");
        }
    }
}
