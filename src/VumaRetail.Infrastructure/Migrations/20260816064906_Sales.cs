using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Sales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "sales");

            migrationBuilder.CreateTable(
                name: "price_lists",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    prices_include_tax = table.Column<bool>(type: "boolean", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("pk_price_lists", x => x.id);
                    table.CheckConstraint("ck_price_lists_effective_window", "effective_to IS NULL OR effective_to >= effective_from");
                });

            migrationBuilder.CreateTable(
                name: "price_override_logs",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sale_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    operator_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actual_unit_price_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    actual_unit_price_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    quantity_uom = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    quantity_value = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    resolved_unit_price_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    resolved_unit_price_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
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
                    table.PrimaryKey("pk_price_override_logs", x => x.id);
                    table.CheckConstraint("ck_price_override_logs_exactly_one_sku", "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int) = 1");
                    table.CheckConstraint("ck_price_override_logs_prices_not_negative", "resolved_unit_price_amount >= 0 AND actual_unit_price_amount >= 0");
                    table.CheckConstraint("ck_price_override_logs_quantity_positive", "quantity_value > 0");
                });

            migrationBuilder.CreateTable(
                name: "promotions",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    discount_percentage = table.Column<decimal>(type: "numeric(9,4)", nullable: true),
                    reward_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    reward_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: true),
                    required_quantity = table.Column<decimal>(type: "numeric(18,6)", nullable: true),
                    free_quantity = table.Column<decimal>(type: "numeric(18,6)", nullable: true),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    days = table.Column<int>(type: "integer", nullable: true),
                    starts_at = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    ends_at = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    is_exclusive = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("pk_promotions", x => x.id);
                    table.CheckConstraint("ck_promotions_effective_window", "effective_to IS NULL OR effective_to >= effective_from");
                    table.CheckConstraint("ck_promotions_percentage_range", "discount_percentage IS NULL OR (discount_percentage >= 0 AND discount_percentage <= 100)");
                    table.CheckConstraint("ck_promotions_quantities_positive", "(required_quantity IS NULL OR required_quantity > 0) AND (free_quantity IS NULL OR free_quantity > 0)");
                    table.CheckConstraint("ck_promotions_reward_currency_pairs", "(reward_amount IS NULL) = (reward_currency IS NULL)");
                    table.CheckConstraint("ck_promotions_reward_not_negative", "reward_amount IS NULL OR reward_amount >= 0");
                });

            migrationBuilder.CreateTable(
                name: "sales_returns",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_id = table.Column<Guid>(type: "uuid", nullable: false),
                    return_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    refund_tender_type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    authorised_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    raised_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("pk_sales_returns", x => x.id);
                    table.CheckConstraint("ck_sales_returns_amounts_not_negative", "net_amount >= 0 AND tax_amount >= 0 AND gross_amount >= 0");
                    table.CheckConstraint("ck_sales_returns_balances", "net_amount + tax_amount = gross_amount");
                    table.CheckConstraint("ck_sales_returns_cancelled_has_timestamp", "status <> 'Cancelled' OR cancelled_at IS NOT NULL");
                    table.CheckConstraint("ck_sales_returns_completed_has_timestamp", "status <> 'Completed' OR completed_at IS NOT NULL");
                });

            migrationBuilder.CreateTable(
                name: "price_list_lines",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    price_list_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    minimum_quantity = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
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
                    table.PrimaryKey("pk_price_list_lines", x => x.id);
                    table.CheckConstraint("ck_price_list_lines_exactly_one_sku", "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int) = 1");
                    table.CheckConstraint("ck_price_list_lines_minimum_quantity_positive", "minimum_quantity > 0");
                    table.CheckConstraint("ck_price_list_lines_price_not_negative", "unit_price_amount >= 0");
                    table.ForeignKey(
                        name: "fk_price_list_lines_price_lists_price_list_id",
                        column: x => x.price_list_id,
                        principalSchema: "sales",
                        principalTable: "price_lists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "promotion_lines",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    promotion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    category_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
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
                    table.PrimaryKey("pk_promotion_lines", x => x.id);
                    table.CheckConstraint("ck_promotion_lines_exactly_one_target", "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int + (category_code IS NOT NULL)::int) = 1");
                    table.ForeignKey(
                        name: "fk_promotion_lines_promotions_promotion_id",
                        column: x => x.promotion_id,
                        principalSchema: "sales",
                        principalTable: "promotions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sales_return_lines",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sales_return_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    previously_returned_quantity = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    tax_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    original_stock_ledger_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    stock_return = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    stock_ledger_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    stock_return_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    gross_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    gross_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    net_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    net_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    original_quantity_uom = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    original_quantity_value = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
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
                    table.PrimaryKey("pk_sales_return_lines", x => x.id);
                    table.CheckConstraint("ck_sales_return_lines_amounts_not_negative", "net_amount >= 0 AND tax_amount >= 0 AND gross_amount >= 0 AND unit_price_amount >= 0");
                    table.CheckConstraint("ck_sales_return_lines_balances", "net_amount + tax_amount = gross_amount");
                    table.CheckConstraint("ck_sales_return_lines_exactly_one_sku", "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int) = 1");
                    table.CheckConstraint("ck_sales_return_lines_previously_returned_not_negative", "previously_returned_quantity >= 0");
                    table.CheckConstraint("ck_sales_return_lines_quantity_positive", "quantity_value > 0");
                    table.CheckConstraint("ck_sales_return_lines_within_quantity_sold", "quantity_value + previously_returned_quantity <= original_quantity_value");
                    table.ForeignKey(
                        name: "fk_sales_return_lines_sales_returns_sales_return_id",
                        column: x => x.sales_return_id,
                        principalSchema: "sales",
                        principalTable: "sales_returns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_price_list_lines_item_id",
                schema: "sales",
                table: "price_list_lines",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_price_list_lines_item_variant_id",
                schema: "sales",
                table: "price_list_lines",
                column: "item_variant_id");

            migrationBuilder.CreateIndex(
                name: "ix_price_list_lines_sync_state",
                schema: "sales",
                table: "price_list_lines",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_price_list_lines_tenant_id",
                schema: "sales",
                table: "price_list_lines",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_price_list_lines_tenant_id_store_id",
                schema: "sales",
                table: "price_list_lines",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ux_price_list_lines_list_id_sku_minimum_quantity",
                schema: "sales",
                table: "price_list_lines",
                columns: new[] { "price_list_id", "item_id", "item_variant_id", "minimum_quantity" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_price_lists_store_id_effective_active",
                schema: "sales",
                table: "price_lists",
                columns: new[] { "store_id", "effective_from" },
                filter: "is_active = true AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_price_lists_sync_state",
                schema: "sales",
                table: "price_lists",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_price_lists_tenant_id",
                schema: "sales",
                table: "price_lists",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_price_lists_tenant_id_store_id",
                schema: "sales",
                table: "price_lists",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ux_price_lists_tenant_id_code",
                schema: "sales",
                table: "price_lists",
                columns: new[] { "tenant_id", "code" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_price_override_logs_occurred_at",
                schema: "sales",
                table: "price_override_logs",
                column: "occurred_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_price_override_logs_operator_user_id_occurred_at",
                schema: "sales",
                table: "price_override_logs",
                columns: new[] { "operator_user_id", "occurred_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_price_override_logs_sale_id",
                schema: "sales",
                table: "price_override_logs",
                column: "sale_id",
                filter: "sale_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_price_override_logs_sync_state",
                schema: "sales",
                table: "price_override_logs",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_price_override_logs_tenant_id",
                schema: "sales",
                table: "price_override_logs",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_price_override_logs_tenant_id_store_id",
                schema: "sales",
                table: "price_override_logs",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ix_promotion_lines_item_id",
                schema: "sales",
                table: "promotion_lines",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_promotion_lines_promotion_id",
                schema: "sales",
                table: "promotion_lines",
                column: "promotion_id");

            migrationBuilder.CreateIndex(
                name: "ix_promotion_lines_sync_state",
                schema: "sales",
                table: "promotion_lines",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_promotion_lines_tenant_id",
                schema: "sales",
                table: "promotion_lines",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_promotion_lines_tenant_id_store_id",
                schema: "sales",
                table: "promotion_lines",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ix_promotions_store_id_effective_active",
                schema: "sales",
                table: "promotions",
                columns: new[] { "store_id", "effective_from" },
                filter: "is_active = true AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_promotions_sync_state",
                schema: "sales",
                table: "promotions",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_promotions_tenant_id",
                schema: "sales",
                table: "promotions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_promotions_tenant_id_store_id",
                schema: "sales",
                table: "promotions",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ux_promotions_tenant_id_code",
                schema: "sales",
                table: "promotions",
                columns: new[] { "tenant_id", "code" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_sales_return_lines_sale_line_id",
                schema: "sales",
                table: "sales_return_lines",
                column: "sale_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_return_lines_stock_return_refused",
                schema: "sales",
                table: "sales_return_lines",
                column: "stock_return",
                filter: "stock_return = 'Refused' AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_sales_return_lines_sync_state",
                schema: "sales",
                table: "sales_return_lines",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_sales_return_lines_tenant_id",
                schema: "sales",
                table: "sales_return_lines",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_return_lines_tenant_id_store_id",
                schema: "sales",
                table: "sales_return_lines",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ux_sales_return_lines_return_id_sale_line_id",
                schema: "sales",
                table: "sales_return_lines",
                columns: new[] { "sales_return_id", "sale_line_id" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_sales_returns_sale_id",
                schema: "sales",
                table: "sales_returns",
                column: "sale_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_returns_status_completed_at",
                schema: "sales",
                table: "sales_returns",
                columns: new[] { "status", "completed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_returns_sync_state",
                schema: "sales",
                table: "sales_returns",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_sales_returns_tenant_id",
                schema: "sales",
                table: "sales_returns",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_returns_tenant_id_store_id",
                schema: "sales",
                table: "sales_returns",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ux_sales_returns_tenant_id_return_number",
                schema: "sales",
                table: "sales_returns",
                columns: new[] { "tenant_id", "return_number" },
                unique: true,
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "price_list_lines",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "price_override_logs",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "promotion_lines",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "sales_return_lines",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "price_lists",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "promotions",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "sales_returns",
                schema: "sales");
        }
    }
}
