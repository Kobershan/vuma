using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Stage29ReportExportArtifactsFix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "artifact_reference",
                schema: "reporting",
                table: "report_exports",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "artifact_reference",
                schema: "reporting",
                table: "report_exports");
        }
    }
}
