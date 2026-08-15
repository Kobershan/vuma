using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SyncAndBackup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "sync");

            migrationBuilder.EnsureSchema(
                name: "backup");

            migrationBuilder.AddColumn<string>(
                name: "sync_stamp",
                schema: "identity",
                table: "users",
                type: "character varying(86)",
                maxLength: 86,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "sync_stamp",
                schema: "identity",
                table: "user_role_assignments",
                type: "character varying(86)",
                maxLength: 86,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "sync_stamp",
                schema: "identity",
                table: "terminals",
                type: "character varying(86)",
                maxLength: 86,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "sync_stamp",
                schema: "platform",
                table: "tenants",
                type: "character varying(86)",
                maxLength: 86,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "sync_stamp",
                schema: "platform",
                table: "stores",
                type: "character varying(86)",
                maxLength: 86,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "sync_stamp",
                schema: "identity",
                table: "roles",
                type: "character varying(86)",
                maxLength: 86,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "sync_stamp",
                schema: "identity",
                table: "role_permissions",
                type: "character varying(86)",
                maxLength: 86,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "sync_stamp",
                schema: "identity",
                table: "refresh_tokens",
                type: "character varying(86)",
                maxLength: 86,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "sync_stamp",
                schema: "platform",
                table: "audit_entries",
                type: "character varying(86)",
                maxLength: 86,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "conflict_entries",
                schema: "sync",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_node = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    local_version = table.Column<string>(type: "jsonb", nullable: false),
                    remote_version = table.Column<string>(type: "jsonb", nullable: false),
                    local_stamp = table.Column<string>(type: "character varying(86)", maxLength: 86, nullable: false),
                    remote_stamp = table.Column<string>(type: "character varying(86)", maxLength: 86, nullable: false),
                    detected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolution = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    resolution_note = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
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
                    table.PrimaryKey("pk_conflict_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "inbox_messages",
                schema: "sync",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_node = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_stamp = table.Column<string>(type: "character varying(86)", maxLength: 86, nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    detail = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
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
                    table.PrimaryKey("pk_inbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "sync",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_node = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    scope = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    conflict_policy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    operation_stamp = table.Column<string>(type: "character varying(86)", maxLength: 86, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    dispatched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
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
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "snapshots",
                schema: "backup",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    source_node = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    object_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    checksum = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    restored_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
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
                    table.PrimaryKey("pk_snapshots", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sync_cursors",
                schema: "sync",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    peer_node = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    direction = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    acknowledged_stamp = table.Column<string>(type: "character varying(86)", maxLength: 86, nullable: false),
                    acknowledged_count = table.Column<long>(type: "bigint", nullable: false),
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
                    table.PrimaryKey("pk_sync_cursors", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_conflict_entries_open",
                schema: "sync",
                table: "conflict_entries",
                columns: new[] { "tenant_id", "detected_at" },
                filter: "resolution = 'Unresolved' AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_conflict_entries_sync_state",
                schema: "sync",
                table: "conflict_entries",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_conflict_entries_tenant_id",
                schema: "sync",
                table: "conflict_entries",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_conflict_entries_tenant_id_store_id",
                schema: "sync",
                table: "conflict_entries",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ix_inbox_messages_sync_state",
                schema: "sync",
                table: "inbox_messages",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_inbox_messages_tenant_id",
                schema: "sync",
                table: "inbox_messages",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_inbox_messages_tenant_id_store_id",
                schema: "sync",
                table: "inbox_messages",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ux_inbox_messages_tenant_source_operation",
                schema: "sync",
                table: "inbox_messages",
                columns: new[] { "tenant_id", "source_node", "operation_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_due",
                schema: "sync",
                table: "outbox_messages",
                columns: new[] { "next_attempt_at", "operation_stamp" },
                filter: "status <> 'Dispatched'");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_sync_state",
                schema: "sync",
                table: "outbox_messages",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_tenant_id",
                schema: "sync",
                table: "outbox_messages",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_tenant_id_store_id",
                schema: "sync",
                table: "outbox_messages",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ux_outbox_messages_source_node_operation_id",
                schema: "sync",
                table: "outbox_messages",
                columns: new[] { "source_node", "operation_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_snapshots_sync_state",
                schema: "backup",
                table: "snapshots",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_snapshots_tenant_id",
                schema: "backup",
                table: "snapshots",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_snapshots_tenant_id_store_id",
                schema: "backup",
                table: "snapshots",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ix_snapshots_tenant_started_at",
                schema: "backup",
                table: "snapshots",
                columns: new[] { "tenant_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ux_snapshots_object_key",
                schema: "backup",
                table: "snapshots",
                column: "object_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sync_cursors_sync_state",
                schema: "sync",
                table: "sync_cursors",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_sync_cursors_tenant_id",
                schema: "sync",
                table: "sync_cursors",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_sync_cursors_tenant_id_store_id",
                schema: "sync",
                table: "sync_cursors",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ux_sync_cursors_tenant_peer_direction",
                schema: "sync",
                table: "sync_cursors",
                columns: new[] { "tenant_id", "peer_node", "direction" },
                unique: true,
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "conflict_entries",
                schema: "sync");

            migrationBuilder.DropTable(
                name: "inbox_messages",
                schema: "sync");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "sync");

            migrationBuilder.DropTable(
                name: "snapshots",
                schema: "backup");

            migrationBuilder.DropTable(
                name: "sync_cursors",
                schema: "sync");

            migrationBuilder.DropColumn(
                name: "sync_stamp",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "sync_stamp",
                schema: "identity",
                table: "user_role_assignments");

            migrationBuilder.DropColumn(
                name: "sync_stamp",
                schema: "identity",
                table: "terminals");

            migrationBuilder.DropColumn(
                name: "sync_stamp",
                schema: "platform",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "sync_stamp",
                schema: "platform",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "sync_stamp",
                schema: "identity",
                table: "roles");

            migrationBuilder.DropColumn(
                name: "sync_stamp",
                schema: "identity",
                table: "role_permissions");

            migrationBuilder.DropColumn(
                name: "sync_stamp",
                schema: "identity",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "sync_stamp",
                schema: "platform",
                table: "audit_entries");
        }
    }
}
