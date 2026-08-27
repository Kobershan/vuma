using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VumaRetail.Infrastructure.Persistence;

namespace VumaRetail.Infrastructure.RegistryMigrations;

/// <summary>Creates the first registry table. Registry migrations are independent of company migrations.</summary>
[DbContext(typeof(VumaRegistryDbContext))]
[Migration("20260827192000_RegistryCompanies")]
public partial class RegistryCompanies : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "registry");

        migrationBuilder.CreateTable(
            name: "companies",
            schema: "registry",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                legal_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                trading_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                registration_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                tax_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                base_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                locale = table.Column<string>(type: "character varying(35)", maxLength: 35, nullable: false),
                document_prefix = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                connection_secret_ref = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                schema_version = table.Column<long>(type: "bigint", nullable: false),
                migration_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                lifecycle_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                is_active = table.Column<bool>(type: "boolean", nullable: false),
            },
            constraints: table => table.PrimaryKey("pk_companies", x => x.id));

        migrationBuilder.CreateIndex("ix_companies_tenant_id_code_unique", "companies", new[] { "tenant_id", "code" }, unique: true, schema: "registry");
        migrationBuilder.CreateIndex("ix_companies_tenant_id_document_prefix", "companies", new[] { "tenant_id", "document_prefix" }, unique: true, schema: "registry");
        migrationBuilder.CreateIndex("ix_companies_tenant_id_is_active", "companies", new[] { "tenant_id", "is_active" }, schema: "registry");

        migrationBuilder.CreateTable("company_groups", table => new
        {
            id = table.Column<Guid>(type: "uuid", nullable: false), tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
            name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
        }, constraints: table => table.PrimaryKey("pk_company_groups", x => x.id), schema: "registry");
        migrationBuilder.CreateIndex("ix_company_groups_tenant_id_name", "company_groups", new[] { "tenant_id", "name" }, unique: true, schema: "registry");
        migrationBuilder.CreateTable("company_group_members", table => new
        {
            group_id = table.Column<Guid>(type: "uuid", nullable: false), company_id = table.Column<Guid>(type: "uuid", nullable: false)
        }, constraints: table => table.PrimaryKey("pk_company_group_members", x => new { x.group_id, x.company_id }), schema: "registry");
        migrationBuilder.CreateTable("saga_intents", table => new
        {
            id = table.Column<Guid>(type: "uuid", nullable: false), tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
            type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false), idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
            payload = table.Column<string>(type: "text", nullable: false), state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false), created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false), expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true), owner = table.Column<string>(type: "text", nullable: true)
        }, constraints: table => table.PrimaryKey("pk_saga_intents", x => x.id), schema: "registry");
        migrationBuilder.CreateIndex("ix_saga_intents_tenant_id_idempotency_key", "saga_intents", new[] { "tenant_id", "idempotency_key" }, unique: true, schema: "registry");
        migrationBuilder.CreateTable("saga_legs", table => new
        {
            intent_id = table.Column<Guid>(type: "uuid", nullable: false), leg_id = table.Column<Guid>(type: "uuid", nullable: false), company_id = table.Column<Guid>(type: "uuid", nullable: false), state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false), attempts = table.Column<int>(type: "integer", nullable: false), acknowledged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true), timed_out_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true), last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
        }, constraints: table => table.PrimaryKey("pk_saga_legs", x => new { x.intent_id, x.leg_id }), schema: "registry");
        migrationBuilder.CreateTable("outbox", table => new
        {
            id = table.Column<Guid>(type: "uuid", nullable: false), tenant_id = table.Column<Guid>(type: "uuid", nullable: false), type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false), payload = table.Column<string>(type: "text", nullable: false), created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false), dispatched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true), attempts = table.Column<int>(type: "integer", nullable: false)
        }, constraints: table => table.PrimaryKey("pk_outbox", x => x.id), schema: "registry");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "outbox", schema: "registry"); migrationBuilder.DropTable(name: "saga_legs", schema: "registry"); migrationBuilder.DropTable(name: "saga_intents", schema: "registry"); migrationBuilder.DropTable(name: "company_group_members", schema: "registry"); migrationBuilder.DropTable(name: "company_groups", schema: "registry"); migrationBuilder.DropTable(name: "companies", schema: "registry");
    }
}
