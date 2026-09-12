# Vuma → Render migration

## Target architecture

```text
Mobile / desktop clients
          │ HTTPS + JWT
          ▼
Render Web Service: vuma-cloud-api
          │ private Render connection
          ▼
Render Postgres: vuma-postgres
  ├── vuma_registry  (identity, tenants, companies, access)
  └── vuma_cloud     (business data and cloud replica)
```

Clients must never receive a PostgreSQL URL. Render provides an internal database URL for services
in the same region; use that URL for the CloudApi. External URLs are for migration tools and local
administration only.

## 1. Create the Render resources

1. Push this repository to GitHub.
2. In Render, choose **New → Blueprint**, select the repository and branch `main`.
3. Render reads the root `render.yaml` and creates `vuma-postgres` and `vuma-cloud-api`.
4. Keep both resources in the same region.
5. After the database is created, open its **Connect** menu and copy the internal connection URL.

The Blueprint creates the default `vuma_cloud` database. Create the registry database once using the
external URL from Render's Connect menu:

```bash
export RENDER_DATABASE_URL='postgresql://...'
psql "$RENDER_DATABASE_URL" -d postgres -c 'CREATE DATABASE vuma_registry OWNER vuma;'
```

If the database already exists, PostgreSQL will report that it exists; that is safe.

## 2. Configure the API secrets in Render

Set these service environment variables in `vuma-cloud-api`:

```text
ConnectionStrings__Vuma=<internal Render URL with database name vuma_cloud>
ConnectionStrings__Registry=<internal Render URL with database name vuma_registry>
Vuma__Jwt__SigningKey=<random 64+ character secret>
Vuma__Backup__Encryption__Key=<a stable base64 key kept in your secret manager>
```

Do not commit those values. The production API deliberately refuses to start with the development
JWT signing key.

## 3. Export the local databases

Use `pg_dump` from the machine that can reach the current PostgreSQL server. Custom format preserves
the schema, data, indexes and sequences and lets the restore stop safely on errors.

```bash
export LOCAL_REGISTRY_URL='postgresql://USER:PASSWORD@HOST:5432/vuma_registry'
export LOCAL_VUMA_URL='postgresql://USER:PASSWORD@HOST:5432/vuma_cloud'

pg_dump --format=custom --no-owner --no-privileges \
  --dbname="$LOCAL_REGISTRY_URL" --file=vuma-registry.dump
pg_dump --format=custom --no-owner --no-privileges \
  --dbname="$LOCAL_VUMA_URL" --file=vuma-cloud.dump
```

If the local installation uses one database rather than the two-database cloud layout, do not blindly
restore it into both targets. First identify which schemas/tables belong to the registry and business
contexts; use the repository's migrations to create the target schemas, then import only the matching
data. The CloudApi wiring intentionally requires both databases.

## 4. Restore into Render

Use the Render **external** URL for the restore, because the migration command runs outside Render.
The external URL must include TLS (`sslmode=require`). Restore into each target database:

```bash
export RENDER_REGISTRY_URL='postgresql://.../vuma_registry?sslmode=require'
export RENDER_VUMA_URL='postgresql://.../vuma_cloud?sslmode=require'

pg_restore --no-owner --no-privileges --exit-on-error \
  --dbname="$RENDER_REGISTRY_URL" vuma-registry.dump
pg_restore --no-owner --no-privileges --exit-on-error \
  --dbname="$RENDER_VUMA_URL" vuma-cloud.dump
```

For a fresh empty target, `pg_restore` should not need `--clean`. Take a backup of the target before
repeating a restore against a non-empty database.

## 5. Run the application's migrations

After both connection strings are set in Render, open the CloudApi service shell and run:

```bash
dotnet VumaRetail.CloudApi.dll --migrate
```

Then redeploy/restart the service. Confirm:

```bash
curl -fsS https://YOUR-SERVICE.onrender.com/health
```

Expected response:

```json
{"status":"ok"}
```

## 6. Point the clients at Render

Use the public HTTPS service URL, never the database URL:

```bash
flutter build apk --release \
  --dart-define=VUMA_API_BASE_URL=https://YOUR-SERVICE.onrender.com/api/v1
```

For the desktop client, set `VUMA_API_BASE_URL` before first launch or sign in using the configured
default, then change it after authentication under **Settings**.

## Important free-tier limits

Treat the free Render setup as a test environment. Keep local backups before migration and do not
use the Render filesystem as the permanent backup vault. Move encrypted backups to durable object
storage before production.
