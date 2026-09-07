using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations.Registry
{
    /// <inheritdoc />
    public partial class _20260905_GroupMoneyEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "group_payment_runs",
                schema: "registry",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    capturing_company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tender_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    reference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    amount_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    amount_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_group_payment_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "group_receipts",
                schema: "registry",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    capturing_company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tender_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    reference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    amount_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    amount_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_group_receipts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "inter_company_clearing_intents",
                schema: "registry",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_document_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    from_company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    state = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    settled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    amount_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    amount_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inter_company_clearing_intents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "group_payment_allocations",
                schema: "registry",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_payment_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_partner_id = table.Column<Guid>(type: "uuid", nullable: true),
                    leg_state = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    target_invoice_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    applied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    compensated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    amount_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    amount_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_group_payment_allocations", x => x.id);
                    table.ForeignKey(
                        name: "fk_group_payment_allocations_group_payment_runs_group_payment_",
                        column: x => x.group_payment_run_id,
                        principalSchema: "registry",
                        principalTable: "group_payment_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "group_receipt_allocations",
                schema: "registry",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_receipt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_partner_id = table.Column<Guid>(type: "uuid", nullable: true),
                    leg_state = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    target_invoice_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    applied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    compensated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    amount_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    amount_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_group_receipt_allocations", x => x.id);
                    table.ForeignKey(
                        name: "fk_group_receipt_allocations_group_receipts_group_receipt_id",
                        column: x => x.group_receipt_id,
                        principalSchema: "registry",
                        principalTable: "group_receipts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "inter_company_clearing_legs",
                schema: "registry",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    intent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    direction = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    currency = table.Column<string>(type: "text", nullable: false),
                    state = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    acknowledged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    compensated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    inter_company_clearing_intent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    amount_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inter_company_clearing_legs", x => x.id);
                    table.ForeignKey(
                        name: "fk_inter_company_clearing_legs_inter_company_clearing_intents_",
                        column: x => x.inter_company_clearing_intent_id,
                        principalSchema: "registry",
                        principalTable: "inter_company_clearing_intents",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_group_payment_allocations_group_payment_run_id",
                schema: "registry",
                table: "group_payment_allocations",
                column: "group_payment_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_group_payment_runs_tenant_id_paid_at",
                schema: "registry",
                table: "group_payment_runs",
                columns: new[] { "tenant_id", "paid_at" });

            migrationBuilder.CreateIndex(
                name: "ix_group_payment_runs_tenant_id_status",
                schema: "registry",
                table: "group_payment_runs",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_group_receipt_allocations_group_receipt_id",
                schema: "registry",
                table: "group_receipt_allocations",
                column: "group_receipt_id");

            migrationBuilder.CreateIndex(
                name: "ix_group_receipts_tenant_id_captured_at",
                schema: "registry",
                table: "group_receipts",
                columns: new[] { "tenant_id", "captured_at" });

            migrationBuilder.CreateIndex(
                name: "ix_group_receipts_tenant_id_status",
                schema: "registry",
                table: "group_receipts",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_inter_company_clearing_intents_tenant_id_group_document_id",
                schema: "registry",
                table: "inter_company_clearing_intents",
                columns: new[] { "tenant_id", "group_document_id" });

            migrationBuilder.CreateIndex(
                name: "ix_inter_company_clearing_intents_tenant_id_state",
                schema: "registry",
                table: "inter_company_clearing_intents",
                columns: new[] { "tenant_id", "state" });

            migrationBuilder.CreateIndex(
                name: "ix_inter_company_clearing_legs_inter_company_clearing_intent_id",
                schema: "registry",
                table: "inter_company_clearing_legs",
                column: "inter_company_clearing_intent_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "group_payment_allocations",
                schema: "registry");

            migrationBuilder.DropTable(
                name: "group_receipt_allocations",
                schema: "registry");

            migrationBuilder.DropTable(
                name: "inter_company_clearing_legs",
                schema: "registry");

            migrationBuilder.DropTable(
                name: "group_payment_runs",
                schema: "registry");

            migrationBuilder.DropTable(
                name: "group_receipts",
                schema: "registry");

            migrationBuilder.DropTable(
                name: "inter_company_clearing_intents",
                schema: "registry");
        }
    }
}
