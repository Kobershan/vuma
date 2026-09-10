using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.RegistryMigrations
{
    /// <inheritdoc />
    public partial class Stage22RegistryTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "business_company_memberships",
                schema: "registry",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_business_company_memberships", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "business_registrations",
                schema: "registry",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_business_registrations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "group_hierarchy_nodes",
                schema: "registry",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    node_type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    ownership_type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    parent_node_id = table.Column<Guid>(type: "uuid", nullable: true),
                    store_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    stock_holding = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_group_hierarchy_nodes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "group_settings",
                schema: "registry",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transfer_value_threshold = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    transfer_costing_method = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    discrepancy_default_owner = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    store_code_prefix = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    regional_approval_business_hours = table.Column<int>(type: "integer", nullable: false),
                    holding_store_decision_hours = table.Column<int>(type: "integer", nullable: false),
                    discrepancy_review_business_days = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_group_settings", x => new { x.tenant_id, x.business_id });
                });

            migrationBuilder.CreateTable(
                name: "owned_stock_on_hand_projection",
                schema: "registry",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    on_hand = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    reserved = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    in_staging = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    available = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    unit_of_measure = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    as_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_owned_stock_on_hand_projection", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "premises_sku_routing",
                schema: "registry",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    premises_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku_or_barcode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_barcode = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_premises_sku_routing", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stock_transfer_requests",
                schema: "registry",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requester_company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sender_company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receiver_company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    holding_company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    total_value = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    central_buying = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    received_quantity = table.Column<decimal>(type: "numeric(18,6)", nullable: true),
                    discrepancy_quantity = table.Column<decimal>(type: "numeric(18,6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_transfer_requests", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_business_company_memberships_tenant_id_business_id_company_",
                schema: "registry",
                table: "business_company_memberships",
                columns: new[] { "tenant_id", "business_id", "company_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_business_registrations_tenant_id_name",
                schema: "registry",
                table: "business_registrations",
                columns: new[] { "tenant_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_group_hierarchy_nodes_tenant_id_business_id_company_id",
                schema: "registry",
                table: "group_hierarchy_nodes",
                columns: new[] { "tenant_id", "business_id", "company_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_group_hierarchy_nodes_tenant_id_business_id_parent_node_id",
                schema: "registry",
                table: "group_hierarchy_nodes",
                columns: new[] { "tenant_id", "business_id", "parent_node_id" });

            migrationBuilder.CreateIndex(
                name: "ix_owned_stock_on_hand_projection_tenant_id_business_id_compan",
                schema: "registry",
                table: "owned_stock_on_hand_projection",
                columns: new[] { "tenant_id", "business_id", "company_id", "location_id" });

            migrationBuilder.CreateIndex(
                name: "ix_premises_sku_routing_tenant_id_premises_id_sku_or_barcode_i",
                schema: "registry",
                table: "premises_sku_routing",
                columns: new[] { "tenant_id", "premises_id", "sku_or_barcode", "is_barcode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_requests_tenant_id_sender_company_id_receive",
                schema: "registry",
                table: "stock_transfer_requests",
                columns: new[] { "tenant_id", "sender_company_id", "receiver_company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_requests_tenant_id_status",
                schema: "registry",
                table: "stock_transfer_requests",
                columns: new[] { "tenant_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "business_company_memberships",
                schema: "registry");

            migrationBuilder.DropTable(
                name: "business_registrations",
                schema: "registry");

            migrationBuilder.DropTable(
                name: "group_hierarchy_nodes",
                schema: "registry");

            migrationBuilder.DropTable(
                name: "group_settings",
                schema: "registry");

            migrationBuilder.DropTable(
                name: "owned_stock_on_hand_projection",
                schema: "registry");

            migrationBuilder.DropTable(
                name: "premises_sku_routing",
                schema: "registry");

            migrationBuilder.DropTable(
                name: "stock_transfer_requests",
                schema: "registry");
        }
    }
}
