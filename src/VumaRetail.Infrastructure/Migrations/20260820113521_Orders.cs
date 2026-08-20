using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Orders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "orders");

            migrationBuilder.CreateTable(
                name: "sales_order_returns",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sales_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    return_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    authorised_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    refund_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    raised_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    gross_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    gross_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    net_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    net_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    tax_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    tax_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    row_version = table.Column<byte[]>(type: "bytea", nullable: false),
                    sync_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    sync_stamp = table.Column<string>(type: "character varying(86)", maxLength: 86, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_order_returns", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sales_orders",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    partner_id = table.Column<Guid>(type: "uuid", nullable: true),
                    channel = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    fulfilment_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    fulfilling_location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delivery_address_line1 = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    delivery_address_city = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    delivery_address_country_code = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: true),
                    delivery_address_line2 = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    delivery_address_region = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    delivery_address_postal_code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    payment_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    settling_sale_id = table.Column<Guid>(type: "uuid", nullable: true),
                    settling_customer_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    order_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    requested_fulfilment_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_revenue_recognised = table.Column<bool>(type: "boolean", nullable: false),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    gross_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    gross_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    net_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    net_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    tax_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    tax_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    row_version = table.Column<byte[]>(type: "bytea", nullable: false),
                    sync_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    sync_stamp = table.Column<string>(type: "character varying(86)", maxLength: 86, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_orders", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sales_order_return_lines",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sales_order_return_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sales_order_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    previously_returned_quantity = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    stock_return = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    stock_ledger_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    stock_return_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    fulfilled_quantity_uom = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    fulfilled_quantity_value = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    gross_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    gross_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    net_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    net_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    quantity_uom = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    quantity_value = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    tax_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    tax_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    unit_price_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    unit_price_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    row_version = table.Column<byte[]>(type: "bytea", nullable: false),
                    sync_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    sync_stamp = table.Column<string>(type: "character varying(86)", maxLength: 86, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_order_return_lines", x => x.id);
                    table.CheckConstraint("ck_sales_order_return_lines_exactly_one_sku", "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int) = 1");
                    table.ForeignKey(
                        name: "fk_sales_order_return_lines_sales_order_returns_sales_order_re",
                        column: x => x.sales_order_return_id,
                        principalSchema: "orders",
                        principalTable: "sales_order_returns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sales_order_lines",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sales_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    price_list_id = table.Column<Guid>(type: "uuid", nullable: true),
                    promotions_summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    line_status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    backordered_quantity_uom = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    backordered_quantity_value = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    discount_amount_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    discount_amount_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    requested_quantity_uom = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    requested_quantity_value = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    tax_amount_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    tax_amount_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    unit_price_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    unit_price_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    row_version = table.Column<byte[]>(type: "bytea", nullable: false),
                    sync_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    sync_stamp = table.Column<string>(type: "character varying(86)", maxLength: 86, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_order_lines", x => x.id);
                    table.CheckConstraint("ck_sales_order_lines_exactly_one_sku", "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int) = 1");
                    table.ForeignKey(
                        name: "fk_sales_order_lines_sales_orders_sales_order_id",
                        column: x => x.sales_order_id,
                        principalSchema: "orders",
                        principalTable: "sales_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sales_order_lines_sales_order_id",
                schema: "orders",
                table: "sales_order_lines",
                column: "sales_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_order_lines_sync_state",
                schema: "orders",
                table: "sales_order_lines",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_sales_order_lines_tenant_id",
                schema: "orders",
                table: "sales_order_lines",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_order_lines_tenant_id_store_id",
                schema: "orders",
                table: "sales_order_lines",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_order_return_lines_sales_order_line_id",
                schema: "orders",
                table: "sales_order_return_lines",
                column: "sales_order_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_order_return_lines_sales_order_return_id",
                schema: "orders",
                table: "sales_order_return_lines",
                column: "sales_order_return_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_order_return_lines_sync_state",
                schema: "orders",
                table: "sales_order_return_lines",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_sales_order_return_lines_tenant_id",
                schema: "orders",
                table: "sales_order_return_lines",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_order_return_lines_tenant_id_store_id",
                schema: "orders",
                table: "sales_order_return_lines",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_order_returns_sales_order_id",
                schema: "orders",
                table: "sales_order_returns",
                column: "sales_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_order_returns_sync_state",
                schema: "orders",
                table: "sales_order_returns",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_sales_order_returns_tenant_id",
                schema: "orders",
                table: "sales_order_returns",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_order_returns_tenant_id_store_id",
                schema: "orders",
                table: "sales_order_returns",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ux_sales_order_returns_tenant_id_return_number",
                schema: "orders",
                table: "sales_order_returns",
                columns: new[] { "tenant_id", "return_number" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_sales_orders_fulfilling_location_id",
                schema: "orders",
                table: "sales_orders",
                column: "fulfilling_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_orders_partner_id",
                schema: "orders",
                table: "sales_orders",
                column: "partner_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_orders_status",
                schema: "orders",
                table: "sales_orders",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_sales_orders_sync_state",
                schema: "orders",
                table: "sales_orders",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_sales_orders_tenant_id",
                schema: "orders",
                table: "sales_orders",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_orders_tenant_id_store_id",
                schema: "orders",
                table: "sales_orders",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ux_sales_orders_tenant_id_order_number",
                schema: "orders",
                table: "sales_orders",
                columns: new[] { "tenant_id", "order_number" },
                unique: true,
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sales_order_lines",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "sales_order_return_lines",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "sales_orders",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "sales_order_returns",
                schema: "orders");
        }
    }
}
