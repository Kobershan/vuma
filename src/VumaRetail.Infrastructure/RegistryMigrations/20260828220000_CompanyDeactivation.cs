using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VumaRetail.Infrastructure.Persistence;

namespace VumaRetail.Infrastructure.RegistryMigrations;

[DbContext(typeof(VumaRegistryDbContext))]
[Migration("20260828220000_CompanyDeactivation")]
public partial class CompanyDeactivation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(name: "deactivated_at", table: "companies", schema: "registry", nullable: true);
        migrationBuilder.AddColumn<string>(name: "deactivated_by", table: "companies", schema: "registry", type: "character varying(256)", maxLength: 256, nullable: true);
        migrationBuilder.AddColumn<string>(name: "deactivation_reason", table: "companies", schema: "registry", type: "character varying(500)", maxLength: 500, nullable: true);
        migrationBuilder.CreateTable(
            name: "company_lifecycle_audits", schema: "registry",
            columns: table => new
            {
                id = table.Column<Guid>("uuid", nullable: false),
                tenant_id = table.Column<Guid>("uuid", nullable: false),
                company_id = table.Column<Guid>("uuid", nullable: false),
                from_state = table.Column<string>("character varying(32)", maxLength: 32, nullable: false),
                to_state = table.Column<string>("character varying(32)", maxLength: 32, nullable: false),
                actor = table.Column<string>("character varying(256)", maxLength: 256, nullable: false),
                reason = table.Column<string>("character varying(500)", maxLength: 500, nullable: false),
                occurred_at = table.Column<DateTimeOffset>("timestamp with time zone", nullable: false)
            }, constraints: table => table.PrimaryKey("pk_company_lifecycle_audits", x => x.id));
        migrationBuilder.CreateIndex("ix_company_lifecycle_audits_tenant_id_company_id_occurred_at", "company_lifecycle_audits", new[] { "tenant_id", "company_id", "occurred_at" }, schema: "registry");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("company_lifecycle_audits", "registry");
        migrationBuilder.DropColumn("deactivated_at", "companies", "registry");
        migrationBuilder.DropColumn("deactivated_by", "companies", "registry");
        migrationBuilder.DropColumn("deactivation_reason", "companies", "registry");
    }
}
