using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Stage07c_ArReceipt_ApPayment_Extensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "group_document_id",
                schema: "finance",
                table: "ar_receipts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "intent_id",
                schema: "finance",
                table: "ar_receipts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "group_document_id",
                schema: "finance",
                table: "ap_payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "intent_id",
                schema: "finance",
                table: "ap_payments",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "group_document_id",
                schema: "finance",
                table: "ar_receipts");

            migrationBuilder.DropColumn(
                name: "intent_id",
                schema: "finance",
                table: "ar_receipts");

            migrationBuilder.DropColumn(
                name: "group_document_id",
                schema: "finance",
                table: "ap_payments");

            migrationBuilder.DropColumn(
                name: "intent_id",
                schema: "finance",
                table: "ap_payments");
        }
    }
}
