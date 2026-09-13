using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Stage18CorrectiveActionOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "closure_operation_id",
                schema: "quality",
                table: "non_conformances",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "corrective_action_operation_id",
                schema: "quality",
                table: "non_conformances",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "closure_operation_id",
                schema: "quality",
                table: "non_conformances");

            migrationBuilder.DropColumn(
                name: "corrective_action_operation_id",
                schema: "quality",
                table: "non_conformances");
        }
    }
}
