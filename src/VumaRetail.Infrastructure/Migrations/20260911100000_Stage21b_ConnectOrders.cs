using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations;

/// <summary>Creates the connection-scoped order and dispatch records introduced by Stage 21b.</summary>
public partial class Stage21b_ConnectOrders : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS connect.orders (
    id uuid NOT NULL PRIMARY KEY, tenant_id uuid NOT NULL, store_id uuid NULL, company_id uuid NOT NULL,
    created_at timestamptz NOT NULL, created_by varchar(128) NOT NULL, updated_at timestamptz NOT NULL,
    updated_by varchar(128) NOT NULL, row_version bytea NOT NULL, sync_state varchar(32) NOT NULL,
    sync_stamp varchar(128) NOT NULL, deleted_at timestamptz NULL, deleted_by varchar(128) NULL,
    retailer_tenant_id uuid NOT NULL, supplier_tenant_id uuid NOT NULL, connection_id uuid NOT NULL,
    purchase_order_id uuid NOT NULL, order_number varchar(64) NOT NULL, status varchar(24) NOT NULL,
    submitted_at timestamptz NOT NULL, promised_at timestamptz NULL, dispatched_at timestamptz NULL,
    rejection_reason varchar(500) NULL, dispatch_note_number varchar(64) NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_connect_orders_tenant_order_number
    ON connect.orders(tenant_id, order_number) WHERE deleted_at IS NULL;
CREATE INDEX IF NOT EXISTS ix_connect_orders_connection_status
    ON connect.orders(connection_id, status) WHERE deleted_at IS NULL;

CREATE TABLE IF NOT EXISTS connect.order_lines (
    id uuid NOT NULL PRIMARY KEY, tenant_id uuid NOT NULL, store_id uuid NULL, company_id uuid NOT NULL,
    created_at timestamptz NOT NULL, created_by varchar(128) NOT NULL, updated_at timestamptz NOT NULL,
    updated_by varchar(128) NOT NULL, row_version bytea NOT NULL, sync_state varchar(32) NOT NULL,
    sync_stamp varchar(128) NOT NULL, deleted_at timestamptz NULL, deleted_by varchar(128) NULL,
    order_id uuid NOT NULL, supplier_sku varchar(64) NOT NULL, description varchar(256) NOT NULL,
    requested_quantity_value numeric(19,6) NOT NULL, requested_quantity_uom varchar(16) NOT NULL,
    confirmed_quantity_value numeric(19,6) NOT NULL, confirmed_quantity_uom varchar(16) NOT NULL,
    dispatched_quantity_value numeric(19,6) NOT NULL, dispatched_quantity_uom varchar(16) NOT NULL,
    unit_price_amount numeric(19,4) NOT NULL, unit_price_currency char(3) NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_connect_order_lines_tenant_order_sku
    ON connect.order_lines(tenant_id, order_id, supplier_sku) WHERE deleted_at IS NULL;
ALTER TABLE connect.order_lines
    ADD CONSTRAINT fk_connect_order_lines_order
    FOREIGN KEY (order_id) REFERENCES connect.orders(id) ON DELETE RESTRICT;
");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE IF EXISTS connect.order_lines; DROP TABLE IF EXISTS connect.orders;");
    }
}
