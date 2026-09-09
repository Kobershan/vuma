using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Stage15_Planning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "planning");

            migrationBuilder.CreateTable(
                name: "abc_xyz_classifications",
                schema: "planning",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    abc = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    xyz = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    demand_share = table.Column<decimal>(type: "numeric(9,6)", nullable: false),
                    coefficient_of_variation = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    generated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("pk_abc_xyz_classifications", x => x.id);
                    table.CheckConstraint("ck_abc_xyz_exactly_one_sku", "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int) = 1");
                });

            migrationBuilder.CreateTable(
                name: "demand_forecasts",
                schema: "planning",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    forecast_period = table.Column<DateOnly>(type: "date", nullable: false),
                    forecast_method = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    mape = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    bias = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    version = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    generated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    generated_by = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("pk_demand_forecasts", x => x.id);
                    table.CheckConstraint("ck_demand_forecasts_exactly_one_sku", "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int) = 1");
                });

            migrationBuilder.CreateTable(
                name: "demand_history",
                schema: "planning",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    total_quantity = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    uom = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    generated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("pk_demand_history", x => x.id);
                    table.CheckConstraint("ck_demand_history_exactly_one_sku", "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int) = 1");
                });

            migrationBuilder.CreateTable(
                name: "markdown_plans",
                schema: "planning",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    approval_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    promotion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    supersedes_plan_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("pk_markdown_plans", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "open_to_buy_budgets",
                schema: "planning",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    month = table.Column<int>(type: "integer", nullable: false),
                    category_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    planned_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
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
                    table.PrimaryKey("pk_open_to_buy_budgets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "replenishment_parameters",
                schema: "planning",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    forecast_method = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    service_level_percent = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    lead_time_days = table.Column<int>(type: "integer", nullable: false),
                    review_period_days = table.Column<int>(type: "integer", nullable: false),
                    uom = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
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
                    table.PrimaryKey("pk_replenishment_parameters", x => x.id);
                    table.CheckConstraint("ck_replenishment_parameters_exactly_one_sku", "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int) = 1");
                });

            migrationBuilder.CreateTable(
                name: "replenishment_suggestions",
                schema: "planning",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    suggested_quantity = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    accepted_quantity = table.Column<decimal>(type: "numeric(18,6)", nullable: true),
                    uom = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    source_company_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    downstream_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    over_open_to_buy = table.Column<bool>(type: "boolean", nullable: false),
                    raised_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("pk_replenishment_suggestions", x => x.id);
                    table.CheckConstraint("ck_replenishment_suggestions_exactly_one_sku", "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int) = 1");
                });

            migrationBuilder.CreateTable(
                name: "safety_stock_calculations",
                schema: "planning",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lead_time_demand_mean = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    demand_variance = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    service_level_percent = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    history_weeks = table.Column<int>(type: "integer", nullable: false),
                    lead_time_days = table.Column<int>(type: "integer", nullable: false),
                    safety_stock = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    reorder_point = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    low_confidence = table.Column<bool>(type: "boolean", nullable: false),
                    method = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    calculated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("pk_safety_stock_calculations", x => x.id);
                    table.CheckConstraint("ck_safety_stock_exactly_one_sku", "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int) = 1");
                });

            migrationBuilder.CreateTable(
                name: "markdown_plan_lines",
                schema: "planning",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    markdown_plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    current_price = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    proposed_discount_percent = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    abc_xyz = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    sell_through_percent = table.Column<decimal>(type: "numeric(7,3)", nullable: false),
                    days_of_supply = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    promotion_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("pk_markdown_plan_lines", x => x.id);
                    table.CheckConstraint("ck_markdown_plan_lines_exactly_one_sku", "((item_id IS NOT NULL)::int + (item_variant_id IS NOT NULL)::int) = 1");
                    table.ForeignKey(
                        name: "fk_markdown_plan_lines_markdown_plans_markdown_plan_id",
                        column: x => x.markdown_plan_id,
                        principalSchema: "planning",
                        principalTable: "markdown_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_abc_xyz_classifications_sync_state",
                schema: "planning",
                table: "abc_xyz_classifications",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_abc_xyz_classifications_tenant_id",
                schema: "planning",
                table: "abc_xyz_classifications",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_abc_xyz_classifications_tenant_id_company_id",
                schema: "planning",
                table: "abc_xyz_classifications",
                columns: new[] { "tenant_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_abc_xyz_classifications_tenant_id_store_id",
                schema: "planning",
                table: "abc_xyz_classifications",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ix_abc_xyz_sku_generated",
                schema: "planning",
                table: "abc_xyz_classifications",
                columns: new[] { "tenant_id", "company_id", "location_id", "item_id", "item_variant_id", "generated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_demand_forecasts_sync_state",
                schema: "planning",
                table: "demand_forecasts",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_demand_forecasts_tenant_id",
                schema: "planning",
                table: "demand_forecasts",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_demand_forecasts_tenant_id_company_id",
                schema: "planning",
                table: "demand_forecasts",
                columns: new[] { "tenant_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_demand_forecasts_tenant_id_store_id",
                schema: "planning",
                table: "demand_forecasts",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ux_demand_forecasts_period_version",
                schema: "planning",
                table: "demand_forecasts",
                columns: new[] { "tenant_id", "company_id", "location_id", "item_id", "item_variant_id", "forecast_period", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_demand_history_sync_state",
                schema: "planning",
                table: "demand_history",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_demand_history_tenant_id",
                schema: "planning",
                table: "demand_history",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_demand_history_tenant_id_company_id",
                schema: "planning",
                table: "demand_history",
                columns: new[] { "tenant_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_demand_history_tenant_id_store_id",
                schema: "planning",
                table: "demand_history",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ux_demand_history_item_period",
                schema: "planning",
                table: "demand_history",
                columns: new[] { "tenant_id", "company_id", "location_id", "item_id", "period_start" },
                unique: true,
                filter: "item_id IS NOT NULL AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_demand_history_variant_period",
                schema: "planning",
                table: "demand_history",
                columns: new[] { "tenant_id", "company_id", "location_id", "item_variant_id", "period_start" },
                unique: true,
                filter: "item_variant_id IS NOT NULL AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_markdown_plan_lines_plan",
                schema: "planning",
                table: "markdown_plan_lines",
                column: "markdown_plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_markdown_plan_lines_sync_state",
                schema: "planning",
                table: "markdown_plan_lines",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_markdown_plan_lines_tenant_id",
                schema: "planning",
                table: "markdown_plan_lines",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_markdown_plan_lines_tenant_id_company_id",
                schema: "planning",
                table: "markdown_plan_lines",
                columns: new[] { "tenant_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_markdown_plan_lines_tenant_id_store_id",
                schema: "planning",
                table: "markdown_plan_lines",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ix_markdown_plans_status",
                schema: "planning",
                table: "markdown_plans",
                columns: new[] { "tenant_id", "company_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_markdown_plans_sync_state",
                schema: "planning",
                table: "markdown_plans",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_markdown_plans_tenant_id",
                schema: "planning",
                table: "markdown_plans",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_markdown_plans_tenant_id_company_id",
                schema: "planning",
                table: "markdown_plans",
                columns: new[] { "tenant_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_markdown_plans_tenant_id_store_id",
                schema: "planning",
                table: "markdown_plans",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ux_markdown_plans_code",
                schema: "planning",
                table: "markdown_plans",
                columns: new[] { "tenant_id", "code" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_open_to_buy_budgets_sync_state",
                schema: "planning",
                table: "open_to_buy_budgets",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_open_to_buy_budgets_tenant_id",
                schema: "planning",
                table: "open_to_buy_budgets",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_open_to_buy_budgets_tenant_id_company_id",
                schema: "planning",
                table: "open_to_buy_budgets",
                columns: new[] { "tenant_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_open_to_buy_budgets_tenant_id_store_id",
                schema: "planning",
                table: "open_to_buy_budgets",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ux_otb_budgets_month",
                schema: "planning",
                table: "open_to_buy_budgets",
                columns: new[] { "tenant_id", "company_id", "year", "month", "category_code" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_replenishment_parameters_sync_state",
                schema: "planning",
                table: "replenishment_parameters",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_replenishment_parameters_tenant_id",
                schema: "planning",
                table: "replenishment_parameters",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_replenishment_parameters_tenant_id_company_id",
                schema: "planning",
                table: "replenishment_parameters",
                columns: new[] { "tenant_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_replenishment_parameters_tenant_id_store_id",
                schema: "planning",
                table: "replenishment_parameters",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ux_replenishment_parameters_sku",
                schema: "planning",
                table: "replenishment_parameters",
                columns: new[] { "tenant_id", "company_id", "location_id", "item_id", "item_variant_id" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_replenishment_suggestions_status",
                schema: "planning",
                table: "replenishment_suggestions",
                columns: new[] { "tenant_id", "company_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_replenishment_suggestions_sync_state",
                schema: "planning",
                table: "replenishment_suggestions",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_replenishment_suggestions_tenant_id",
                schema: "planning",
                table: "replenishment_suggestions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_replenishment_suggestions_tenant_id_company_id",
                schema: "planning",
                table: "replenishment_suggestions",
                columns: new[] { "tenant_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_replenishment_suggestions_tenant_id_store_id",
                schema: "planning",
                table: "replenishment_suggestions",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ux_replenishment_suggestions_run_key",
                schema: "planning",
                table: "replenishment_suggestions",
                column: "idempotency_key",
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_safety_stock_calculations_sync_state",
                schema: "planning",
                table: "safety_stock_calculations",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_safety_stock_calculations_tenant_id",
                schema: "planning",
                table: "safety_stock_calculations",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_safety_stock_calculations_tenant_id_company_id",
                schema: "planning",
                table: "safety_stock_calculations",
                columns: new[] { "tenant_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_safety_stock_calculations_tenant_id_store_id",
                schema: "planning",
                table: "safety_stock_calculations",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ix_safety_stock_sku_calculated",
                schema: "planning",
                table: "safety_stock_calculations",
                columns: new[] { "tenant_id", "company_id", "location_id", "item_id", "item_variant_id", "calculated_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "abc_xyz_classifications",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "demand_forecasts",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "demand_history",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "markdown_plan_lines",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "open_to_buy_budgets",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "replenishment_parameters",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "replenishment_suggestions",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "safety_stock_calculations",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "markdown_plans",
                schema: "planning");
        }
    }
}
