using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VumaRetail.Infrastructure.Persistence;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations;

[DbContext(typeof(VumaRetailDbContext))]
[Migration("20260914201500_Stage21AuthoritativeCheckoutOrder")]
public partial class Stage21AuthoritativeCheckoutOrder : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "authoritative_order_id",
            schema: "ecommerce",
            table: "checkout_intents",
            type: "uuid",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "authoritative_order_id",
            schema: "ecommerce",
            table: "checkout_intents");
    }
}
