using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VumaRetail.Infrastructure.Persistence;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations;

/// <summary>Persists project rebate agreements; the model snapshot is maintained separately because the preceding fleet link migration is hand-authored.</summary>
[DbContext(typeof(VumaRetailDbContext))]
[Migration("20260920091137_Stage32RebateAgreements")]
public partial class Stage32RebateAgreements : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "rebate_agreements",
            schema: "projects",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                rate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                threshold_amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                threshold_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
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
            constraints: table => table.PrimaryKey("pk_rebate_agreements", x => x.id));

        migrationBuilder.CreateIndex("ix_rebate_agreements_tenant_id_company_id_number", "rebate_agreements", new[] { "tenant_id", "company_id", "number" }, "projects", unique: true, filter: "deleted_at IS NULL");
        migrationBuilder.CreateIndex("ix_rebate_agreements_tenant_id", "rebate_agreements", "tenant_id", "projects");
        migrationBuilder.CreateIndex("ix_rebate_agreements_tenant_id_company_id", "rebate_agreements", new[] { "tenant_id", "company_id" }, "projects");
        migrationBuilder.CreateIndex("ix_rebate_agreements_tenant_id_store_id", "rebate_agreements", new[] { "tenant_id", "store_id" }, "projects");
        migrationBuilder.CreateIndex("ix_rebate_agreements_sync_state", "rebate_agreements", "sync_state", "projects", filter: "sync_state <> 'Synced'");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "rebate_agreements", schema: "projects");
    }
}
