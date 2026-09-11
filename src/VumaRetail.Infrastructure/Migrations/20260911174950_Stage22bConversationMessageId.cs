using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Stage22bConversationMessageId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "external_message_id",
                schema: "conversations",
                table: "conversation_turns",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_conversation_turns_tenant_id_conversation_id_external_messa",
                schema: "conversations",
                table: "conversation_turns",
                columns: new[] { "tenant_id", "conversation_id", "external_message_id" },
                unique: true,
                filter: "external_message_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_conversation_turns_tenant_id_conversation_id_external_messa",
                schema: "conversations",
                table: "conversation_turns");

            migrationBuilder.DropColumn(
                name: "external_message_id",
                schema: "conversations",
                table: "conversation_turns");
        }
    }
}
