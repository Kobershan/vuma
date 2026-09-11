using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations;

public partial class Stage21b_ConnectClaims : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS connect.delivery_claims (
    id uuid NOT NULL PRIMARY KEY, tenant_id uuid NOT NULL, store_id uuid NULL, company_id uuid NOT NULL,
    created_at timestamptz NOT NULL, created_by varchar(128) NOT NULL, updated_at timestamptz NOT NULL,
    updated_by varchar(128) NOT NULL, row_version bytea NOT NULL, sync_state varchar(32) NOT NULL,
    sync_stamp varchar(128) NOT NULL, deleted_at timestamptz NULL, deleted_by varchar(128) NULL,
    retailer_tenant_id uuid NOT NULL, supplier_tenant_id uuid NOT NULL, connection_id uuid NOT NULL,
    order_id uuid NOT NULL, order_line_id uuid NOT NULL, claim_number varchar(64) NOT NULL,
    reason varchar(24) NOT NULL, quantity_value numeric(19,6) NOT NULL, quantity_uom varchar(16) NOT NULL,
    amount_amount numeric(19,4) NOT NULL, amount_currency char(3) NOT NULL, description varchar(1000) NOT NULL,
    status varchar(16) NOT NULL, credit_note_reference varchar(128) NULL,
    raised_at timestamptz NOT NULL, resolved_at timestamptz NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_connect_claims_tenant_number
    ON connect.delivery_claims(tenant_id, claim_number) WHERE deleted_at IS NULL;
");
    }
    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.Sql("DROP TABLE IF EXISTS connect.delivery_claims;");
}
