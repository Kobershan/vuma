using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations;

/// <summary>Renames the persisted procurement comparison from its former net label to gross.</summary>
public partial class MatchedGrossColumn : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "ordered_gross_value_amount",
            schema: "procurement",
            table: "supplier_invoice_match_lines",
            type: "numeric(18,4)",
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<string>(
            name: "ordered_gross_value_currency",
            schema: "procurement",
            table: "supplier_invoice_match_lines",
            type: "character(3)",
            fixedLength: true,
            maxLength: 3,
            nullable: false,
            defaultValue: "ZAR");

        migrationBuilder.RenameColumn(
            name: "matched_net_amount",
            schema: "procurement",
            table: "supplier_invoice_matches",
            newName: "matched_gross_amount");

        migrationBuilder.RenameColumn(
            name: "matched_net_currency",
            schema: "procurement",
            table: "supplier_invoice_matches",
            newName: "matched_gross_currency");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameColumn(
            name: "matched_gross_amount",
            schema: "procurement",
            table: "supplier_invoice_matches",
            newName: "matched_net_amount");

        migrationBuilder.RenameColumn(
            name: "matched_gross_currency",
            schema: "procurement",
            table: "supplier_invoice_matches",
            newName: "matched_net_currency");

        migrationBuilder.DropColumn(
            name: "ordered_gross_value_amount",
            schema: "procurement",
            table: "supplier_invoice_match_lines");

        migrationBuilder.DropColumn(
            name: "ordered_gross_value_currency",
            schema: "procurement",
            table: "supplier_invoice_match_lines");
    }
}
