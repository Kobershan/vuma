using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.RegistryMigrations
{
    /// <inheritdoc />
    public partial class Stage06d_CreditHoldIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_credit_holds_tenant_id_credit_group_id_company_id_document_",
                schema: "registry",
                table: "credit_holds",
                columns: new[] { "tenant_id", "credit_group_id", "company_id", "document_reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_credit_exposure_entries_tenant_id_credit_group_id_company_i",
                schema: "registry",
                table: "credit_exposure_entries",
                columns: new[] { "tenant_id", "credit_group_id", "company_id", "document_reference" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_credit_holds_tenant_id_credit_group_id_company_id_document_",
                schema: "registry",
                table: "credit_holds");

            migrationBuilder.DropIndex(
                name: "ix_credit_exposure_entries_tenant_id_credit_group_id_company_i",
                schema: "registry",
                table: "credit_exposure_entries");
        }
    }
}
