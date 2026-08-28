using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VumaRetail.Infrastructure.Persistence;

namespace VumaRetail.Infrastructure.RegistryMigrations;

/// <summary>Creates the first registry table. Registry migrations are independent of company migrations.</summary>
[DbContext(typeof(VumaRegistryDbContext))]
[Migration("20260827192000_RegistryCompanies")]
public partial class RegistryCompanies : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "registry");

        migrationBuilder.CreateTable(
            name: "companies",
            schema: "registry",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                legal_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                trading_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                registration_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                tax_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                base_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                locale = table.Column<string>(type: "character varying(35)", maxLength: 35, nullable: false),
                document_prefix = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                connection_secret_ref = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                schema_version = table.Column<long>(type: "bigint", nullable: false),
                migration_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                lifecycle_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                is_active = table.Column<bool>(type: "boolean", nullable: false),
            },
            constraints: table => table.PrimaryKey("pk_companies", x => x.id));

        migrationBuilder.CreateIndex("ix_companies_tenant_id_code_unique", "companies", new[] { "tenant_id", "code" }, unique: true, schema: "registry");
        migrationBuilder.CreateIndex("ix_companies_tenant_id_document_prefix", "companies", new[] { "tenant_id", "document_prefix" }, unique: true, schema: "registry");
        migrationBuilder.CreateIndex("ix_companies_tenant_id_is_active", "companies", new[] { "tenant_id", "is_active" }, schema: "registry");
        migrationBuilder.AddCheckConstraint("ck_companies_lifecycle_state", "companies", "lifecycle_state IN ('Provisioning', 'Seeding', 'Registered', 'Active', 'Deactivated')", "registry");
        migrationBuilder.AddCheckConstraint("ck_companies_active_requires_active_state", "companies", "NOT is_active OR lifecycle_state = 'Active'", "registry");
        migrationBuilder.AddCheckConstraint("ck_companies_active_requires_secret", "companies", "NOT is_active OR connection_secret_ref IS NOT NULL", "registry");

    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint("ck_companies_active_requires_secret", "companies", "registry");
        migrationBuilder.DropCheckConstraint("ck_companies_active_requires_active_state", "companies", "registry");
        migrationBuilder.DropCheckConstraint("ck_companies_lifecycle_state", "companies", "registry");
        migrationBuilder.DropTable(name: "companies", schema: "registry");
    }
}
