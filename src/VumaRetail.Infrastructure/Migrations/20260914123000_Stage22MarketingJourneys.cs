using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations;

/// <summary>Persists versioned journeys, durable enrollments, and content-free attribution events.</summary>
public partial class Stage22MarketingJourneys : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.CreateTable("journey_definitions", "marketing", t => new
        {
            id = t.Column<Guid>("uuid", nullable: false), name = t.Column<string>("character varying(256)", maxLength: 256, nullable: false),
            version = t.Column<int>("integer", nullable: false), definition_json = t.Column<string>("jsonb", nullable: false), status = t.Column<string>("character varying(16)", maxLength: 16, nullable: false),
            tenant_id = t.Column<Guid>("uuid", nullable: false), store_id = t.Column<Guid>("uuid", nullable: true), company_id = t.Column<Guid>("uuid", nullable: false),
            created_at = t.Column<DateTimeOffset>("timestamp with time zone", nullable: false), created_by = t.Column<string>("character varying(128)", maxLength: 128, nullable: false), updated_at = t.Column<DateTimeOffset>("timestamp with time zone", nullable: false), updated_by = t.Column<string>("character varying(128)", maxLength: 128, nullable: false), row_version = t.Column<byte[]>("bytea", nullable: false), sync_state = t.Column<string>("character varying(32)", maxLength: 32, nullable: false), sync_stamp = t.Column<string>("character varying(86)", maxLength: 86, nullable: false), deleted_at = t.Column<DateTimeOffset>("timestamp with time zone", nullable: true), deleted_by = t.Column<string>("character varying(128)", maxLength: 128, nullable: true)
        }, t => t.PrimaryKey("pk_journey_definitions", x => x.id));
        m.CreateTable("journey_enrollments", "marketing", t => new
        {
            id = t.Column<Guid>("uuid", nullable: false), journey_definition_id = t.Column<Guid>("uuid", nullable: false), customer_id = t.Column<Guid>("uuid", nullable: false), idempotency_key = t.Column<string>("character varying(256)", maxLength: 256, nullable: false), next_run_at = t.Column<DateTimeOffset>("timestamp with time zone", nullable: false), is_active = t.Column<bool>("boolean", nullable: false),
            tenant_id = t.Column<Guid>("uuid", nullable: false), store_id = t.Column<Guid>("uuid", nullable: true), company_id = t.Column<Guid>("uuid", nullable: false), created_at = t.Column<DateTimeOffset>("timestamp with time zone", nullable: false), created_by = t.Column<string>("character varying(128)", maxLength: 128, nullable: false), updated_at = t.Column<DateTimeOffset>("timestamp with time zone", nullable: false), updated_by = t.Column<string>("character varying(128)", maxLength: 128, nullable: false), row_version = t.Column<byte[]>("bytea", nullable: false), sync_state = t.Column<string>("character varying(32)", maxLength: 32, nullable: false), sync_stamp = t.Column<string>("character varying(86)", maxLength: 86, nullable: false), deleted_at = t.Column<DateTimeOffset>("timestamp with time zone", nullable: true), deleted_by = t.Column<string>("character varying(128)", maxLength: 128, nullable: true)
        }, t => t.PrimaryKey("pk_journey_enrollments", x => x.id));
        m.CreateTable("attribution_events", "marketing", t => new
        {
            id = t.Column<Guid>("uuid", nullable: false), campaign_id = t.Column<Guid>("uuid", nullable: true), outbound_message_id = t.Column<Guid>("uuid", nullable: true), customer_id = t.Column<Guid>("uuid", nullable: false), event_type = t.Column<string>("character varying(64)", maxLength: 64, nullable: false), occurred_at = t.Column<DateTimeOffset>("timestamp with time zone", nullable: false),
            tenant_id = t.Column<Guid>("uuid", nullable: false), store_id = t.Column<Guid>("uuid", nullable: true), company_id = t.Column<Guid>("uuid", nullable: false), created_at = t.Column<DateTimeOffset>("timestamp with time zone", nullable: false), created_by = t.Column<string>("character varying(128)", maxLength: 128, nullable: false), updated_at = t.Column<DateTimeOffset>("timestamp with time zone", nullable: false), updated_by = t.Column<string>("character varying(128)", maxLength: 128, nullable: false), row_version = t.Column<byte[]>("bytea", nullable: false), sync_state = t.Column<string>("character varying(32)", maxLength: 32, nullable: false), sync_stamp = t.Column<string>("character varying(86)", maxLength: 86, nullable: false), deleted_at = t.Column<DateTimeOffset>("timestamp with time zone", nullable: true), deleted_by = t.Column<string>("character varying(128)", maxLength: 128, nullable: true)
        }, t => t.PrimaryKey("pk_attribution_events", x => x.id));
        m.CreateIndex("ux_journey_definitions_tenant_company_name_version", "journey_definitions", new[] { "tenant_id", "company_id", "name", "version" }, "marketing", unique: true, filter: "deleted_at IS NULL");
        m.CreateIndex("ux_journey_enrollments_tenant_company_idempotency", "journey_enrollments", new[] { "tenant_id", "company_id", "idempotency_key" }, "marketing", unique: true, filter: "deleted_at IS NULL");
        m.CreateIndex("ix_journey_enrollments_due", "journey_enrollments", new[] { "tenant_id", "company_id", "is_active", "next_run_at" }, "marketing");
        m.CreateIndex("ix_attribution_events_campaign_time", "attribution_events", new[] { "tenant_id", "company_id", "campaign_id", "occurred_at" }, "marketing");
    }
    protected override void Down(MigrationBuilder m)
    { m.DropTable("attribution_events", "marketing"); m.DropTable("journey_enrollments", "marketing"); m.DropTable("journey_definitions", "marketing"); }
}
