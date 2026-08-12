using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Licensing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "licensing");

            migrationBuilder.CreateTable(
                name: "activations",
                schema: "licensing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    licence_key_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    activation_reference = table.Column<Guid>(type: "uuid", nullable: false),
                    install_id = table.Column<Guid>(type: "uuid", nullable: false),
                    node_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    fingerprint_salt = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    fingerprint_components = table.Column<string>(type: "jsonb", nullable: false),
                    fingerprint_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    activated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    rebind_count = table.Column<int>(type: "integer", nullable: false),
                    last_rebind_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_contact_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("pk_activations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "clock_watermarks",
                schema: "licensing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    node_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    highest_seen = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    rollback_count = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("pk_clock_watermarks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "emergency_unlocks",
                schema: "licensing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code_reference = table.Column<Guid>(type: "uuid", nullable: false),
                    redeemed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    reported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("pk_emergency_unlocks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "leases",
                schema: "licensing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    activation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lease_reference = table.Column<Guid>(type: "uuid", nullable: false),
                    document = table.Column<string>(type: "text", nullable: false),
                    entitlements = table.Column<string>(type: "jsonb", nullable: false),
                    limits = table.Column<string>(type: "jsonb", nullable: false),
                    declared_level = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    declared_reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    issuance_counter = table.Column<long>(type: "bigint", nullable: false),
                    amount_due_value = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    amount_due_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: true),
                    pay_url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    update_payment_method_url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    dunning_completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    write_unlock_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    support_phone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    messages = table.Column<string>(type: "jsonb", nullable: false),
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
                    table.PrimaryKey("pk_leases", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "licences",
                schema: "licensing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    activation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document = table.Column<string>(type: "text", nullable: false),
                    plan_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    entitlements = table.Column<string>(type: "jsonb", nullable: false),
                    limits = table.Column<string>(type: "jsonb", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fingerprint_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    nonce = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    issuance_counter = table.Column<long>(type: "bigint", nullable: false),
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
                    table.PrimaryKey("pk_licences", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "metering_records",
                schema: "licensing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    node_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    period = table.Column<DateOnly>(type: "date", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("pk_metering_records", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "support_grants",
                schema: "licensing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    grant_reference = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    scope = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    decided_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("pk_support_grants", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tamper_flags",
                schema: "licensing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    detected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    detail = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    reported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("pk_tamper_flags", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_activations_sync_state",
                schema: "licensing",
                table: "activations",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_activations_tenant_id",
                schema: "licensing",
                table: "activations",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_activations_tenant_id_store_id",
                schema: "licensing",
                table: "activations",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ux_activations_tenant_node",
                schema: "licensing",
                table: "activations",
                columns: new[] { "tenant_id", "node_id" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_clock_watermarks_sync_state",
                schema: "licensing",
                table: "clock_watermarks",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_clock_watermarks_tenant_id",
                schema: "licensing",
                table: "clock_watermarks",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_clock_watermarks_tenant_id_store_id",
                schema: "licensing",
                table: "clock_watermarks",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ux_clock_watermarks_tenant_node",
                schema: "licensing",
                table: "clock_watermarks",
                columns: new[] { "tenant_id", "node_id" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_emergency_unlocks_sync_state",
                schema: "licensing",
                table: "emergency_unlocks",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_emergency_unlocks_tenant_expires_at",
                schema: "licensing",
                table: "emergency_unlocks",
                columns: new[] { "tenant_id", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_emergency_unlocks_tenant_id",
                schema: "licensing",
                table: "emergency_unlocks",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_emergency_unlocks_tenant_id_store_id",
                schema: "licensing",
                table: "emergency_unlocks",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ux_emergency_unlocks_tenant_code",
                schema: "licensing",
                table: "emergency_unlocks",
                columns: new[] { "tenant_id", "code_reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_leases_sync_state",
                schema: "licensing",
                table: "leases",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_leases_tenant_id",
                schema: "licensing",
                table: "leases",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_leases_tenant_id_store_id",
                schema: "licensing",
                table: "leases",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ix_leases_tenant_issued_at",
                schema: "licensing",
                table: "leases",
                columns: new[] { "tenant_id", "issued_at" });

            migrationBuilder.CreateIndex(
                name: "ix_licences_sync_state",
                schema: "licensing",
                table: "licences",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_licences_tenant_id",
                schema: "licensing",
                table: "licences",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_licences_tenant_id_store_id",
                schema: "licensing",
                table: "licences",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ix_licences_tenant_issuance_counter",
                schema: "licensing",
                table: "licences",
                columns: new[] { "tenant_id", "issuance_counter" });

            migrationBuilder.CreateIndex(
                name: "ix_metering_records_pending",
                schema: "licensing",
                table: "metering_records",
                columns: new[] { "tenant_id", "period" },
                filter: "state = 'Pending' AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_metering_records_sync_state",
                schema: "licensing",
                table: "metering_records",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_metering_records_tenant_id",
                schema: "licensing",
                table: "metering_records",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_metering_records_tenant_id_store_id",
                schema: "licensing",
                table: "metering_records",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ux_metering_records_tenant_node_period",
                schema: "licensing",
                table: "metering_records",
                columns: new[] { "tenant_id", "node_id", "period" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_support_grants_sync_state",
                schema: "licensing",
                table: "support_grants",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_support_grants_tenant_id",
                schema: "licensing",
                table: "support_grants",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_support_grants_tenant_id_store_id",
                schema: "licensing",
                table: "support_grants",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ix_support_grants_tenant_requested_at",
                schema: "licensing",
                table: "support_grants",
                columns: new[] { "tenant_id", "requested_at" });

            migrationBuilder.CreateIndex(
                name: "ux_support_grants_tenant_reference",
                schema: "licensing",
                table: "support_grants",
                columns: new[] { "tenant_id", "grant_reference" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_tamper_flags_sync_state",
                schema: "licensing",
                table: "tamper_flags",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_tamper_flags_tenant_id",
                schema: "licensing",
                table: "tamper_flags",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_tamper_flags_tenant_id_store_id",
                schema: "licensing",
                table: "tamper_flags",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tamper_flags_unreported",
                schema: "licensing",
                table: "tamper_flags",
                columns: new[] { "tenant_id", "detected_at" },
                filter: "reported_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "activations",
                schema: "licensing");

            migrationBuilder.DropTable(
                name: "clock_watermarks",
                schema: "licensing");

            migrationBuilder.DropTable(
                name: "emergency_unlocks",
                schema: "licensing");

            migrationBuilder.DropTable(
                name: "leases",
                schema: "licensing");

            migrationBuilder.DropTable(
                name: "licences",
                schema: "licensing");

            migrationBuilder.DropTable(
                name: "metering_records",
                schema: "licensing");

            migrationBuilder.DropTable(
                name: "support_grants",
                schema: "licensing");

            migrationBuilder.DropTable(
                name: "tamper_flags",
                schema: "licensing");
        }
    }
}
