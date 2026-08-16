using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Imports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "imports");

            migrationBuilder.CreateTable(
                name: "import_batches",
                schema: "imports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    target_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source_format = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    file_name = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    content_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    duplicate_strategy = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    worksheet = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    committed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    committed_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    rolled_back_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rolled_back_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    rollback_reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    total_rows = table.Column<int>(type: "integer", nullable: false),
                    valid_rows = table.Column<int>(type: "integer", nullable: false),
                    invalid_rows = table.Column<int>(type: "integer", nullable: false),
                    created_rows = table.Column<int>(type: "integer", nullable: false),
                    updated_rows = table.Column<int>(type: "integer", nullable: false),
                    skipped_rows = table.Column<int>(type: "integer", nullable: false),
                    source_columns = table.Column<string>(type: "jsonb", nullable: false),
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
                    table.PrimaryKey("pk_import_batches", x => x.id);
                    table.CheckConstraint("ck_import_batches_counts_non_negative", "total_rows >= 0 AND valid_rows >= 0 AND invalid_rows >= 0 AND created_rows >= 0 AND updated_rows >= 0 AND skipped_rows >= 0");
                });

            migrationBuilder.CreateTable(
                name: "import_mapping_templates",
                schema: "imports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    target_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source_signature = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    times_used = table.Column<int>(type: "integer", nullable: false),
                    bindings = table.Column<string>(type: "jsonb", nullable: false),
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
                    table.PrimaryKey("pk_import_mapping_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "import_column_mappings",
                schema: "imports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    import_batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_field = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_column = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    default_value = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
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
                    table.PrimaryKey("pk_import_column_mappings", x => x.id);
                    table.CheckConstraint("ck_import_column_mappings_binds_something", "source_column IS NOT NULL OR default_value IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_import_column_mappings_import_batches_import_batch_id",
                        column: x => x.import_batch_id,
                        principalSchema: "imports",
                        principalTable: "import_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "import_rows",
                schema: "imports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    import_batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    row_number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    target_entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    compensation_entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    before_image = table.Column<string>(type: "text", nullable: true),
                    raw_values = table.Column<string>(type: "jsonb", nullable: false),
                    normalised_values = table.Column<string>(type: "jsonb", nullable: false),
                    errors = table.Column<string>(type: "jsonb", nullable: false),
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
                    table.PrimaryKey("pk_import_rows", x => x.id);
                    table.CheckConstraint("ck_import_rows_row_number_after_header", "row_number >= 2");
                    table.ForeignKey(
                        name: "fk_import_rows_import_batches_import_batch_id",
                        column: x => x.import_batch_id,
                        principalSchema: "imports",
                        principalTable: "import_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_import_batches_sync_state",
                schema: "imports",
                table: "import_batches",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_import_batches_tenant_id",
                schema: "imports",
                table: "import_batches",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_import_batches_tenant_id_content_hash_committed",
                schema: "imports",
                table: "import_batches",
                columns: new[] { "tenant_id", "content_hash" },
                filter: "status = 'Committed' AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_import_batches_tenant_id_store_id",
                schema: "imports",
                table: "import_batches",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ix_import_batches_tenant_id_target_kind_batch_number",
                schema: "imports",
                table: "import_batches",
                columns: new[] { "tenant_id", "target_kind", "batch_number" });

            migrationBuilder.CreateIndex(
                name: "ux_import_batches_tenant_id_batch_number",
                schema: "imports",
                table: "import_batches",
                columns: new[] { "tenant_id", "batch_number" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_import_column_mappings_sync_state",
                schema: "imports",
                table: "import_column_mappings",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_import_column_mappings_tenant_id",
                schema: "imports",
                table: "import_column_mappings",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_import_column_mappings_tenant_id_store_id",
                schema: "imports",
                table: "import_column_mappings",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ux_import_column_mappings_batch_id_target_field",
                schema: "imports",
                table: "import_column_mappings",
                columns: new[] { "import_batch_id", "target_field" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_import_mapping_templates_sync_state",
                schema: "imports",
                table: "import_mapping_templates",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_import_mapping_templates_tenant_id",
                schema: "imports",
                table: "import_mapping_templates",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_import_mapping_templates_tenant_id_store_id",
                schema: "imports",
                table: "import_mapping_templates",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ux_import_mapping_templates_signature_active",
                schema: "imports",
                table: "import_mapping_templates",
                columns: new[] { "tenant_id", "target_kind", "source_signature" },
                unique: true,
                filter: "is_active = true AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_import_mapping_templates_tenant_id_code",
                schema: "imports",
                table: "import_mapping_templates",
                columns: new[] { "tenant_id", "code" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_import_rows_import_batch_id_status_row_number",
                schema: "imports",
                table: "import_rows",
                columns: new[] { "import_batch_id", "status", "row_number" });

            migrationBuilder.CreateIndex(
                name: "ix_import_rows_sync_state",
                schema: "imports",
                table: "import_rows",
                column: "sync_state",
                filter: "sync_state <> 'Synced'");

            migrationBuilder.CreateIndex(
                name: "ix_import_rows_target_entity_id",
                schema: "imports",
                table: "import_rows",
                column: "target_entity_id",
                filter: "target_entity_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_import_rows_tenant_id",
                schema: "imports",
                table: "import_rows",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_import_rows_tenant_id_store_id",
                schema: "imports",
                table: "import_rows",
                columns: new[] { "tenant_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ux_import_rows_import_batch_id_row_number",
                schema: "imports",
                table: "import_rows",
                columns: new[] { "import_batch_id", "row_number" },
                unique: true,
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "import_column_mappings",
                schema: "imports");

            migrationBuilder.DropTable(
                name: "import_mapping_templates",
                schema: "imports");

            migrationBuilder.DropTable(
                name: "import_rows",
                schema: "imports");

            migrationBuilder.DropTable(
                name: "import_batches",
                schema: "imports");
        }
    }
}
