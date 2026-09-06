using Microsoft.EntityFrameworkCore.Migrations;

namespace VumaRetail.Infrastructure.RegistryMigrations
{
    public partial class AddCreditHoldsAndExposureEntries : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "credit_holds",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_ref = table.Column<string>(type: "text", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credit_holds", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "credit_exposure_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_ref = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credit_exposure_entries", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_credit_exposure_entries_document_ref",
                table: "credit_exposure_entries",
                column: "document_ref",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "credit_holds");

            migrationBuilder.DropTable(
                name: "credit_exposure_entries");
        }
    }
}