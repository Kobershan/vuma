using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VumaRetail.Infrastructure.Persistence;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations;

/// <summary>Creates durable company-scoped payroll runs and immutable employee calculations.</summary>
[DbContext(typeof(VumaRetailDbContext))]
[Migration("20260920092044_Stage32PayrollRuns")]
public partial class Stage32PayrollRuns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(name: "payroll_runs", schema: "hr_management", columns: table => new
        {
            id = table.Column<Guid>(type: "uuid", nullable: false), from = table.Column<DateOnly>(type: "date", nullable: false), to = table.Column<DateOnly>(type: "date", nullable: false), currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false), request_id = table.Column<Guid>(type: "uuid", nullable: false), status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false), gross_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false), deduction_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false), net_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false), tenant_id = table.Column<Guid>(type: "uuid", nullable: false), store_id = table.Column<Guid>(type: "uuid", nullable: true), company_id = table.Column<Guid>(type: "uuid", nullable: false), created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false), created_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false), updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false), updated_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false), row_version = table.Column<byte[]>(type: "bytea", nullable: false), sync_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false), sync_stamp = table.Column<string>(type: "character varying(86)", maxLength: 86, nullable: false), deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true), deleted_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
        }, constraints: table => table.PrimaryKey("pk_payroll_runs", x => x.id));
        migrationBuilder.CreateTable(name: "payroll_lines", schema: "hr_management", columns: table => new
        {
            id = table.Column<Guid>(type: "uuid", nullable: false), payroll_run_id = table.Column<Guid>(type: "uuid", nullable: false), employee_id = table.Column<Guid>(type: "uuid", nullable: false), hours = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false), hourly_rate = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false), gross_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false), deduction_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false), net_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false), currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false), tenant_id = table.Column<Guid>(type: "uuid", nullable: false), store_id = table.Column<Guid>(type: "uuid", nullable: true), company_id = table.Column<Guid>(type: "uuid", nullable: false), created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false), created_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false), updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false), updated_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false), row_version = table.Column<byte[]>(type: "bytea", nullable: false), sync_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false), sync_stamp = table.Column<string>(type: "character varying(86)", maxLength: 86, nullable: false), deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true), deleted_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
        }, constraints: table => table.PrimaryKey("pk_payroll_lines", x => x.id));
        migrationBuilder.CreateIndex("ix_payroll_runs_tenant_id_company_id_request_id", "payroll_runs", new[] { "tenant_id", "company_id", "request_id" }, "hr_management", unique: true, filter: "deleted_at IS NULL");
        migrationBuilder.CreateIndex("ix_payroll_runs_tenant_id", "payroll_runs", "tenant_id", "hr_management");
        migrationBuilder.CreateIndex("ix_payroll_runs_tenant_id_company_id", "payroll_runs", new[] { "tenant_id", "company_id" }, "hr_management");
        migrationBuilder.CreateIndex("ix_payroll_runs_tenant_id_store_id", "payroll_runs", new[] { "tenant_id", "store_id" }, "hr_management");
        migrationBuilder.CreateIndex("ix_payroll_runs_sync_state", "payroll_runs", "sync_state", "hr_management", filter: "sync_state <> 'Synced'");
        migrationBuilder.CreateIndex("ix_payroll_lines_tenant_id", "payroll_lines", "tenant_id", "hr_management");
        migrationBuilder.CreateIndex("ix_payroll_lines_tenant_id_company_id", "payroll_lines", new[] { "tenant_id", "company_id" }, "hr_management");
        migrationBuilder.CreateIndex("ix_payroll_lines_tenant_id_store_id", "payroll_lines", new[] { "tenant_id", "store_id" }, "hr_management");
        migrationBuilder.CreateIndex("ix_payroll_lines_sync_state", "payroll_lines", "sync_state", "hr_management", filter: "sync_state <> 'Synced'");
        migrationBuilder.CreateIndex("ix_payroll_lines_tenant_id_payroll_run_id_employee_id", "payroll_lines", new[] { "tenant_id", "payroll_run_id", "employee_id" }, "hr_management", unique: true, filter: "deleted_at IS NULL");
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    { migrationBuilder.DropTable(name: "payroll_lines", schema: "hr_management"); migrationBuilder.DropTable(name: "payroll_runs", schema: "hr_management"); }
}
