using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations;

/// <summary>
/// Migration marker for the Stage 21b foundation migration. The connection foundation tables
/// are created idempotently by the Connect deployment script in this working tree.
/// </summary>
public partial class Stage21b_ConnectFoundation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "connect");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
