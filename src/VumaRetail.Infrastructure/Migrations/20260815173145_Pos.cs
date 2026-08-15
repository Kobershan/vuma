using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Pos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "pos");

            migrationBuilder.CreateTable(
                name: "receipt_prints",
                schema: "pos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_id = table.Column<Guid>(type: "uuid", nullable: false),
                    printed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    terminal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_reprint = table.Column<bool>(type: "boolean", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    printed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("pk_receipt_prints", x => x.id);
                    table.CheckConstraint("ck_receipt_prints_reprint_has_reason", "is_reprint = false OR reason IS NOT NULL");
                });

            migrationBuilder.CreateTable(
                name: "sales",
                schema: "pos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    till_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    terminal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operator_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    voided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    void_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    amount_tendered_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    amount_tendered_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    change_given_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    change_given_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
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
                    table.PrimaryKey("pk_sales", x => x.id);
                    table.CheckConstraint("ck_sales_change_not_negative", "change_given_amount >= 0");
                    table.CheckConstraint("ck_sales_completed_has_timestamp", "status <> 'Completed' OR completed_at IS NOT NULL");
                    table.CheckConstraint("ck_sales_voided_has_reason", "status <> 'Voided' OR (voided_at IS NOT NULL AND void_reason IS NOT NULL)");
                });

            migrationBuilder.CreateTable(
                name: "till_sessions",
                schema: "pos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    terminal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operator_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    counted_cash_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    expected_cash_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    opening_float_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    opening_float_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
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
                    table.PrimaryKey("pk_till_sessions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sale_lines",
                schema: "pos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_number = table.Column<int>(type: "integer", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    tax_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    is_voided = table.Column<bool>(type: "boolean", nullable: false),
                    voided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    stock_issue = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    stock_ledger_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    stock_issue_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    discount_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    discount_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
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
                    table.PrimaryKey("pk_sale_lines", x => x.id);
                    table.CheckConstraint("ck_sale_lines_balances", "net_amount + tax_amount = gross_amount");
                    table.CheckConstraint("ck_sale_lines_discount_not_negative", "discount_amount >= 0");
                    table.CheckConstraint("ck_sale_lines_exactly_one_sku", "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int) = 1");
                    table.CheckConstraint("ck_sale_lines_quantity_positive", "quantity_value > 0");
                    table.ForeignKey(
                        name: "fk_sale_lines_sales_sale_id",
                        column: x => x.sale_id,
                        principalSchema: "pos",
                        principalTable: "sales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sale_tenders",
                schema: "pos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    amount_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    amount_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
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
                    table.PrimaryKey("pk_sale_tenders", x => x.id);
                    table.CheckConstraint("ck_sale_tenders_amount_positive", "amount_amount > 0");
                    table.ForeignKey(
                        name: "fk_sale_tenders_sales_sale_id",
                        column: x => x.sale_id,
                        principalSchema: "pos",
                        principalTable: "sales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_receipt_prints_sale_id_printed_at",
                schema: "pos",
                table: "receipt_prints",
                columns: new[] { "sale_id", "printed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_receipt_prints_sync_state",
                schema: "pos",
                table: "receipt_prints",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_receipt_prints_tenant_id",
                schema: "pos",
                table: "receipt_prints",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_receipt_prints_tenant_id_store_id",
                schema: "pos",
                table: "receipt_prints",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sale_lines_item_id",
                schema: "pos",
                table: "sale_lines",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_sale_lines_item_variant_id",
                schema: "pos",
                table: "sale_lines",
                column: "item_variant_id");

            migrationBuilder.CreateIndex(
                name: "ix_sale_lines_stock_issue_refused",
                schema: "pos",
                table: "sale_lines",
                column: "stock_issue",
                filter: "stock_issue = 'Refused' AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_sale_lines_sync_state",
                schema: "pos",
                table: "sale_lines",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_sale_lines_tenant_id",
                schema: "pos",
                table: "sale_lines",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_sale_lines_tenant_id_store_id",
                schema: "pos",
                table: "sale_lines",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ux_sale_lines_sale_id_line_number",
                schema: "pos",
                table: "sale_lines",
                columns: new[] { "sale_id", "line_number" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_sale_tenders_sale_id",
                schema: "pos",
                table: "sale_tenders",
                column: "sale_id");

            migrationBuilder.CreateIndex(
                name: "ix_sale_tenders_sync_state",
                schema: "pos",
                table: "sale_tenders",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_sale_tenders_tenant_id",
                schema: "pos",
                table: "sale_tenders",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_sale_tenders_tenant_id_store_id",
                schema: "pos",
                table: "sale_tenders",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sale_tenders_type_captured_at",
                schema: "pos",
                table: "sale_tenders",
                columns: new[] { "type", "captured_at" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_customer_id",
                schema: "pos",
                table: "sales",
                column: "customer_id",
                filter: "customer_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_sales_sync_state",
                schema: "pos",
                table: "sales",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_sales_tenant_id",
                schema: "pos",
                table: "sales",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_tenant_id_store_id",
                schema: "pos",
                table: "sales",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_terminal_id_parked",
                schema: "pos",
                table: "sales",
                columns: new[] { "terminal_id", "opened_at" },
                filter: "status = 'Parked' AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_sales_till_session_id_status",
                schema: "pos",
                table: "sales",
                columns: new[] { "till_session_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_sales_tenant_id_sale_number",
                schema: "pos",
                table: "sales",
                columns: new[] { "tenant_id", "sale_number" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_till_sessions_sync_state",
                schema: "pos",
                table: "till_sessions",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_till_sessions_tenant_id",
                schema: "pos",
                table: "till_sessions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_till_sessions_tenant_id_store_id",
                schema: "pos",
                table: "till_sessions",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ix_till_sessions_terminal_id_opened_at",
                schema: "pos",
                table: "till_sessions",
                columns: new[] { "terminal_id", "opened_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_till_sessions_tenant_id_terminal_id_open",
                schema: "pos",
                table: "till_sessions",
                columns: new[] { "tenant_id", "terminal_id" },
                unique: true,
                filter: "status = 'Open' AND deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "receipt_prints",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "sale_lines",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "sale_tenders",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "till_sessions",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "sales",
                schema: "pos");
        }
    }
}
