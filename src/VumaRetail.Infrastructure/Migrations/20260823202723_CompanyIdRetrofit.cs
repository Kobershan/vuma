using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CompanyIdRetrofit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "warehouse",
                table: "zones",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "identity",
                table: "users",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "identity",
                table: "user_role_assignments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "catalog",
                table: "units_of_measure",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "pos",
                table: "till_sessions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "identity",
                table: "terminals",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "platform",
                table: "tenants",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "finance",
                table: "tax_rules",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "licensing",
                table: "tamper_flags",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "sync",
                table: "sync_cursors",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "licensing",
                table: "support_grants",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "procurement",
                table: "supplier_scorecards",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "procurement",
                table: "supplier_invoice_matches",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "procurement",
                table: "supplier_invoice_match_lines",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "platform",
                table: "stores",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "inventory",
                table: "stocktake_sessions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "inventory",
                table: "stocktake_lines",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "inventory",
                table: "stock_transfers",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "inventory",
                table: "stock_locations",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "inventory",
                table: "stock_ledger_entries",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "inventory",
                table: "stock_balances",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "backup",
                table: "snapshots",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "warehouse",
                table: "shipment_confirmations",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "sales",
                table: "sales_returns",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "sales",
                table: "sales_return_lines",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "pos",
                table: "sales",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "pos",
                table: "sale_tenders",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "pos",
                table: "sale_lines",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "identity",
                table: "roles",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "identity",
                table: "role_permissions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "procurement",
                table: "rfqs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "procurement",
                table: "rfq_responses",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "procurement",
                table: "rfq_response_lines",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "procurement",
                table: "rfq_lines",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "identity",
                table: "refresh_tokens",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "finance",
                table: "reconciliation_variance_flags",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "pos",
                table: "receipt_prints",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "warehouse",
                table: "putaway_tasks",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "procurement",
                table: "purchase_requisitions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "procurement",
                table: "purchase_requisition_lines",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "procurement",
                table: "purchase_orders",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "procurement",
                table: "purchase_order_lines",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "sales",
                table: "promotions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "sales",
                table: "promotion_lines",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "sales",
                table: "price_override_logs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "sales",
                table: "price_lists",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "sales",
                table: "price_list_lines",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "finance",
                table: "posting_rules",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "finance",
                table: "posting_rule_lines",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "warehouse",
                table: "pick_waves",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "warehouse",
                table: "pick_tasks",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "partners",
                table: "partners",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "warehouse",
                table: "pack_tasks",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "sync",
                table: "outbox_messages",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "licensing",
                table: "metering_records",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "licensing",
                table: "licences",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "licensing",
                table: "leases",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "finance",
                table: "journals",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "finance",
                table: "journal_lines",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "catalog",
                table: "items",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "catalog",
                table: "item_variants",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "sync",
                table: "inbox_messages",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "imports",
                table: "import_rows",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "imports",
                table: "import_mapping_templates",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "imports",
                table: "import_column_mappings",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "imports",
                table: "import_batches",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "procurement",
                table: "goods_receipts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "procurement",
                table: "goods_receipt_lines",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "licensing",
                table: "emergency_unlocks",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "finance",
                table: "document_number_counters",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "warehouse",
                table: "cycle_counts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "warehouse",
                table: "cycle_count_lines",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "sync",
                table: "conflict_entries",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "licensing",
                table: "clock_watermarks",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "warehouse",
                table: "bins",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "warehouse",
                table: "bin_stock_movements",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "warehouse",
                table: "bin_stock",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "catalog",
                table: "barcodes",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "finance",
                table: "bank_statement_lines",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "finance",
                table: "bank_accounts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "platform",
                table: "audit_entries",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "finance",
                table: "ar_receipts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "finance",
                table: "ar_receipt_allocations",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "finance",
                table: "ar_invoices",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "finance",
                table: "ar_invoice_lines",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "finance",
                table: "ap_payments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "finance",
                table: "ap_payment_allocations",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "finance",
                table: "ap_invoices",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "finance",
                table: "ap_invoice_lines",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "licensing",
                table: "activations",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "finance",
                table: "accounts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "finance",
                table: "accounting_periods",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "ix_zones_company_id",
                schema: "warehouse",
                table: "zones",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_company_id",
                schema: "identity",
                table: "users",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_role_assignments_company_id",
                schema: "identity",
                table: "user_role_assignments",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_units_of_measure_company_id",
                schema: "catalog",
                table: "units_of_measure",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_till_sessions_company_id",
                schema: "pos",
                table: "till_sessions",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_terminals_company_id",
                schema: "identity",
                table: "terminals",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenants_company_id",
                schema: "platform",
                table: "tenants",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_tax_rules_company_id",
                schema: "finance",
                table: "tax_rules",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_tamper_flags_company_id",
                schema: "licensing",
                table: "tamper_flags",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_sync_cursors_company_id",
                schema: "sync",
                table: "sync_cursors",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_support_grants_company_id",
                schema: "licensing",
                table: "support_grants",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_supplier_scorecards_company_id",
                schema: "procurement",
                table: "supplier_scorecards",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_supplier_invoice_matches_company_id",
                schema: "procurement",
                table: "supplier_invoice_matches",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_supplier_invoice_match_lines_company_id",
                schema: "procurement",
                table: "supplier_invoice_match_lines",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_stores_company_id",
                schema: "platform",
                table: "stores",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_stocktake_sessions_company_id",
                schema: "inventory",
                table: "stocktake_sessions",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_stocktake_lines_company_id",
                schema: "inventory",
                table: "stocktake_lines",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfers_company_id",
                schema: "inventory",
                table: "stock_transfers",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_locations_company_id",
                schema: "inventory",
                table: "stock_locations",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_ledger_entries_company_id",
                schema: "inventory",
                table: "stock_ledger_entries",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_balances_company_id",
                schema: "inventory",
                table: "stock_balances",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_snapshots_company_id",
                schema: "backup",
                table: "snapshots",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_shipment_confirmations_company_id",
                schema: "warehouse",
                table: "shipment_confirmations",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_returns_company_id",
                schema: "sales",
                table: "sales_returns",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_return_lines_company_id",
                schema: "sales",
                table: "sales_return_lines",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_company_id",
                schema: "pos",
                table: "sales",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_sale_tenders_company_id",
                schema: "pos",
                table: "sale_tenders",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_sale_lines_company_id",
                schema: "pos",
                table: "sale_lines",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_roles_company_id",
                schema: "identity",
                table: "roles",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_role_permissions_company_id",
                schema: "identity",
                table: "role_permissions",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_rfqs_company_id",
                schema: "procurement",
                table: "rfqs",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_rfq_responses_company_id",
                schema: "procurement",
                table: "rfq_responses",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_rfq_response_lines_company_id",
                schema: "procurement",
                table: "rfq_response_lines",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_rfq_lines_company_id",
                schema: "procurement",
                table: "rfq_lines",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_company_id",
                schema: "identity",
                table: "refresh_tokens",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_reconciliation_variance_flags_company_id",
                schema: "finance",
                table: "reconciliation_variance_flags",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_receipt_prints_company_id",
                schema: "pos",
                table: "receipt_prints",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_putaway_tasks_company_id",
                schema: "warehouse",
                table: "putaway_tasks",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_requisitions_company_id",
                schema: "procurement",
                table: "purchase_requisitions",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_requisition_lines_company_id",
                schema: "procurement",
                table: "purchase_requisition_lines",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_orders_company_id",
                schema: "procurement",
                table: "purchase_orders",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_lines_company_id",
                schema: "procurement",
                table: "purchase_order_lines",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_promotions_company_id",
                schema: "sales",
                table: "promotions",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_promotion_lines_company_id",
                schema: "sales",
                table: "promotion_lines",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_price_override_logs_company_id",
                schema: "sales",
                table: "price_override_logs",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_price_lists_company_id",
                schema: "sales",
                table: "price_lists",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_price_list_lines_company_id",
                schema: "sales",
                table: "price_list_lines",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_posting_rules_company_id",
                schema: "finance",
                table: "posting_rules",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_posting_rule_lines_company_id",
                schema: "finance",
                table: "posting_rule_lines",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_pick_waves_company_id",
                schema: "warehouse",
                table: "pick_waves",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_pick_tasks_company_id",
                schema: "warehouse",
                table: "pick_tasks",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_partners_company_id",
                schema: "partners",
                table: "partners",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_pack_tasks_company_id",
                schema: "warehouse",
                table: "pack_tasks",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_company_id",
                schema: "sync",
                table: "outbox_messages",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_metering_records_company_id",
                schema: "licensing",
                table: "metering_records",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_licences_company_id",
                schema: "licensing",
                table: "licences",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_leases_company_id",
                schema: "licensing",
                table: "leases",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_journals_company_id",
                schema: "finance",
                table: "journals",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_journal_lines_company_id",
                schema: "finance",
                table: "journal_lines",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_items_company_id",
                schema: "catalog",
                table: "items",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_item_variants_company_id",
                schema: "catalog",
                table: "item_variants",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_inbox_messages_company_id",
                schema: "sync",
                table: "inbox_messages",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_import_rows_company_id",
                schema: "imports",
                table: "import_rows",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_import_mapping_templates_company_id",
                schema: "imports",
                table: "import_mapping_templates",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_import_column_mappings_company_id",
                schema: "imports",
                table: "import_column_mappings",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_import_batches_company_id",
                schema: "imports",
                table: "import_batches",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipts_company_id",
                schema: "procurement",
                table: "goods_receipts",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipt_lines_company_id",
                schema: "procurement",
                table: "goods_receipt_lines",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_emergency_unlocks_company_id",
                schema: "licensing",
                table: "emergency_unlocks",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_document_number_counters_company_id",
                schema: "finance",
                table: "document_number_counters",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_cycle_counts_company_id",
                schema: "warehouse",
                table: "cycle_counts",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_cycle_count_lines_company_id",
                schema: "warehouse",
                table: "cycle_count_lines",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_conflict_entries_company_id",
                schema: "sync",
                table: "conflict_entries",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_clock_watermarks_company_id",
                schema: "licensing",
                table: "clock_watermarks",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_bins_company_id",
                schema: "warehouse",
                table: "bins",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_bin_stock_movements_company_id",
                schema: "warehouse",
                table: "bin_stock_movements",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_bin_stock_company_id",
                schema: "warehouse",
                table: "bin_stock",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_barcodes_company_id",
                schema: "catalog",
                table: "barcodes",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_bank_statement_lines_company_id",
                schema: "finance",
                table: "bank_statement_lines",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_bank_accounts_company_id",
                schema: "finance",
                table: "bank_accounts",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_company_id",
                schema: "platform",
                table: "audit_entries",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_ar_receipts_company_id",
                schema: "finance",
                table: "ar_receipts",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_ar_receipt_allocations_company_id",
                schema: "finance",
                table: "ar_receipt_allocations",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_ar_invoices_company_id",
                schema: "finance",
                table: "ar_invoices",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_ar_invoice_lines_company_id",
                schema: "finance",
                table: "ar_invoice_lines",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_ap_payments_company_id",
                schema: "finance",
                table: "ap_payments",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_ap_payment_allocations_company_id",
                schema: "finance",
                table: "ap_payment_allocations",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_ap_invoices_company_id",
                schema: "finance",
                table: "ap_invoices",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_ap_invoice_lines_company_id",
                schema: "finance",
                table: "ap_invoice_lines",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_activations_company_id",
                schema: "licensing",
                table: "activations",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_accounts_company_id",
                schema: "finance",
                table: "accounts",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_accounting_periods_company_id",
                schema: "finance",
                table: "accounting_periods",
                column: "company_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_zones_company_id",
                schema: "warehouse",
                table: "zones");

            migrationBuilder.DropIndex(
                name: "ix_users_company_id",
                schema: "identity",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_user_role_assignments_company_id",
                schema: "identity",
                table: "user_role_assignments");

            migrationBuilder.DropIndex(
                name: "ix_units_of_measure_company_id",
                schema: "catalog",
                table: "units_of_measure");

            migrationBuilder.DropIndex(
                name: "ix_till_sessions_company_id",
                schema: "pos",
                table: "till_sessions");

            migrationBuilder.DropIndex(
                name: "ix_terminals_company_id",
                schema: "identity",
                table: "terminals");

            migrationBuilder.DropIndex(
                name: "ix_tenants_company_id",
                schema: "platform",
                table: "tenants");

            migrationBuilder.DropIndex(
                name: "ix_tax_rules_company_id",
                schema: "finance",
                table: "tax_rules");

            migrationBuilder.DropIndex(
                name: "ix_tamper_flags_company_id",
                schema: "licensing",
                table: "tamper_flags");

            migrationBuilder.DropIndex(
                name: "ix_sync_cursors_company_id",
                schema: "sync",
                table: "sync_cursors");

            migrationBuilder.DropIndex(
                name: "ix_support_grants_company_id",
                schema: "licensing",
                table: "support_grants");

            migrationBuilder.DropIndex(
                name: "ix_supplier_scorecards_company_id",
                schema: "procurement",
                table: "supplier_scorecards");

            migrationBuilder.DropIndex(
                name: "ix_supplier_invoice_matches_company_id",
                schema: "procurement",
                table: "supplier_invoice_matches");

            migrationBuilder.DropIndex(
                name: "ix_supplier_invoice_match_lines_company_id",
                schema: "procurement",
                table: "supplier_invoice_match_lines");

            migrationBuilder.DropIndex(
                name: "ix_stores_company_id",
                schema: "platform",
                table: "stores");

            migrationBuilder.DropIndex(
                name: "ix_stocktake_sessions_company_id",
                schema: "inventory",
                table: "stocktake_sessions");

            migrationBuilder.DropIndex(
                name: "ix_stocktake_lines_company_id",
                schema: "inventory",
                table: "stocktake_lines");

            migrationBuilder.DropIndex(
                name: "ix_stock_transfers_company_id",
                schema: "inventory",
                table: "stock_transfers");

            migrationBuilder.DropIndex(
                name: "ix_stock_locations_company_id",
                schema: "inventory",
                table: "stock_locations");

            migrationBuilder.DropIndex(
                name: "ix_stock_ledger_entries_company_id",
                schema: "inventory",
                table: "stock_ledger_entries");

            migrationBuilder.DropIndex(
                name: "ix_stock_balances_company_id",
                schema: "inventory",
                table: "stock_balances");

            migrationBuilder.DropIndex(
                name: "ix_snapshots_company_id",
                schema: "backup",
                table: "snapshots");

            migrationBuilder.DropIndex(
                name: "ix_shipment_confirmations_company_id",
                schema: "warehouse",
                table: "shipment_confirmations");

            migrationBuilder.DropIndex(
                name: "ix_sales_returns_company_id",
                schema: "sales",
                table: "sales_returns");

            migrationBuilder.DropIndex(
                name: "ix_sales_return_lines_company_id",
                schema: "sales",
                table: "sales_return_lines");

            migrationBuilder.DropIndex(
                name: "ix_sales_company_id",
                schema: "pos",
                table: "sales");

            migrationBuilder.DropIndex(
                name: "ix_sale_tenders_company_id",
                schema: "pos",
                table: "sale_tenders");

            migrationBuilder.DropIndex(
                name: "ix_sale_lines_company_id",
                schema: "pos",
                table: "sale_lines");

            migrationBuilder.DropIndex(
                name: "ix_roles_company_id",
                schema: "identity",
                table: "roles");

            migrationBuilder.DropIndex(
                name: "ix_role_permissions_company_id",
                schema: "identity",
                table: "role_permissions");

            migrationBuilder.DropIndex(
                name: "ix_rfqs_company_id",
                schema: "procurement",
                table: "rfqs");

            migrationBuilder.DropIndex(
                name: "ix_rfq_responses_company_id",
                schema: "procurement",
                table: "rfq_responses");

            migrationBuilder.DropIndex(
                name: "ix_rfq_response_lines_company_id",
                schema: "procurement",
                table: "rfq_response_lines");

            migrationBuilder.DropIndex(
                name: "ix_rfq_lines_company_id",
                schema: "procurement",
                table: "rfq_lines");

            migrationBuilder.DropIndex(
                name: "ix_refresh_tokens_company_id",
                schema: "identity",
                table: "refresh_tokens");

            migrationBuilder.DropIndex(
                name: "ix_reconciliation_variance_flags_company_id",
                schema: "finance",
                table: "reconciliation_variance_flags");

            migrationBuilder.DropIndex(
                name: "ix_receipt_prints_company_id",
                schema: "pos",
                table: "receipt_prints");

            migrationBuilder.DropIndex(
                name: "ix_putaway_tasks_company_id",
                schema: "warehouse",
                table: "putaway_tasks");

            migrationBuilder.DropIndex(
                name: "ix_purchase_requisitions_company_id",
                schema: "procurement",
                table: "purchase_requisitions");

            migrationBuilder.DropIndex(
                name: "ix_purchase_requisition_lines_company_id",
                schema: "procurement",
                table: "purchase_requisition_lines");

            migrationBuilder.DropIndex(
                name: "ix_purchase_orders_company_id",
                schema: "procurement",
                table: "purchase_orders");

            migrationBuilder.DropIndex(
                name: "ix_purchase_order_lines_company_id",
                schema: "procurement",
                table: "purchase_order_lines");

            migrationBuilder.DropIndex(
                name: "ix_promotions_company_id",
                schema: "sales",
                table: "promotions");

            migrationBuilder.DropIndex(
                name: "ix_promotion_lines_company_id",
                schema: "sales",
                table: "promotion_lines");

            migrationBuilder.DropIndex(
                name: "ix_price_override_logs_company_id",
                schema: "sales",
                table: "price_override_logs");

            migrationBuilder.DropIndex(
                name: "ix_price_lists_company_id",
                schema: "sales",
                table: "price_lists");

            migrationBuilder.DropIndex(
                name: "ix_price_list_lines_company_id",
                schema: "sales",
                table: "price_list_lines");

            migrationBuilder.DropIndex(
                name: "ix_posting_rules_company_id",
                schema: "finance",
                table: "posting_rules");

            migrationBuilder.DropIndex(
                name: "ix_posting_rule_lines_company_id",
                schema: "finance",
                table: "posting_rule_lines");

            migrationBuilder.DropIndex(
                name: "ix_pick_waves_company_id",
                schema: "warehouse",
                table: "pick_waves");

            migrationBuilder.DropIndex(
                name: "ix_pick_tasks_company_id",
                schema: "warehouse",
                table: "pick_tasks");

            migrationBuilder.DropIndex(
                name: "ix_partners_company_id",
                schema: "partners",
                table: "partners");

            migrationBuilder.DropIndex(
                name: "ix_pack_tasks_company_id",
                schema: "warehouse",
                table: "pack_tasks");

            migrationBuilder.DropIndex(
                name: "ix_outbox_messages_company_id",
                schema: "sync",
                table: "outbox_messages");

            migrationBuilder.DropIndex(
                name: "ix_metering_records_company_id",
                schema: "licensing",
                table: "metering_records");

            migrationBuilder.DropIndex(
                name: "ix_licences_company_id",
                schema: "licensing",
                table: "licences");

            migrationBuilder.DropIndex(
                name: "ix_leases_company_id",
                schema: "licensing",
                table: "leases");

            migrationBuilder.DropIndex(
                name: "ix_journals_company_id",
                schema: "finance",
                table: "journals");

            migrationBuilder.DropIndex(
                name: "ix_journal_lines_company_id",
                schema: "finance",
                table: "journal_lines");

            migrationBuilder.DropIndex(
                name: "ix_items_company_id",
                schema: "catalog",
                table: "items");

            migrationBuilder.DropIndex(
                name: "ix_item_variants_company_id",
                schema: "catalog",
                table: "item_variants");

            migrationBuilder.DropIndex(
                name: "ix_inbox_messages_company_id",
                schema: "sync",
                table: "inbox_messages");

            migrationBuilder.DropIndex(
                name: "ix_import_rows_company_id",
                schema: "imports",
                table: "import_rows");

            migrationBuilder.DropIndex(
                name: "ix_import_mapping_templates_company_id",
                schema: "imports",
                table: "import_mapping_templates");

            migrationBuilder.DropIndex(
                name: "ix_import_column_mappings_company_id",
                schema: "imports",
                table: "import_column_mappings");

            migrationBuilder.DropIndex(
                name: "ix_import_batches_company_id",
                schema: "imports",
                table: "import_batches");

            migrationBuilder.DropIndex(
                name: "ix_goods_receipts_company_id",
                schema: "procurement",
                table: "goods_receipts");

            migrationBuilder.DropIndex(
                name: "ix_goods_receipt_lines_company_id",
                schema: "procurement",
                table: "goods_receipt_lines");

            migrationBuilder.DropIndex(
                name: "ix_emergency_unlocks_company_id",
                schema: "licensing",
                table: "emergency_unlocks");

            migrationBuilder.DropIndex(
                name: "ix_document_number_counters_company_id",
                schema: "finance",
                table: "document_number_counters");

            migrationBuilder.DropIndex(
                name: "ix_cycle_counts_company_id",
                schema: "warehouse",
                table: "cycle_counts");

            migrationBuilder.DropIndex(
                name: "ix_cycle_count_lines_company_id",
                schema: "warehouse",
                table: "cycle_count_lines");

            migrationBuilder.DropIndex(
                name: "ix_conflict_entries_company_id",
                schema: "sync",
                table: "conflict_entries");

            migrationBuilder.DropIndex(
                name: "ix_clock_watermarks_company_id",
                schema: "licensing",
                table: "clock_watermarks");

            migrationBuilder.DropIndex(
                name: "ix_bins_company_id",
                schema: "warehouse",
                table: "bins");

            migrationBuilder.DropIndex(
                name: "ix_bin_stock_movements_company_id",
                schema: "warehouse",
                table: "bin_stock_movements");

            migrationBuilder.DropIndex(
                name: "ix_bin_stock_company_id",
                schema: "warehouse",
                table: "bin_stock");

            migrationBuilder.DropIndex(
                name: "ix_barcodes_company_id",
                schema: "catalog",
                table: "barcodes");

            migrationBuilder.DropIndex(
                name: "ix_bank_statement_lines_company_id",
                schema: "finance",
                table: "bank_statement_lines");

            migrationBuilder.DropIndex(
                name: "ix_bank_accounts_company_id",
                schema: "finance",
                table: "bank_accounts");

            migrationBuilder.DropIndex(
                name: "ix_audit_entries_company_id",
                schema: "platform",
                table: "audit_entries");

            migrationBuilder.DropIndex(
                name: "ix_ar_receipts_company_id",
                schema: "finance",
                table: "ar_receipts");

            migrationBuilder.DropIndex(
                name: "ix_ar_receipt_allocations_company_id",
                schema: "finance",
                table: "ar_receipt_allocations");

            migrationBuilder.DropIndex(
                name: "ix_ar_invoices_company_id",
                schema: "finance",
                table: "ar_invoices");

            migrationBuilder.DropIndex(
                name: "ix_ar_invoice_lines_company_id",
                schema: "finance",
                table: "ar_invoice_lines");

            migrationBuilder.DropIndex(
                name: "ix_ap_payments_company_id",
                schema: "finance",
                table: "ap_payments");

            migrationBuilder.DropIndex(
                name: "ix_ap_payment_allocations_company_id",
                schema: "finance",
                table: "ap_payment_allocations");

            migrationBuilder.DropIndex(
                name: "ix_ap_invoices_company_id",
                schema: "finance",
                table: "ap_invoices");

            migrationBuilder.DropIndex(
                name: "ix_ap_invoice_lines_company_id",
                schema: "finance",
                table: "ap_invoice_lines");

            migrationBuilder.DropIndex(
                name: "ix_activations_company_id",
                schema: "licensing",
                table: "activations");

            migrationBuilder.DropIndex(
                name: "ix_accounts_company_id",
                schema: "finance",
                table: "accounts");

            migrationBuilder.DropIndex(
                name: "ix_accounting_periods_company_id",
                schema: "finance",
                table: "accounting_periods");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "warehouse",
                table: "zones");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "identity",
                table: "user_role_assignments");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "catalog",
                table: "units_of_measure");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "pos",
                table: "till_sessions");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "identity",
                table: "terminals");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "platform",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "finance",
                table: "tax_rules");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "licensing",
                table: "tamper_flags");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "sync",
                table: "sync_cursors");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "licensing",
                table: "support_grants");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "procurement",
                table: "supplier_scorecards");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "procurement",
                table: "supplier_invoice_matches");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "procurement",
                table: "supplier_invoice_match_lines");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "platform",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "inventory",
                table: "stocktake_sessions");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "inventory",
                table: "stocktake_lines");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "inventory",
                table: "stock_transfers");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "inventory",
                table: "stock_locations");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "inventory",
                table: "stock_ledger_entries");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "inventory",
                table: "stock_balances");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "backup",
                table: "snapshots");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "warehouse",
                table: "shipment_confirmations");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "sales",
                table: "sales_returns");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "sales",
                table: "sales_return_lines");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "pos",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "pos",
                table: "sale_tenders");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "pos",
                table: "sale_lines");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "identity",
                table: "roles");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "identity",
                table: "role_permissions");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "procurement",
                table: "rfqs");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "procurement",
                table: "rfq_responses");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "procurement",
                table: "rfq_response_lines");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "procurement",
                table: "rfq_lines");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "identity",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "finance",
                table: "reconciliation_variance_flags");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "pos",
                table: "receipt_prints");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "warehouse",
                table: "putaway_tasks");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "procurement",
                table: "purchase_requisitions");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "procurement",
                table: "purchase_requisition_lines");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "procurement",
                table: "purchase_orders");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "procurement",
                table: "purchase_order_lines");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "sales",
                table: "promotions");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "sales",
                table: "promotion_lines");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "sales",
                table: "price_override_logs");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "sales",
                table: "price_lists");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "sales",
                table: "price_list_lines");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "finance",
                table: "posting_rules");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "finance",
                table: "posting_rule_lines");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "warehouse",
                table: "pick_waves");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "warehouse",
                table: "pick_tasks");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "partners",
                table: "partners");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "warehouse",
                table: "pack_tasks");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "sync",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "licensing",
                table: "metering_records");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "licensing",
                table: "licences");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "licensing",
                table: "leases");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "finance",
                table: "journals");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "finance",
                table: "journal_lines");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "catalog",
                table: "items");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "catalog",
                table: "item_variants");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "sync",
                table: "inbox_messages");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "imports",
                table: "import_rows");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "imports",
                table: "import_mapping_templates");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "imports",
                table: "import_column_mappings");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "imports",
                table: "import_batches");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "procurement",
                table: "goods_receipts");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "procurement",
                table: "goods_receipt_lines");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "licensing",
                table: "emergency_unlocks");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "finance",
                table: "document_number_counters");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "warehouse",
                table: "cycle_counts");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "warehouse",
                table: "cycle_count_lines");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "sync",
                table: "conflict_entries");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "licensing",
                table: "clock_watermarks");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "warehouse",
                table: "bins");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "warehouse",
                table: "bin_stock_movements");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "warehouse",
                table: "bin_stock");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "catalog",
                table: "barcodes");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "finance",
                table: "bank_statement_lines");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "finance",
                table: "bank_accounts");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "platform",
                table: "audit_entries");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "finance",
                table: "ar_receipts");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "finance",
                table: "ar_receipt_allocations");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "finance",
                table: "ar_invoices");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "finance",
                table: "ar_invoice_lines");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "finance",
                table: "ap_payments");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "finance",
                table: "ap_payment_allocations");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "finance",
                table: "ap_invoices");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "finance",
                table: "ap_invoice_lines");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "licensing",
                table: "activations");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "finance",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "finance",
                table: "accounting_periods");
        }
    }
}
