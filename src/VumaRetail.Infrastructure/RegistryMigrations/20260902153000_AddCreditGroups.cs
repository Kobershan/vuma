using Microsoft.EntityFrameworkCore.Migrations;

namespace VumaRetail.Infrastructure.RegistryMigrations
{
    public partial class AddCreditGroups : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "credit_groups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    direction = table.Column<string>(type: "text", nullable: false),
                    limit_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    currency = table.Column<string>(type: "text", nullable: false),
                    exposure_policy = table.Column<string>(type: "text", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credit_groups", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "credit_group_members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    partner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sub_limit_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credit_group_members", x => x.id);
                    table.ForeignKey(
                        name: "fk_credit_group_members_credit_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "credit_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_credit_group_members_group_id",
                table: "credit_group_members",
                column: "group_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "credit_group_members");

            migrationBuilder.DropTable(
                name: "credit_groups");
        }
    }
}