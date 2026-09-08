using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Stage10b_AccountsAndLayBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "customer_accounts");

            migrationBuilder.CreateTable(
                name: "account_holders",
                schema: "customer_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    charge_limit_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    charge_limit_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: true),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("pk_account_holders", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "accounts",
                schema: "customer_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    partner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    terms_days = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    hold_reason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    credit_limit_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    credit_limit_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: true),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("pk_accounts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "layby_agreements",
                schema: "customer_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    agreement_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    partner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    term_months = table.Column<int>(type: "integer", nullable: false),
                    expiry_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancel_refund_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    cancel_refund_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: true),
                    cancel_fee_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    cancel_fee_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: true),
                    admin_fee_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    admin_fee_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    agreed_total_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    agreed_total_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    deposit_required_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    deposit_required_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    paid_to_date_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    paid_to_date_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: true),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("pk_layby_agreements", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "terms",
                schema: "customer_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    interest_monthly_rate = table.Column<decimal>(type: "numeric(9,6)", nullable: false),
                    settlement_discount_rate = table.Column<decimal>(type: "numeric(9,6)", nullable: false),
                    settlement_discount_days = table.Column<int>(type: "integer", nullable: false),
                    lay_by_max_term_months = table.Column<int>(type: "integer", nullable: false),
                    stale_balance_minutes = table.Column<int>(type: "integer", nullable: false),
                    layby_admin_fee_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    layby_admin_fee_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: true),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("pk_terms", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "layby_agreement_lines",
                schema: "customer_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    agreement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    quantity_value = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    quantity_uom = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    pack_size_description = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    price_list_id = table.Column<Guid>(type: "uuid", nullable: true),
                    agreed_unit_price_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    agreed_unit_price_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    discount_amount_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    discount_amount_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    net_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    net_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    tax_amount_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    tax_amount_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: true),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("pk_layby_agreement_lines", x => x.id);
                    table.CheckConstraint("ck_layby_agreement_lines_pack_size_required", "pack_size_description <> ''");
                    table.CheckConstraint("ck_layby_agreement_lines_quantity_positive", "quantity_value > 0");
                    table.ForeignKey(
                        name: "fk_layby_agreement_lines_layby_agreements_agreement_id",
                        column: x => x.agreement_id,
                        principalSchema: "customer_accounts",
                        principalTable: "layby_agreements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "layby_instalments",
                schema: "customer_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    agreement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    receipt_reference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    channel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    taken_offline = table.Column<bool>(type: "boolean", nullable: false),
                    amount_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    amount_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: true),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("pk_layby_instalments", x => x.id);
                    table.ForeignKey(
                        name: "fk_layby_instalments_layby_agreements_agreement_id",
                        column: x => x.agreement_id,
                        principalSchema: "customer_accounts",
                        principalTable: "layby_agreements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_account_holders_account_id",
                schema: "customer_accounts",
                table: "account_holders",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_account_holders_sync_state",
                schema: "customer_accounts",
                table: "account_holders",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_account_holders_tenant_id",
                schema: "customer_accounts",
                table: "account_holders",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_account_holders_tenant_id_company_id",
                schema: "customer_accounts",
                table: "account_holders",
                columns: new[] { "tenant_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_account_holders_tenant_id_store_id",
                schema: "customer_accounts",
                table: "account_holders",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ix_accounts_partner_id",
                schema: "customer_accounts",
                table: "accounts",
                column: "partner_id");

            migrationBuilder.CreateIndex(
                name: "ix_accounts_sync_state",
                schema: "customer_accounts",
                table: "accounts",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_accounts_tenant_id",
                schema: "customer_accounts",
                table: "accounts",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_accounts_tenant_id_company_id",
                schema: "customer_accounts",
                table: "accounts",
                columns: new[] { "tenant_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_accounts_tenant_id_store_id",
                schema: "customer_accounts",
                table: "accounts",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ux_accounts_tenant_id_number",
                schema: "customer_accounts",
                table: "accounts",
                columns: new[] { "tenant_id", "account_number" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_layby_agreement_lines_agreement_id",
                schema: "customer_accounts",
                table: "layby_agreement_lines",
                column: "agreement_id");

            migrationBuilder.CreateIndex(
                name: "ix_layby_agreement_lines_sync_state",
                schema: "customer_accounts",
                table: "layby_agreement_lines",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_layby_agreement_lines_tenant_id",
                schema: "customer_accounts",
                table: "layby_agreement_lines",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_layby_agreement_lines_tenant_id_company_id",
                schema: "customer_accounts",
                table: "layby_agreement_lines",
                columns: new[] { "tenant_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_layby_agreement_lines_tenant_id_store_id",
                schema: "customer_accounts",
                table: "layby_agreement_lines",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ix_layby_agreements_sync_state",
                schema: "customer_accounts",
                table: "layby_agreements",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_layby_agreements_tenant_id",
                schema: "customer_accounts",
                table: "layby_agreements",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_layby_agreements_tenant_id_company_id",
                schema: "customer_accounts",
                table: "layby_agreements",
                columns: new[] { "tenant_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_layby_agreements_tenant_id_status_expiry",
                schema: "customer_accounts",
                table: "layby_agreements",
                columns: new[] { "tenant_id", "status", "expiry_date" });

            migrationBuilder.CreateIndex(
                name: "ix_layby_agreements_tenant_id_store_id",
                schema: "customer_accounts",
                table: "layby_agreements",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ux_layby_agreements_tenant_id_number",
                schema: "customer_accounts",
                table: "layby_agreements",
                columns: new[] { "tenant_id", "agreement_number" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_layby_instalments_agreement_id",
                schema: "customer_accounts",
                table: "layby_instalments",
                column: "agreement_id");

            migrationBuilder.CreateIndex(
                name: "ix_layby_instalments_sync_state",
                schema: "customer_accounts",
                table: "layby_instalments",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_layby_instalments_tenant_id",
                schema: "customer_accounts",
                table: "layby_instalments",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_layby_instalments_tenant_id_company_id",
                schema: "customer_accounts",
                table: "layby_instalments",
                columns: new[] { "tenant_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_layby_instalments_tenant_id_store_id",
                schema: "customer_accounts",
                table: "layby_instalments",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ux_layby_instalments_agreement_id_sequence",
                schema: "customer_accounts",
                table: "layby_instalments",
                columns: new[] { "agreement_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_terms_sync_state",
                schema: "customer_accounts",
                table: "terms",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_terms_tenant_id_company_id",
                schema: "customer_accounts",
                table: "terms",
                columns: new[] { "tenant_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_terms_tenant_id_store_id",
                schema: "customer_accounts",
                table: "terms",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ux_terms_tenant_id",
                schema: "customer_accounts",
                table: "terms",
                column: "tenant_id",
                unique: true,
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_holders",
                schema: "customer_accounts");

            migrationBuilder.DropTable(
                name: "accounts",
                schema: "customer_accounts");

            migrationBuilder.DropTable(
                name: "layby_agreement_lines",
                schema: "customer_accounts");

            migrationBuilder.DropTable(
                name: "layby_instalments",
                schema: "customer_accounts");

            migrationBuilder.DropTable(
                name: "terms",
                schema: "customer_accounts");

            migrationBuilder.DropTable(
                name: "layby_agreements",
                schema: "customer_accounts");
        }
    }
}
