using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.RegistryMigrations
{
    /// <inheritdoc />
    public partial class Stage09b_TradingSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "trading_sessions",
                schema: "registry",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    premises_id = table.Column<Guid>(type: "uuid", nullable: false),
                    terminal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cashier_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_group_partner_id = table.Column<Guid>(type: "uuid", nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    tender_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    tender_amount_value = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    tender_amount_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: true),
                    tender_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    unwound_invoices_json = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tendered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    voided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    void_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trading_sessions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "trading_session_segments",
                schema: "registry",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    tender_allocation_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    tender_allocation_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: true),
                    allocation_basis = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    resulting_sale_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resulting_invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resulting_invoice_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    is_removed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trading_session_segments", x => x.id);
                    table.ForeignKey(
                        name: "fk_trading_session_segments_trading_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "registry",
                        principalTable: "trading_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trading_session_lines",
                schema: "registry",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    segment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    barcode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    quantity_value = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    quantity_uom = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    tax_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    pack_size_description = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    price_list_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_voided = table.Column<bool>(type: "boolean", nullable: false),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    voided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    discount_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    discount_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    net_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    net_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    tax_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    tax_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    unit_price_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    unit_price_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trading_session_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_trading_session_lines_trading_session_segments_segment_id",
                        column: x => x.segment_id,
                        principalSchema: "registry",
                        principalTable: "trading_session_segments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_trading_session_lines_segment_id",
                schema: "registry",
                table: "trading_session_lines",
                column: "segment_id");

            migrationBuilder.CreateIndex(
                name: "ix_trading_session_lines_tenant_id_segment_id",
                schema: "registry",
                table: "trading_session_lines",
                columns: new[] { "tenant_id", "segment_id" });

            migrationBuilder.CreateIndex(
                name: "ix_trading_session_lines_tenant_id_session_id",
                schema: "registry",
                table: "trading_session_lines",
                columns: new[] { "tenant_id", "session_id" });

            migrationBuilder.CreateIndex(
                name: "ix_trading_session_segments_session_id",
                schema: "registry",
                table: "trading_session_segments",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_trading_session_segments_tenant_id_session_id",
                schema: "registry",
                table: "trading_session_segments",
                columns: new[] { "tenant_id", "session_id" });

            migrationBuilder.CreateIndex(
                name: "ix_trading_session_segments_tenant_id_session_id_company_id",
                schema: "registry",
                table: "trading_session_segments",
                columns: new[] { "tenant_id", "session_id", "company_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_trading_sessions_tenant_id_idempotency_key",
                schema: "registry",
                table: "trading_sessions",
                columns: new[] { "tenant_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_trading_sessions_tenant_id_session_number",
                schema: "registry",
                table: "trading_sessions",
                columns: new[] { "tenant_id", "session_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_trading_sessions_tenant_id_status",
                schema: "registry",
                table: "trading_sessions",
                columns: new[] { "tenant_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "trading_session_lines",
                schema: "registry");

            migrationBuilder.DropTable(
                name: "trading_session_segments",
                schema: "registry");

            migrationBuilder.DropTable(
                name: "trading_sessions",
                schema: "registry");
        }
    }
}
