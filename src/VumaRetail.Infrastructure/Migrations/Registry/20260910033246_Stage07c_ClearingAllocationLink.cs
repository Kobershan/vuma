using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations.Registry
{
    /// <inheritdoc />
    public partial class Stage07c_ClearingAllocationLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "allocation_id",
                schema: "registry",
                table: "inter_company_clearing_intents",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "ix_inter_company_clearing_intents_tenant_id_group_document_id_",
                schema: "registry",
                table: "inter_company_clearing_intents",
                columns: new[] { "tenant_id", "group_document_id", "allocation_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_inter_company_clearing_intents_tenant_id_group_document_id_",
                schema: "registry",
                table: "inter_company_clearing_intents");

            migrationBuilder.DropColumn(
                name: "allocation_id",
                schema: "registry",
                table: "inter_company_clearing_intents");
        }
    }
}
