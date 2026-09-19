using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SalesReturnRequestIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "request_id",
                schema: "sales",
                table: "sales_returns",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_sales_returns_tenant_sale_request",
                schema: "sales",
                table: "sales_returns",
                columns: new[] { "tenant_id", "sale_id", "request_id" },
                unique: true,
                filter: "request_id IS NOT NULL AND deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_sales_returns_tenant_sale_request",
                schema: "sales",
                table: "sales_returns");

            migrationBuilder.DropColumn(
                name: "request_id",
                schema: "sales",
                table: "sales_returns");

        }
    }
}
