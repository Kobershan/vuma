# Vuma deployment topology

## Server

Install PostgreSQL 17+, the Vuma StoreServer service, and a reverse proxy/TLS certificate for the
public cloud endpoint. Configure `ConnectionStrings:Registry` for the registry database and a company
connection for the local company database. The registry stores company metadata and database routing;
each company gets a separate PostgreSQL database, so tenant data is not shared between company
databases. Run the EF migrations through the StoreServer provisioning/migration service.

Configure secrets through the server secret store/environment, never in `appsettings.json`:

- production JWT signing key and licensing public key;
- PostgreSQL credentials and backup encryption key;
- Twilio Account SID, Auth Token, approved WhatsApp sender, and the exact HTTPS webhook URL;
- cloud sync credentials and object-storage credentials.

For database-per-company routing, each registry `ConnectionSecretRef` must resolve through
`Vuma:CompanyConnections:{ref}` (or the equivalent `ConnectionStrings:{ref}` entry) in the host
secret provider. Missing references fail closed before a company context is opened; the ordinary
host database is never used as a fallback for company business data.

The Twilio webhook is `/api/v1/conversations/webhook/twilio/whatsapp`. Twilio-signed form requests
are rejected unless the HMAC-SHA1 signature validates against the full configured webhook URL and all
received form fields. Twilio requires HTTPS for production callbacks and signs requests using the
request URL plus sorted POST fields. See [Twilio request security](https://www.twilio.com/docs/usage/security)
and [the WhatsApp API](https://www.twilio.com/docs/whatsapp/api).

## PCs

Install the self-contained Windows Vuma `.exe` package, the required receipt-printer/scanner drivers,
and the terminal certificate issued by the server. The desktop client points at the local StoreServer
HTTPS URL. Use the self-contained single-file artifact from `artifacts/desktop-win-single`; do not
copy only a framework-dependent launcher without its companion files. PCs do not receive database
credentials and do not connect directly to PostgreSQL.

For the first login and administrator workflow, see [DEFAULT-ADMIN.md](DEFAULT-ADMIN.md).

The StoreServer is the local offline-first boundary. Its outbox synchronises approved data to the
cloud API; conflicts remain in the existing review queue. Use the Windows package job to produce the
WPF `.exe`; the Linux development environment cannot produce a valid WPF Windows binary.

## Mobile devices

Install the Flutter APK on Android or the signed Flutter IPA through Apple deployment on iOS. Build
with `VUMA_API_BASE_URL` set to the company cloud/API URL. Mobile devices use HTTPS API sessions and
never receive PostgreSQL credentials. Android APK and Windows Flutter builds are reproducible from
the `mobile/` project; iOS requires a macOS/Xcode signing runner.

The desktop and mobile clients follow the visual references in `docs/vuma-erp-dashboard.html` and
`docs/vuma-mobile-screens.html`: the desktop uses the group-control navigation and KPI/panel layout,
while the mobile client uses the Home, Transfers, Orders and More tab surfaces. Regenerate the shared
palette from `design/tokens.json` with `pwsh scripts/generate-tokens.ps1` before packaging clients.

## Cloud

Run CloudApi behind HTTPS with a registry database and one isolated PostgreSQL database per company.
Provisioning creates the company registry row, allocates its database/secret, applies the company
migrations, and records progress before the company is enabled. Backups must cover the registry and
every company database, with encryption keys stored separately. A company deletion/deactivation must
revoke access in the registry before any database operation.

The dashboard, desktop client and Flutter client all call the API. The database connection is chosen
server-side from authenticated tenant/company context; a client-supplied company id is never trusted
as a database selector.

## Control plane production gate

Run `VumaRetail.ControlPlane` as a separately protected service. Start it with an explicit persistent
`ConnectionStrings:ControlPlane` value and an HTTPS `ControlPlane:SignerEndpoint`; the process refuses
production startup when either is missing. Use `src/VumaRetail.ControlPlane/appsettings.Production.example.json`
as a shape-only template and inject the real values through the deployment secret store.

Vendor routes require mutual TLS. Map each certificate's SHA-256 thumbprint to its least-privileged
vendor role under `ControlPlane:VendorAuthorization:CertificateRoles`. The `X-Vendor-Role` header is
accepted only in Development and must not be used by a production client. Rotate certificates by
deploying the replacement thumbprint before revoking the old one, then remove the old mapping.

The control-plane database path must be on a persistent, backed-up volume. A container-local SQLite
file is not a production topology unless the volume, backup, restore and single-writer ownership are
explicitly provided. Live mTLS, signer, backup-restore and rollback acceptance must be executed in the
target deployment environment; repository tests verify the fail-closed configuration and authorization
contract only.
## TLS requirements for non-Render deployments

Render terminates TLS before forwarding HTTP to the cloud container. On a VPS or self-hosted Docker
deployment, put nginx, Caddy, an AWS ALB, or another TLS-terminating reverse proxy in front of port
10000. Never expose the container's plain HTTP port directly to the internet. Configure
`AllowedHosts` with the real public hostname(s), not `*` or the shipped placeholder.
