using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VumaRetail.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Stage21b_ConnectModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE SCHEMA IF NOT EXISTS connect;
CREATE TABLE IF NOT EXISTS connect.trading_connections (
 id uuid PRIMARY KEY, tenant_id uuid NOT NULL, store_id uuid NULL, company_id uuid NOT NULL,
 created_at timestamptz NOT NULL, created_by varchar(128) NOT NULL, updated_at timestamptz NOT NULL,
 updated_by varchar(128) NOT NULL, row_version bytea NOT NULL, sync_state varchar(32) NOT NULL,
 sync_stamp varchar(128) NOT NULL, deleted_at timestamptz NULL, deleted_by varchar(128) NULL,
 supplier_tenant_id uuid NOT NULL, retailer_tenant_id uuid NOT NULL, status varchar(16) NOT NULL,
 supplier_account_reference varchar(64) NULL, retailer_account_reference varchar(64) NULL,
 currency varchar(3) NOT NULL, credit_limit numeric(19,4) NOT NULL, lead_time_days integer NOT NULL,
 minimum_order_value numeric(19,4) NOT NULL, created_at_utc timestamptz NOT NULL,
 accepted_at_utc timestamptz NULL, suspended_at_utc timestamptz NULL, ended_at_utc timestamptz NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_connect_connections_party ON connect.trading_connections(tenant_id, supplier_tenant_id, retailer_tenant_id) WHERE deleted_at IS NULL;
CREATE TABLE IF NOT EXISTS connect.connection_codes (
 id uuid PRIMARY KEY, tenant_id uuid NOT NULL, store_id uuid NULL, company_id uuid NOT NULL,
 created_at timestamptz NOT NULL, created_by varchar(128) NOT NULL, updated_at timestamptz NOT NULL,
 updated_by varchar(128) NOT NULL, row_version bytea NOT NULL, sync_state varchar(32) NOT NULL,
 sync_stamp varchar(128) NOT NULL, deleted_at timestamptz NULL, deleted_by varchar(128) NULL,
 code varchar(64) NOT NULL, max_uses integer NOT NULL, redeemed_uses integer NOT NULL,
 expires_at timestamptz NOT NULL, price_tier varchar(64) NULL, territory varchar(64) NULL,
 grants_portal_access boolean NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_connect_codes_tenant_code ON connect.connection_codes(tenant_id, code) WHERE deleted_at IS NULL;
CREATE TABLE IF NOT EXISTS connect.catalogue_publications (
 id uuid PRIMARY KEY, tenant_id uuid NOT NULL, store_id uuid NULL, company_id uuid NOT NULL,
 created_at timestamptz NOT NULL, created_by varchar(128) NOT NULL, updated_at timestamptz NOT NULL,
 updated_by varchar(128) NOT NULL, row_version bytea NOT NULL, sync_state varchar(32) NOT NULL,
 sync_stamp varchar(128) NOT NULL, deleted_at timestamptz NULL, deleted_by varchar(128) NULL,
 connection_id uuid NOT NULL, version integer NOT NULL, effective_from timestamptz NOT NULL,
 version_note varchar(1000) NULL, rolled_back_at timestamptz NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_connect_catalogue_version ON connect.catalogue_publications(tenant_id, connection_id, version) WHERE deleted_at IS NULL;
CREATE TABLE IF NOT EXISTS connect.catalogue_publication_lines (
 id uuid PRIMARY KEY, tenant_id uuid NOT NULL, store_id uuid NULL, company_id uuid NOT NULL,
 created_at timestamptz NOT NULL, created_by varchar(128) NOT NULL, updated_at timestamptz NOT NULL,
 updated_by varchar(128) NOT NULL, row_version bytea NOT NULL, sync_state varchar(32) NOT NULL,
 sync_stamp varchar(128) NOT NULL, deleted_at timestamptz NULL, deleted_by varchar(128) NULL,
 publication_id uuid NOT NULL, supplier_sku varchar(64) NOT NULL, description varchar(256) NOT NULL,
 barcode varchar(64) NULL, pack_size integer NOT NULL, minimum_order_quantity numeric(19,4) NULL,
 lead_time_days integer NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_connect_catalogue_line_sku ON connect.catalogue_publication_lines(tenant_id, publication_id, supplier_sku) WHERE deleted_at IS NULL;
ALTER TABLE connect.catalogue_publication_lines ADD CONSTRAINT fk_connect_catalogue_lines_publication FOREIGN KEY (publication_id) REFERENCES connect.catalogue_publications(id) ON DELETE RESTRICT;
CREATE TABLE IF NOT EXISTS connect.price_proposals (
 id uuid PRIMARY KEY, tenant_id uuid NOT NULL, store_id uuid NULL, company_id uuid NOT NULL,
 created_at timestamptz NOT NULL, created_by varchar(128) NOT NULL, updated_at timestamptz NOT NULL,
 updated_by varchar(128) NOT NULL, row_version bytea NOT NULL, sync_state varchar(32) NOT NULL,
 sync_stamp varchar(128) NOT NULL, deleted_at timestamptz NULL, deleted_by varchar(128) NULL,
 connection_id uuid NOT NULL, effective_from timestamptz NOT NULL, expires_at timestamptz NULL,
 version_note varchar(1000) NULL, status varchar(24) NOT NULL, decided_at timestamptz NULL
);
CREATE INDEX IF NOT EXISTS ix_connect_price_proposals_connection_date ON connect.price_proposals(tenant_id, connection_id, effective_from) WHERE deleted_at IS NULL;
CREATE TABLE IF NOT EXISTS connect.price_proposal_lines (
 id uuid PRIMARY KEY, tenant_id uuid NOT NULL, store_id uuid NULL, company_id uuid NOT NULL,
 created_at timestamptz NOT NULL, created_by varchar(128) NOT NULL, updated_at timestamptz NOT NULL,
 updated_by varchar(128) NOT NULL, row_version bytea NOT NULL, sync_state varchar(32) NOT NULL,
 sync_stamp varchar(128) NOT NULL, deleted_at timestamptz NULL, deleted_by varchar(128) NULL,
 proposal_id uuid NOT NULL, supplier_sku varchar(64) NOT NULL, unit_price numeric(19,4) NOT NULL,
 currency varchar(3) NOT NULL, minimum_order_quantity numeric(19,4) NULL, lead_time_days integer NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_connect_price_line_sku ON connect.price_proposal_lines(tenant_id, proposal_id, supplier_sku) WHERE deleted_at IS NULL;
ALTER TABLE connect.price_proposal_lines ADD CONSTRAINT fk_connect_price_lines_proposal FOREIGN KEY (proposal_id) REFERENCES connect.price_proposals(id) ON DELETE RESTRICT;
CREATE TABLE IF NOT EXISTS connect.orders (
 id uuid PRIMARY KEY, tenant_id uuid NOT NULL, store_id uuid NULL, company_id uuid NOT NULL,
 created_at timestamptz NOT NULL, created_by varchar(128) NOT NULL, updated_at timestamptz NOT NULL,
 updated_by varchar(128) NOT NULL, row_version bytea NOT NULL, sync_state varchar(32) NOT NULL,
 sync_stamp varchar(128) NOT NULL, deleted_at timestamptz NULL, deleted_by varchar(128) NULL,
 retailer_tenant_id uuid NOT NULL, supplier_tenant_id uuid NOT NULL, connection_id uuid NOT NULL,
 purchase_order_id uuid NOT NULL, order_number varchar(64) NOT NULL, status varchar(24) NOT NULL,
 submitted_at timestamptz NOT NULL, promised_at timestamptz NULL, dispatched_at timestamptz NULL,
 rejection_reason varchar(500) NULL, dispatch_note_number varchar(64) NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_connect_orders_tenant_order_number ON connect.orders(tenant_id, order_number) WHERE deleted_at IS NULL;
CREATE TABLE IF NOT EXISTS connect.order_lines (
 id uuid PRIMARY KEY, tenant_id uuid NOT NULL, store_id uuid NULL, company_id uuid NOT NULL,
 created_at timestamptz NOT NULL, created_by varchar(128) NOT NULL, updated_at timestamptz NOT NULL,
 updated_by varchar(128) NOT NULL, row_version bytea NOT NULL, sync_state varchar(32) NOT NULL,
 sync_stamp varchar(128) NOT NULL, deleted_at timestamptz NULL, deleted_by varchar(128) NULL,
 order_id uuid NOT NULL, supplier_sku varchar(64) NOT NULL, description varchar(256) NOT NULL,
 requested_quantity_value numeric(19,6) NOT NULL, requested_quantity_uom varchar(16) NOT NULL,
 confirmed_quantity_value numeric(19,6) NOT NULL, confirmed_quantity_uom varchar(16) NOT NULL,
 dispatched_quantity_value numeric(19,6) NOT NULL, dispatched_quantity_uom varchar(16) NOT NULL,
 unit_price_amount numeric(19,4) NOT NULL, unit_price_currency varchar(3) NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_connect_order_lines_sku ON connect.order_lines(tenant_id, order_id, supplier_sku) WHERE deleted_at IS NULL;
ALTER TABLE connect.order_lines ADD CONSTRAINT fk_connect_order_lines_order FOREIGN KEY (order_id) REFERENCES connect.orders(id) ON DELETE RESTRICT;
CREATE TABLE IF NOT EXISTS connect.remittance_advices (
 id uuid PRIMARY KEY, tenant_id uuid NOT NULL, store_id uuid NULL, company_id uuid NOT NULL,
 created_at timestamptz NOT NULL, created_by varchar(128) NOT NULL, updated_at timestamptz NOT NULL,
 updated_by varchar(128) NOT NULL, row_version bytea NOT NULL, sync_state varchar(32) NOT NULL,
 sync_stamp varchar(128) NOT NULL, deleted_at timestamptz NULL, deleted_by varchar(128) NULL,
 retailer_tenant_id uuid NOT NULL, supplier_tenant_id uuid NOT NULL, connection_id uuid NOT NULL,
 payment_id uuid NOT NULL, invoice_reference varchar(128) NOT NULL, amount_amount numeric(19,4) NOT NULL,
 amount_currency varchar(3) NOT NULL, method varchar(24) NOT NULL, provider_reference varchar(128) NOT NULL,
 remittance_reference varchar(128) NOT NULL, status varchar(16) NOT NULL, issued_at timestamptz NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_connect_remittance_payment ON connect.remittance_advices(tenant_id, payment_id) WHERE deleted_at IS NULL;
CREATE TABLE IF NOT EXISTS connect.delivery_claims (
 id uuid PRIMARY KEY, tenant_id uuid NOT NULL, store_id uuid NULL, company_id uuid NOT NULL,
 created_at timestamptz NOT NULL, created_by varchar(128) NOT NULL, updated_at timestamptz NOT NULL,
 updated_by varchar(128) NOT NULL, row_version bytea NOT NULL, sync_state varchar(32) NOT NULL,
 sync_stamp varchar(128) NOT NULL, deleted_at timestamptz NULL, deleted_by varchar(128) NULL,
 retailer_tenant_id uuid NOT NULL, supplier_tenant_id uuid NOT NULL, connection_id uuid NOT NULL,
 order_id uuid NOT NULL, order_line_id uuid NOT NULL, claim_number varchar(64) NOT NULL,
 reason varchar(24) NOT NULL, quantity_value numeric(19,6) NOT NULL, quantity_uom varchar(16) NOT NULL,
 amount_amount numeric(19,4) NOT NULL, amount_currency varchar(3) NOT NULL, description varchar(1000) NOT NULL,
 status varchar(16) NOT NULL, credit_note_reference varchar(128) NULL, raised_at timestamptz NOT NULL,
 resolved_at timestamptz NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_connect_claim_number ON connect.delivery_claims(tenant_id, claim_number) WHERE deleted_at IS NULL;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS connect.delivery_claims; DROP TABLE IF EXISTS connect.remittance_advices; DROP TABLE IF EXISTS connect.order_lines; DROP TABLE IF EXISTS connect.orders; DROP TABLE IF EXISTS connect.price_proposal_lines; DROP TABLE IF EXISTS connect.price_proposals; DROP TABLE IF EXISTS connect.catalogue_publication_lines; DROP TABLE IF EXISTS connect.catalogue_publications; DROP TABLE IF EXISTS connect.connection_codes; DROP TABLE IF EXISTS connect.trading_connections;");
        }
    }
}
