using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Stage10b_Stokvels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hamper_baskets",
                schema: "customer_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_price_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    group_price_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
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
                    table.PrimaryKey("pk_hamper_baskets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stokvel_benefit_allocations",
                schema: "customer_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    basis = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    allocated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("pk_stokvel_benefit_allocations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stokvel_contributions",
                schema: "customer_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("pk_stokvel_contributions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stokvel_groups",
                schema: "customer_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    constitution = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    cycle_start = table.Column<DateOnly>(type: "date", nullable: false),
                    cycle_end = table.Column<DateOnly>(type: "date", nullable: false),
                    store_scope_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
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
                    table.PrimaryKey("pk_stokvel_groups", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stokvel_members",
                schema: "customer_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    partner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    joined_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    left_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    contribution_obligation_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    contribution_obligation_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
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
                    table.PrimaryKey("pk_stokvel_members", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stokvel_payouts",
                schema: "customer_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    hamper_basket_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    settled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sale_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("pk_stokvel_payouts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "hamper_basket_lines",
                schema: "customer_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    basket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    quantity_value = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    quantity_uom = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    substitution_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    substitution_item_variant_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("pk_hamper_basket_lines", x => x.id);
                    table.CheckConstraint("ck_hamper_basket_lines_quantity_positive", "quantity_value > 0");
                    table.ForeignKey(
                        name: "fk_hamper_basket_lines_hamper_baskets_basket_id",
                        column: x => x.basket_id,
                        principalSchema: "customer_accounts",
                        principalTable: "hamper_baskets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_hamper_basket_lines_basket_id",
                schema: "customer_accounts",
                table: "hamper_basket_lines",
                column: "basket_id");

            migrationBuilder.CreateIndex(
                name: "ix_hamper_basket_lines_sync_state",
                schema: "customer_accounts",
                table: "hamper_basket_lines",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_hamper_basket_lines_tenant_id",
                schema: "customer_accounts",
                table: "hamper_basket_lines",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_hamper_basket_lines_tenant_id_company_id",
                schema: "customer_accounts",
                table: "hamper_basket_lines",
                columns: new[] { "tenant_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_hamper_basket_lines_tenant_id_store_id",
                schema: "customer_accounts",
                table: "hamper_basket_lines",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ix_hamper_baskets_group_id",
                schema: "customer_accounts",
                table: "hamper_baskets",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "ix_hamper_baskets_sync_state",
                schema: "customer_accounts",
                table: "hamper_baskets",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_hamper_baskets_tenant_id",
                schema: "customer_accounts",
                table: "hamper_baskets",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_hamper_baskets_tenant_id_company_id",
                schema: "customer_accounts",
                table: "hamper_baskets",
                columns: new[] { "tenant_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_hamper_baskets_tenant_id_store_id",
                schema: "customer_accounts",
                table: "hamper_baskets",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stokvel_benefit_allocations_group_id",
                schema: "customer_accounts",
                table: "stokvel_benefit_allocations",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "ix_stokvel_benefit_allocations_member_id",
                schema: "customer_accounts",
                table: "stokvel_benefit_allocations",
                column: "member_id");

            migrationBuilder.CreateIndex(
                name: "ix_stokvel_benefit_allocations_sync_state",
                schema: "customer_accounts",
                table: "stokvel_benefit_allocations",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_stokvel_benefit_allocations_tenant_id",
                schema: "customer_accounts",
                table: "stokvel_benefit_allocations",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_stokvel_benefit_allocations_tenant_id_company_id",
                schema: "customer_accounts",
                table: "stokvel_benefit_allocations",
                columns: new[] { "tenant_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stokvel_benefit_allocations_tenant_id_store_id",
                schema: "customer_accounts",
                table: "stokvel_benefit_allocations",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stokvel_contributions_group_id",
                schema: "customer_accounts",
                table: "stokvel_contributions",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "ix_stokvel_contributions_member_id",
                schema: "customer_accounts",
                table: "stokvel_contributions",
                column: "member_id");

            migrationBuilder.CreateIndex(
                name: "ix_stokvel_contributions_sync_state",
                schema: "customer_accounts",
                table: "stokvel_contributions",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_stokvel_contributions_tenant_id",
                schema: "customer_accounts",
                table: "stokvel_contributions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_stokvel_contributions_tenant_id_company_id",
                schema: "customer_accounts",
                table: "stokvel_contributions",
                columns: new[] { "tenant_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stokvel_contributions_tenant_id_store_id",
                schema: "customer_accounts",
                table: "stokvel_contributions",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ux_stokvel_contributions_member_id_receipt",
                schema: "customer_accounts",
                table: "stokvel_contributions",
                columns: new[] { "member_id", "receipt_reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stokvel_groups_sync_state",
                schema: "customer_accounts",
                table: "stokvel_groups",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_stokvel_groups_tenant_id",
                schema: "customer_accounts",
                table: "stokvel_groups",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_stokvel_groups_tenant_id_company_id",
                schema: "customer_accounts",
                table: "stokvel_groups",
                columns: new[] { "tenant_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stokvel_groups_tenant_id_status",
                schema: "customer_accounts",
                table: "stokvel_groups",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_stokvel_groups_tenant_id_store_id",
                schema: "customer_accounts",
                table: "stokvel_groups",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ux_stokvel_groups_tenant_id_number",
                schema: "customer_accounts",
                table: "stokvel_groups",
                columns: new[] { "tenant_id", "group_number" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_stokvel_members_group_id",
                schema: "customer_accounts",
                table: "stokvel_members",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "ix_stokvel_members_group_id_partner_id",
                schema: "customer_accounts",
                table: "stokvel_members",
                columns: new[] { "group_id", "partner_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stokvel_members_sync_state",
                schema: "customer_accounts",
                table: "stokvel_members",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_stokvel_members_tenant_id",
                schema: "customer_accounts",
                table: "stokvel_members",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_stokvel_members_tenant_id_company_id",
                schema: "customer_accounts",
                table: "stokvel_members",
                columns: new[] { "tenant_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stokvel_members_tenant_id_store_id",
                schema: "customer_accounts",
                table: "stokvel_members",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stokvel_payouts_group_id",
                schema: "customer_accounts",
                table: "stokvel_payouts",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "ix_stokvel_payouts_member_id",
                schema: "customer_accounts",
                table: "stokvel_payouts",
                column: "member_id");

            migrationBuilder.CreateIndex(
                name: "ix_stokvel_payouts_sync_state",
                schema: "customer_accounts",
                table: "stokvel_payouts",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_stokvel_payouts_tenant_id",
                schema: "customer_accounts",
                table: "stokvel_payouts",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_stokvel_payouts_tenant_id_company_id",
                schema: "customer_accounts",
                table: "stokvel_payouts",
                columns: new[] { "tenant_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stokvel_payouts_tenant_id_store_id",
                schema: "customer_accounts",
                table: "stokvel_payouts",
                columns: new[] { "tenant_id", "store_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hamper_basket_lines",
                schema: "customer_accounts");

            migrationBuilder.DropTable(
                name: "stokvel_benefit_allocations",
                schema: "customer_accounts");

            migrationBuilder.DropTable(
                name: "stokvel_contributions",
                schema: "customer_accounts");

            migrationBuilder.DropTable(
                name: "stokvel_groups",
                schema: "customer_accounts");

            migrationBuilder.DropTable(
                name: "stokvel_members",
                schema: "customer_accounts");

            migrationBuilder.DropTable(
                name: "stokvel_payouts",
                schema: "customer_accounts");

            migrationBuilder.DropTable(
                name: "hamper_baskets",
                schema: "customer_accounts");
        }
    }
}
