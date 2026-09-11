using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations;

public partial class Stage21b_ConnectSettlement : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS connect.remittance_advices (
    id uuid NOT NULL PRIMARY KEY, tenant_id uuid NOT NULL, store_id uuid NULL, company_id uuid NOT NULL,
    created_at timestamptz NOT NULL, created_by varchar(128) NOT NULL, updated_at timestamptz NOT NULL,
    updated_by varchar(128) NOT NULL, row_version bytea NOT NULL, sync_state varchar(32) NOT NULL,
    sync_stamp varchar(128) NOT NULL, deleted_at timestamptz NULL, deleted_by varchar(128) NULL,
    retailer_tenant_id uuid NOT NULL, supplier_tenant_id uuid NOT NULL, connection_id uuid NOT NULL,
    payment_id uuid NOT NULL, invoice_reference varchar(128) NOT NULL,
    amount_amount numeric(19,4) NOT NULL, amount_currency char(3) NOT NULL,
    method varchar(24) NOT NULL, provider_reference varchar(128) NOT NULL,
    remittance_reference varchar(128) NOT NULL, status varchar(16) NOT NULL,
    issued_at timestamptz NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_connect_remittance_tenant_payment
    ON connect.remittance_advices(tenant_id, payment_id) WHERE deleted_at IS NULL;
");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.Sql("DROP TABLE IF EXISTS connect.remittance_advices;");
}
