using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations;

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
