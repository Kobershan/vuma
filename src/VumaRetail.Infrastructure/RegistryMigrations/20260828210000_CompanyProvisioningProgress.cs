using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VumaRetail.Infrastructure.Persistence;

namespace VumaRetail.Infrastructure.RegistryMigrations;

[DbContext(typeof(VumaRegistryDbContext))]
[Migration("20260828210000_CompanyProvisioningProgress")]
public partial class CompanyProvisioningProgress : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "provisioning_step", schema: "registry", table: "companies",
            type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: "provisioning");
        migrationBuilder.AddColumn<string>(
            name: "provisioning_error", schema: "registry", table: "companies",
            type: "character varying(256)", maxLength: 256, nullable: true);
        migrationBuilder.AddColumn<int>(
            name: "provisioning_attempts", schema: "registry", table: "companies",
            type: "integer", nullable: false, defaultValue: 0);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "provisioning_attempts", schema: "registry", table: "companies");
        migrationBuilder.DropColumn(name: "provisioning_error", schema: "registry", table: "companies");
        migrationBuilder.DropColumn(name: "provisioning_step", schema: "registry", table: "companies");
    }
}
