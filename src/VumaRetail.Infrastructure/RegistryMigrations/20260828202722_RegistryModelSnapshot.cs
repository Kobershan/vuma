using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.RegistryMigrations;

/// <summary>Records the registry model already created by the explicit registry migrations.</summary>
public partial class RegistryModelSnapshot : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // The registry schema is created by RegistryCompanies and RegistryGroupsAndSagas.
        // This migration supplies EF's model snapshot without replaying those tables.
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The preceding registry migrations own the schema teardown.
    }
}
