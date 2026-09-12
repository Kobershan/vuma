# Vuma deployment topology

## Server

Install PostgreSQL 16+, the Vuma StoreServer service, and a reverse proxy/TLS certificate for the
public cloud endpoint. Configure `ConnectionStrings:Registry` for the registry database and a company
connection for the local company database. The registry stores company metadata and database routing;
each company gets a separate PostgreSQL database, so tenant data is not shared between company
databases. Run the EF migrations through the StoreServer provisioning/migration service.

Configure secrets through the server secret store/environment, never in `appsettings.json`:

- production JWT signing key and licensing public key;
- PostgreSQL credentials and backup encryption key;
- Twilio Account SID, Auth Token, approved WhatsApp sender, and the exact HTTPS webhook URL;
- cloud sync credentials and object-storage credentials.

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

The StoreServer is the local offline-first boundary. Its outbox synchronises approved data to the
cloud API; conflicts remain in the existing review queue. Use the Windows package job to produce the
WPF `.exe`; the Linux development environment cannot produce a valid WPF Windows binary.

## Mobile devices

Install the Flutter APK on Android or the signed Flutter IPA through Apple deployment on iOS. Build
with `VUMA_API_BASE_URL` set to the company cloud/API URL. Mobile devices use HTTPS API sessions and
never receive PostgreSQL credentials. Android APK and Windows Flutter builds are reproducible from
the `mobile/` project; iOS requires a macOS/Xcode signing runner.

## Cloud

Run CloudApi behind HTTPS with a registry database and one isolated PostgreSQL database per company.
Provisioning creates the company registry row, allocates its database/secret, applies the company
migrations, and records progress before the company is enabled. Backups must cover the registry and
every company database, with encryption keys stored separately. A company deletion/deactivation must
revoke access in the registry before any database operation.

The dashboard, desktop client and Flutter client all call the API. The database connection is chosen
server-side from authenticated tenant/company context; a client-supplied company id is never trusted
as a database selector.
