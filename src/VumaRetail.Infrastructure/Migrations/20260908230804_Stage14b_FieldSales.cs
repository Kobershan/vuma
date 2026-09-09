using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Stage14b_FieldSales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "fieldsales");

            migrationBuilder.CreateTable(
                name: "pro_forma_credit_notes",
                schema: "fieldsales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    credit_note_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    rep_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_invoice_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    reason_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approval_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resulting_return_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decision_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("pk_pro_forma_credit_notes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pro_forma_orders",
                schema: "fieldsales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pro_forma_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    rep_id = table.Column<Guid>(type: "uuid", nullable: false),
                    partner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delivery_address_line1 = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    delivery_address_city = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    delivery_address_country_code = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: true),
                    delivery_address_line2 = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    delivery_address_region = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    delivery_address_postal_code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approval_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    credit_hold_id = table.Column<Guid>(type: "uuid", nullable: true),
                    converted_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decision_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    reprice_delta_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("pk_pro_forma_orders", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "rep_performance_snapshots",
                schema: "fieldsales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rep_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    captured_count = table.Column<int>(type: "integer", nullable: false),
                    margin_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    active_customers = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    snapshotted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    captured_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    captured_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    converted_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    converted_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    credited_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    credited_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    expired_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    expired_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    invoiced_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    invoiced_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    net_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    net_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    rejected_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    rejected_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
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
                    table.PrimaryKey("pk_rep_performance_snapshots", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "rep_targets",
                schema: "fieldsales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rep_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    is_current = table.Column<bool>(type: "boolean", nullable: false),
                    set_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    target_net_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    target_net_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
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
                    table.PrimaryKey("pk_rep_targets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "reps",
                schema: "fieldsales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    registry_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    company_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    customer_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    territory_province = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    territory_city = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    territory_suburb = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    see_cost = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("pk_reps", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pro_forma_credit_note_lines",
                schema: "fieldsales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pro_forma_credit_note_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_invoice_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    quantity_value = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    quantity_uom = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    net_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    net_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    tax_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    tax_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    unit_price_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    unit_price_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
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
                    table.PrimaryKey("pk_pro_forma_credit_note_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_pro_forma_credit_note_lines_pro_forma_credit_notes_pro_form",
                        column: x => x.pro_forma_credit_note_id,
                        principalSchema: "fieldsales",
                        principalTable: "pro_forma_credit_notes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pro_forma_order_lines",
                schema: "fieldsales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pro_forma_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    quantity_value = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    quantity_uom = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    tax_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    pack_size_description = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    price_list_id = table.Column<Guid>(type: "uuid", nullable: true),
                    promotions_summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    availability_as_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    available_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    available_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    discount_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    discount_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    net_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    net_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    tax_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    tax_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    unit_price_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    unit_price_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
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
                    table.PrimaryKey("pk_pro_forma_order_lines", x => x.id);
                    table.CheckConstraint("ck_pro_forma_order_lines_quantity_positive", "quantity_value > 0");
                    table.ForeignKey(
                        name: "fk_pro_forma_order_lines_pro_forma_orders_pro_forma_order_id",
                        column: x => x.pro_forma_order_id,
                        principalSchema: "fieldsales",
                        principalTable: "pro_forma_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pro_forma_credit_note_lines_pro_forma_credit_note_id",
                schema: "fieldsales",
                table: "pro_forma_credit_note_lines",
                column: "pro_forma_credit_note_id");

            migrationBuilder.CreateIndex(
                name: "ix_pro_forma_credit_note_lines_sync_state",
                schema: "fieldsales",
                table: "pro_forma_credit_note_lines",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_pro_forma_credit_note_lines_tenant_id",
                schema: "fieldsales",
                table: "pro_forma_credit_note_lines",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_pro_forma_credit_note_lines_tenant_id_company_id",
                schema: "fieldsales",
                table: "pro_forma_credit_note_lines",
                columns: new[] { "tenant_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pro_forma_credit_note_lines_tenant_id_pro_forma_credit_note",
                schema: "fieldsales",
                table: "pro_forma_credit_note_lines",
                columns: new[] { "tenant_id", "pro_forma_credit_note_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pro_forma_credit_note_lines_tenant_id_store_id",
                schema: "fieldsales",
                table: "pro_forma_credit_note_lines",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pro_forma_credit_notes_sync_state",
                schema: "fieldsales",
                table: "pro_forma_credit_notes",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_pro_forma_credit_notes_tenant_id",
                schema: "fieldsales",
                table: "pro_forma_credit_notes",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_pro_forma_credit_notes_tenant_id_company_id",
                schema: "fieldsales",
                table: "pro_forma_credit_notes",
                columns: new[] { "tenant_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pro_forma_credit_notes_tenant_id_credit_note_number",
                schema: "fieldsales",
                table: "pro_forma_credit_notes",
                columns: new[] { "tenant_id", "credit_note_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pro_forma_credit_notes_tenant_id_idempotency_key",
                schema: "fieldsales",
                table: "pro_forma_credit_notes",
                columns: new[] { "tenant_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pro_forma_credit_notes_tenant_id_rep_id_status",
                schema: "fieldsales",
                table: "pro_forma_credit_notes",
                columns: new[] { "tenant_id", "rep_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_pro_forma_credit_notes_tenant_id_store_id",
                schema: "fieldsales",
                table: "pro_forma_credit_notes",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pro_forma_order_lines_pro_forma_order_id",
                schema: "fieldsales",
                table: "pro_forma_order_lines",
                column: "pro_forma_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_pro_forma_order_lines_sync_state",
                schema: "fieldsales",
                table: "pro_forma_order_lines",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_pro_forma_order_lines_tenant_id",
                schema: "fieldsales",
                table: "pro_forma_order_lines",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_pro_forma_order_lines_tenant_id_company_id",
                schema: "fieldsales",
                table: "pro_forma_order_lines",
                columns: new[] { "tenant_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pro_forma_order_lines_tenant_id_pro_forma_order_id",
                schema: "fieldsales",
                table: "pro_forma_order_lines",
                columns: new[] { "tenant_id", "pro_forma_order_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pro_forma_order_lines_tenant_id_store_id",
                schema: "fieldsales",
                table: "pro_forma_order_lines",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pro_forma_orders_sync_state",
                schema: "fieldsales",
                table: "pro_forma_orders",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_pro_forma_orders_tenant_id",
                schema: "fieldsales",
                table: "pro_forma_orders",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_pro_forma_orders_tenant_id_company_id",
                schema: "fieldsales",
                table: "pro_forma_orders",
                columns: new[] { "tenant_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pro_forma_orders_tenant_id_idempotency_key",
                schema: "fieldsales",
                table: "pro_forma_orders",
                columns: new[] { "tenant_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pro_forma_orders_tenant_id_pro_forma_number",
                schema: "fieldsales",
                table: "pro_forma_orders",
                columns: new[] { "tenant_id", "pro_forma_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pro_forma_orders_tenant_id_rep_id_status",
                schema: "fieldsales",
                table: "pro_forma_orders",
                columns: new[] { "tenant_id", "rep_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_pro_forma_orders_tenant_id_store_id",
                schema: "fieldsales",
                table: "pro_forma_orders",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ix_rep_performance_snapshots_sync_state",
                schema: "fieldsales",
                table: "rep_performance_snapshots",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_rep_performance_snapshots_tenant_id",
                schema: "fieldsales",
                table: "rep_performance_snapshots",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_rep_performance_snapshots_tenant_id_company_id",
                schema: "fieldsales",
                table: "rep_performance_snapshots",
                columns: new[] { "tenant_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_rep_performance_snapshots_tenant_id_rep_id_period_start_ver",
                schema: "fieldsales",
                table: "rep_performance_snapshots",
                columns: new[] { "tenant_id", "rep_id", "period_start", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rep_performance_snapshots_tenant_id_store_id",
                schema: "fieldsales",
                table: "rep_performance_snapshots",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ix_rep_targets_sync_state",
                schema: "fieldsales",
                table: "rep_targets",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_rep_targets_tenant_id",
                schema: "fieldsales",
                table: "rep_targets",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_rep_targets_tenant_id_company_id",
                schema: "fieldsales",
                table: "rep_targets",
                columns: new[] { "tenant_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_rep_targets_tenant_id_rep_id_period_start_version",
                schema: "fieldsales",
                table: "rep_targets",
                columns: new[] { "tenant_id", "rep_id", "period_start", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rep_targets_tenant_id_store_id",
                schema: "fieldsales",
                table: "rep_targets",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ix_reps_sync_state",
                schema: "fieldsales",
                table: "reps",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_reps_tenant_id",
                schema: "fieldsales",
                table: "reps",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_reps_tenant_id_company_id",
                schema: "fieldsales",
                table: "reps",
                columns: new[] { "tenant_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_reps_tenant_id_is_active",
                schema: "fieldsales",
                table: "reps",
                columns: new[] { "tenant_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_reps_tenant_id_registry_user_id",
                schema: "fieldsales",
                table: "reps",
                columns: new[] { "tenant_id", "registry_user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_reps_tenant_id_store_id",
                schema: "fieldsales",
                table: "reps",
                columns: new[] { "tenant_id", "store_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pro_forma_credit_note_lines",
                schema: "fieldsales");

            migrationBuilder.DropTable(
                name: "pro_forma_order_lines",
                schema: "fieldsales");

            migrationBuilder.DropTable(
                name: "rep_performance_snapshots",
                schema: "fieldsales");

            migrationBuilder.DropTable(
                name: "rep_targets",
                schema: "fieldsales");

            migrationBuilder.DropTable(
                name: "reps",
                schema: "fieldsales");

            migrationBuilder.DropTable(
                name: "pro_forma_credit_notes",
                schema: "fieldsales");

            migrationBuilder.DropTable(
                name: "pro_forma_orders",
                schema: "fieldsales");
        }
    }
}
