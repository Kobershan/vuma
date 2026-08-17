using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Procurement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "procurement");

            migrationBuilder.CreateTable(
                name: "goods_receipts",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    partner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    delivery_note_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    received_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    received_value_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    received_value_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
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
                    table.PrimaryKey("pk_goods_receipts", x => x.id);
                    table.CheckConstraint("ck_goods_receipts_cancelled_has_timestamp", "status <> 'Cancelled' OR cancelled_at IS NOT NULL");
                    table.CheckConstraint("ck_goods_receipts_completed_has_timestamp", "status <> 'Completed' OR completed_at IS NOT NULL");
                    table.CheckConstraint("ck_goods_receipts_value_not_negative", "received_value_amount >= 0");
                });

            migrationBuilder.CreateTable(
                name: "purchase_orders",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    partner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expected_at = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    amends_purchase_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rfq_response_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    raised_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("pk_purchase_orders", x => x.id);
                    table.CheckConstraint("ck_purchase_orders_amounts_not_negative", "net_amount >= 0 AND tax_amount >= 0 AND gross_amount >= 0");
                    table.CheckConstraint("ck_purchase_orders_balances", "net_amount + tax_amount = gross_amount");
                    table.CheckConstraint("ck_purchase_orders_cancelled_has_reason", "status <> 'Cancelled' OR cancellation_reason IS NOT NULL");
                    table.CheckConstraint("ck_purchase_orders_issued_was_approved", "issued_at IS NULL OR approved_at IS NOT NULL");
                    table.CheckConstraint("ck_purchase_orders_version_positive", "version >= 1");
                });

            migrationBuilder.CreateTable(
                name: "purchase_requisitions",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    requisition_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    required_by = table.Column<DateOnly>(type: "date", nullable: false),
                    justification = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    raised_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decided_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("pk_purchase_requisitions", x => x.id);
                    table.CheckConstraint("ck_purchase_requisitions_rejected_has_reason", "status <> 'Rejected' OR rejection_reason IS NOT NULL");
                });

            migrationBuilder.CreateTable(
                name: "rfqs",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rfq_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    purchase_requisition_id = table.Column<Guid>(type: "uuid", nullable: true),
                    closes_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    raised_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    awarded_response_id = table.Column<Guid>(type: "uuid", nullable: true),
                    awarded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("pk_rfqs", x => x.id);
                    table.CheckConstraint("ck_rfqs_awarded_has_response", "status <> 'Awarded' OR awarded_response_id IS NOT NULL");
                });

            migrationBuilder.CreateTable(
                name: "supplier_invoice_matches",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    partner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_invoice_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    invoice_date = table.Column<DateOnly>(type: "date", nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    price_tolerance_percentage = table.Column<decimal>(type: "numeric(9,4)", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    variances = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    matched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    released_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    journal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    claimed_gross_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    claimed_gross_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    claimed_net_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    claimed_net_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    claimed_tax_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    claimed_tax_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    matched_net_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    matched_net_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    price_tolerance_floor_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    price_tolerance_floor_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    price_variance_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    price_variance_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
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
                    table.PrimaryKey("pk_supplier_invoice_matches", x => x.id);
                    table.CheckConstraint("ck_supplier_invoice_matches_balances", "claimed_net_amount + claimed_tax_amount = claimed_gross_amount");
                    table.CheckConstraint("ck_supplier_invoice_matches_blocked_not_released", "status <> 'Blocked' OR released_at IS NULL");
                    table.CheckConstraint("ck_supplier_invoice_matches_claim_not_negative", "claimed_net_amount >= 0 AND claimed_tax_amount >= 0 AND claimed_gross_amount >= 0");
                    table.CheckConstraint("ck_supplier_invoice_matches_released_has_user", "released_at IS NULL OR released_by_user_id IS NOT NULL");
                    table.CheckConstraint("ck_supplier_invoice_matches_tolerance_range", "price_tolerance_percentage >= 0 AND price_tolerance_percentage <= 100 AND price_tolerance_floor_amount >= 0");
                });

            migrationBuilder.CreateTable(
                name: "supplier_scorecards",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    partner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    orders_placed = table.Column<int>(type: "integer", nullable: false),
                    lines_ordered = table.Column<int>(type: "integer", nullable: false),
                    lines_delivered = table.Column<int>(type: "integer", nullable: false),
                    lines_delivered_on_time = table.Column<int>(type: "integer", nullable: false),
                    lines_with_rejections = table.Column<int>(type: "integer", nullable: false),
                    quantity_ordered = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    quantity_received = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    quantity_rejected = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    on_time_delivery_rate = table.Column<decimal>(type: "numeric(9,4)", nullable: false),
                    fill_rate = table.Column<decimal>(type: "numeric(9,4)", nullable: false),
                    quality_rate = table.Column<decimal>(type: "numeric(9,4)", nullable: false),
                    overall_rating = table.Column<decimal>(type: "numeric(9,4)", nullable: false),
                    snapshotted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    price_variance_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    price_variance_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    purchase_value_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    purchase_value_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
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
                    table.PrimaryKey("pk_supplier_scorecards", x => x.id);
                    table.CheckConstraint("ck_supplier_scorecards_counts_not_negative", "orders_placed >= 0 AND lines_ordered >= 0 AND lines_delivered >= 0 AND lines_delivered_on_time >= 0 AND lines_with_rejections >= 0");
                    table.CheckConstraint("ck_supplier_scorecards_on_time_within_delivered", "lines_delivered_on_time <= lines_delivered");
                    table.CheckConstraint("ck_supplier_scorecards_period_ordered", "period_end >= period_start");
                    table.CheckConstraint("ck_supplier_scorecards_rates_in_range", "on_time_delivery_rate BETWEEN 0 AND 100 AND fill_rate BETWEEN 0 AND 100 AND quality_rate BETWEEN 0 AND 100 AND overall_rating BETWEEN 0 AND 100");
                });

            migrationBuilder.CreateTable(
                name: "goods_receipt_lines",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    goods_receipt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_order_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    rejection_reason = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    stock_posting = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    stock_ledger_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    stock_posting_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    accepted_quantity_uom = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    accepted_quantity_value = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    accepted_value_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    accepted_value_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    ordered_quantity_uom = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ordered_quantity_value = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    rejected_quantity_uom = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    rejected_quantity_value = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    unit_cost_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    unit_cost_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
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
                    table.PrimaryKey("pk_goods_receipt_lines", x => x.id);
                    table.CheckConstraint("ck_goods_receipt_lines_accepted_positive", "accepted_quantity_value > 0");
                    table.CheckConstraint("ck_goods_receipt_lines_cost_not_negative", "unit_cost_amount >= 0 AND accepted_value_amount >= 0");
                    table.CheckConstraint("ck_goods_receipt_lines_posting_evidence", "(stock_posting <> 'Posted' OR stock_ledger_entry_id IS NOT NULL) AND (stock_posting <> 'Refused' OR stock_posting_note IS NOT NULL)");
                    table.CheckConstraint("ck_goods_receipt_lines_rejected_not_negative", "rejected_quantity_value >= 0");
                    table.CheckConstraint("ck_goods_receipt_lines_rejection_pairs", "(rejected_quantity_value = 0) = (rejection_reason = 'None')");
                    table.ForeignKey(
                        name: "fk_goods_receipt_lines_goods_receipts_goods_receipt_id",
                        column: x => x.goods_receipt_id,
                        principalSchema: "procurement",
                        principalTable: "goods_receipts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "purchase_order_lines",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    tax_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    purchase_requisition_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    gross_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    gross_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    invoiced_quantity_uom = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    invoiced_quantity_value = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    net_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    net_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    quantity_uom = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    quantity_value = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    received_quantity_uom = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    received_quantity_value = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    rejected_quantity_uom = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    rejected_quantity_value = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    tax_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    tax_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    unit_cost_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    unit_cost_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
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
                    table.PrimaryKey("pk_purchase_order_lines", x => x.id);
                    table.CheckConstraint("ck_purchase_order_lines_balances", "net_amount + tax_amount = gross_amount");
                    table.CheckConstraint("ck_purchase_order_lines_costs_not_negative", "unit_cost_amount >= 0 AND net_amount >= 0 AND tax_amount >= 0 AND gross_amount >= 0");
                    table.CheckConstraint("ck_purchase_order_lines_exactly_one_sku", "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int) = 1");
                    table.CheckConstraint("ck_purchase_order_lines_quantity_positive", "quantity_value > 0");
                    table.CheckConstraint("ck_purchase_order_lines_running_totals_not_negative", "received_quantity_value >= 0 AND rejected_quantity_value >= 0 AND invoiced_quantity_value >= 0");
                    table.ForeignKey(
                        name: "fk_purchase_order_lines_purchase_orders_purchase_order_id",
                        column: x => x.purchase_order_id,
                        principalSchema: "procurement",
                        principalTable: "purchase_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "purchase_requisition_lines",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_requisition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    estimated_unit_cost_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    estimated_unit_cost_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: true),
                    sourced_to_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sourced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    quantity_uom = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    quantity_value = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
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
                    table.PrimaryKey("pk_purchase_requisition_lines", x => x.id);
                    table.CheckConstraint("ck_purchase_requisition_lines_estimate_currency_pairs", "(estimated_unit_cost_amount IS NULL) = (estimated_unit_cost_currency IS NULL)");
                    table.CheckConstraint("ck_purchase_requisition_lines_estimate_not_negative", "estimated_unit_cost_amount IS NULL OR estimated_unit_cost_amount >= 0");
                    table.CheckConstraint("ck_purchase_requisition_lines_exactly_one_sku", "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int) = 1");
                    table.CheckConstraint("ck_purchase_requisition_lines_quantity_positive", "quantity_value > 0");
                    table.ForeignKey(
                        name: "fk_purchase_requisition_lines_purchase_requisitions_purchase_r",
                        column: x => x.purchase_requisition_id,
                        principalSchema: "procurement",
                        principalTable: "purchase_requisitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "rfq_lines",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rfq_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    specification = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    purchase_requisition_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    quantity_uom = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    quantity_value = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
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
                    table.PrimaryKey("pk_rfq_lines", x => x.id);
                    table.CheckConstraint("ck_rfq_lines_exactly_one_sku", "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int) = 1");
                    table.CheckConstraint("ck_rfq_lines_quantity_positive", "quantity_value > 0");
                    table.ForeignKey(
                        name: "fk_rfq_lines_rfqs_rfq_id",
                        column: x => x.rfq_id,
                        principalSchema: "procurement",
                        principalTable: "rfqs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "rfq_responses",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rfq_id = table.Column<Guid>(type: "uuid", nullable: false),
                    partner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    quoted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lead_time_days = table.Column<int>(type: "integer", nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    awarded_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    total_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    total_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
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
                    table.PrimaryKey("pk_rfq_responses", x => x.id);
                    table.CheckConstraint("ck_rfq_responses_awarded_has_user", "status <> 'Awarded' OR awarded_by_user_id IS NOT NULL");
                    table.CheckConstraint("ck_rfq_responses_lead_time_not_negative", "lead_time_days >= 0");
                    table.CheckConstraint("ck_rfq_responses_total_not_negative", "total_amount >= 0");
                    table.ForeignKey(
                        name: "fk_rfq_responses_rfqs_rfq_id",
                        column: x => x.rfq_id,
                        principalSchema: "procurement",
                        principalTable: "rfqs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supplier_invoice_match_lines",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_invoice_match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_order_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    variances = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    invoiced_quantity_uom = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    invoiced_quantity_value = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    invoiced_unit_cost_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    invoiced_unit_cost_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    invoiced_value_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    invoiced_value_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    ordered_quantity_uom = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ordered_quantity_value = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    ordered_unit_cost_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    ordered_unit_cost_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    ordered_value_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    ordered_value_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    previously_invoiced_quantity_uom = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    previously_invoiced_quantity_value = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    price_variance_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    price_variance_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    quantity_variance_uom = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    quantity_variance_value = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    received_quantity_uom = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    received_quantity_value = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
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
                    table.PrimaryKey("pk_supplier_invoice_match_lines", x => x.id);
                    table.CheckConstraint("ck_supplier_invoice_match_lines_costs_not_negative", "ordered_unit_cost_amount >= 0 AND invoiced_unit_cost_amount >= 0");
                    table.CheckConstraint("ck_supplier_invoice_match_lines_invoiced_positive", "invoiced_quantity_value > 0");
                    table.CheckConstraint("ck_supplier_invoice_match_lines_price_variance", "price_variance_amount = invoiced_value_amount - ordered_value_amount");
                    table.CheckConstraint("ck_supplier_invoice_match_lines_unknown_blocks", "purchase_order_line_id IS NOT NULL OR status = 'Blocked'");
                    table.ForeignKey(
                        name: "fk_supplier_invoice_match_lines_supplier_invoice_matches_suppl",
                        column: x => x.supplier_invoice_match_id,
                        principalSchema: "procurement",
                        principalTable: "supplier_invoice_matches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "rfq_response_lines",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rfq_response_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rfq_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    extended_cost_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    extended_cost_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    quoted_quantity_uom = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    quoted_quantity_value = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    requested_quantity_uom = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    requested_quantity_value = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    unit_cost_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    unit_cost_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
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
                    table.PrimaryKey("pk_rfq_response_lines", x => x.id);
                    table.CheckConstraint("ck_rfq_response_lines_cost_not_negative", "unit_cost_amount >= 0");
                    table.CheckConstraint("ck_rfq_response_lines_quoted_positive", "quoted_quantity_value > 0");
                    table.ForeignKey(
                        name: "fk_rfq_response_lines_rfq_responses_rfq_response_id",
                        column: x => x.rfq_response_id,
                        principalSchema: "procurement",
                        principalTable: "rfq_responses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipt_lines_purchase_order_line_id",
                schema: "procurement",
                table: "goods_receipt_lines",
                column: "purchase_order_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipt_lines_stock_posting_refused",
                schema: "procurement",
                table: "goods_receipt_lines",
                column: "stock_posting",
                filter: "stock_posting = 'Refused' AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipt_lines_sync_state",
                schema: "procurement",
                table: "goods_receipt_lines",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipt_lines_tenant_id",
                schema: "procurement",
                table: "goods_receipt_lines",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipt_lines_tenant_id_store_id",
                schema: "procurement",
                table: "goods_receipt_lines",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ux_goods_receipt_lines_receipt_id_order_line_id",
                schema: "procurement",
                table: "goods_receipt_lines",
                columns: new[] { "goods_receipt_id", "purchase_order_line_id" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipts_purchase_order_id",
                schema: "procurement",
                table: "goods_receipts",
                column: "purchase_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipts_status_received_at",
                schema: "procurement",
                table: "goods_receipts",
                columns: new[] { "status", "received_at" });

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipts_sync_state",
                schema: "procurement",
                table: "goods_receipts",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipts_tenant_id",
                schema: "procurement",
                table: "goods_receipts",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipts_tenant_id_store_id",
                schema: "procurement",
                table: "goods_receipts",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ux_goods_receipts_tenant_id_number",
                schema: "procurement",
                table: "goods_receipts",
                columns: new[] { "tenant_id", "receipt_number" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_lines_item_id",
                schema: "procurement",
                table: "purchase_order_lines",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_lines_purchase_order_id",
                schema: "procurement",
                table: "purchase_order_lines",
                column: "purchase_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_lines_requisition_line_id",
                schema: "procurement",
                table: "purchase_order_lines",
                column: "purchase_requisition_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_lines_sync_state",
                schema: "procurement",
                table: "purchase_order_lines",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_lines_tenant_id",
                schema: "procurement",
                table: "purchase_order_lines",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_lines_tenant_id_store_id",
                schema: "procurement",
                table: "purchase_order_lines",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_orders_amends_purchase_order_id",
                schema: "procurement",
                table: "purchase_orders",
                column: "amends_purchase_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_orders_partner_id_expected_at",
                schema: "procurement",
                table: "purchase_orders",
                columns: new[] { "partner_id", "expected_at" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_orders_status_expected_at",
                schema: "procurement",
                table: "purchase_orders",
                columns: new[] { "status", "expected_at" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_orders_sync_state",
                schema: "procurement",
                table: "purchase_orders",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_orders_tenant_id",
                schema: "procurement",
                table: "purchase_orders",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_orders_tenant_id_store_id",
                schema: "procurement",
                table: "purchase_orders",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ux_purchase_orders_tenant_id_number",
                schema: "procurement",
                table: "purchase_orders",
                columns: new[] { "tenant_id", "order_number" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_requisition_lines_item_id",
                schema: "procurement",
                table: "purchase_requisition_lines",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_requisition_lines_requisition_id",
                schema: "procurement",
                table: "purchase_requisition_lines",
                column: "purchase_requisition_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_requisition_lines_sync_state",
                schema: "procurement",
                table: "purchase_requisition_lines",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_requisition_lines_tenant_id",
                schema: "procurement",
                table: "purchase_requisition_lines",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_requisition_lines_tenant_id_store_id",
                schema: "procurement",
                table: "purchase_requisition_lines",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_requisitions_status_required_by",
                schema: "procurement",
                table: "purchase_requisitions",
                columns: new[] { "status", "required_by" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_requisitions_sync_state",
                schema: "procurement",
                table: "purchase_requisitions",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_requisitions_tenant_id",
                schema: "procurement",
                table: "purchase_requisitions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_requisitions_tenant_id_store_id",
                schema: "procurement",
                table: "purchase_requisitions",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ux_purchase_requisitions_tenant_id_number",
                schema: "procurement",
                table: "purchase_requisitions",
                columns: new[] { "tenant_id", "requisition_number" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_rfq_lines_item_id",
                schema: "procurement",
                table: "rfq_lines",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_rfq_lines_rfq_id",
                schema: "procurement",
                table: "rfq_lines",
                column: "rfq_id");

            migrationBuilder.CreateIndex(
                name: "ix_rfq_lines_sync_state",
                schema: "procurement",
                table: "rfq_lines",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_rfq_lines_tenant_id",
                schema: "procurement",
                table: "rfq_lines",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_rfq_lines_tenant_id_store_id",
                schema: "procurement",
                table: "rfq_lines",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ix_rfq_response_lines_sync_state",
                schema: "procurement",
                table: "rfq_response_lines",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_rfq_response_lines_tenant_id",
                schema: "procurement",
                table: "rfq_response_lines",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_rfq_response_lines_tenant_id_store_id",
                schema: "procurement",
                table: "rfq_response_lines",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ux_rfq_response_lines_response_id_rfq_line_id",
                schema: "procurement",
                table: "rfq_response_lines",
                columns: new[] { "rfq_response_id", "rfq_line_id" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_rfq_responses_partner_id",
                schema: "procurement",
                table: "rfq_responses",
                column: "partner_id");

            migrationBuilder.CreateIndex(
                name: "ix_rfq_responses_sync_state",
                schema: "procurement",
                table: "rfq_responses",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_rfq_responses_tenant_id",
                schema: "procurement",
                table: "rfq_responses",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_rfq_responses_tenant_id_store_id",
                schema: "procurement",
                table: "rfq_responses",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ux_rfq_responses_rfq_id_partner_id",
                schema: "procurement",
                table: "rfq_responses",
                columns: new[] { "rfq_id", "partner_id" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_rfqs_status_closes_at",
                schema: "procurement",
                table: "rfqs",
                columns: new[] { "status", "closes_at" });

            migrationBuilder.CreateIndex(
                name: "ix_rfqs_sync_state",
                schema: "procurement",
                table: "rfqs",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_rfqs_tenant_id",
                schema: "procurement",
                table: "rfqs",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_rfqs_tenant_id_store_id",
                schema: "procurement",
                table: "rfqs",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ux_rfqs_tenant_id_number",
                schema: "procurement",
                table: "rfqs",
                columns: new[] { "tenant_id", "rfq_number" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_supplier_invoice_match_lines_match_id",
                schema: "procurement",
                table: "supplier_invoice_match_lines",
                column: "supplier_invoice_match_id");

            migrationBuilder.CreateIndex(
                name: "ix_supplier_invoice_match_lines_purchase_order_line_id",
                schema: "procurement",
                table: "supplier_invoice_match_lines",
                column: "purchase_order_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_supplier_invoice_match_lines_sync_state",
                schema: "procurement",
                table: "supplier_invoice_match_lines",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_supplier_invoice_match_lines_tenant_id",
                schema: "procurement",
                table: "supplier_invoice_match_lines",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_supplier_invoice_match_lines_tenant_id_store_id",
                schema: "procurement",
                table: "supplier_invoice_match_lines",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ix_supplier_invoice_matches_partner_id",
                schema: "procurement",
                table: "supplier_invoice_matches",
                column: "partner_id");

            migrationBuilder.CreateIndex(
                name: "ix_supplier_invoice_matches_status_matched_at",
                schema: "procurement",
                table: "supplier_invoice_matches",
                columns: new[] { "status", "matched_at" });

            migrationBuilder.CreateIndex(
                name: "ix_supplier_invoice_matches_sync_state",
                schema: "procurement",
                table: "supplier_invoice_matches",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_supplier_invoice_matches_tenant_id",
                schema: "procurement",
                table: "supplier_invoice_matches",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_supplier_invoice_matches_tenant_id_store_id",
                schema: "procurement",
                table: "supplier_invoice_matches",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ux_supplier_invoice_matches_order_id_invoice_number",
                schema: "procurement",
                table: "supplier_invoice_matches",
                columns: new[] { "purchase_order_id", "supplier_invoice_number" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_supplier_scorecards_period",
                schema: "procurement",
                table: "supplier_scorecards",
                columns: new[] { "period_start", "period_end" });

            migrationBuilder.CreateIndex(
                name: "ix_supplier_scorecards_sync_state",
                schema: "procurement",
                table: "supplier_scorecards",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_supplier_scorecards_tenant_id",
                schema: "procurement",
                table: "supplier_scorecards",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_supplier_scorecards_tenant_id_store_id",
                schema: "procurement",
                table: "supplier_scorecards",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ux_supplier_scorecards_partner_period",
                schema: "procurement",
                table: "supplier_scorecards",
                columns: new[] { "partner_id", "period_start", "period_end" },
                unique: true,
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "goods_receipt_lines",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "purchase_order_lines",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "purchase_requisition_lines",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "rfq_lines",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "rfq_response_lines",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "supplier_invoice_match_lines",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "supplier_scorecards",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "goods_receipts",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "purchase_orders",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "purchase_requisitions",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "rfq_responses",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "supplier_invoice_matches",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "rfqs",
                schema: "procurement");
        }
    }
}
