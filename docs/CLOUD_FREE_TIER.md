# Cloud free-tier setup

The initial hosted database target is Neon PostgreSQL. Neon’s current Free plan is suitable for
development and pilot tenants: it provides free projects with scale-to-zero compute, per-project
storage and compute limits, and no credit card requirement. Check the live [Neon pricing page](https://neon.com/pricing)
before provisioning production data because free-tier limits can change.

## Topology

Use two control-plane databases plus one database for each company:

| Database | Purpose |
| --- | --- |
| `vuma_registry` | Tenant/company directory, lifecycle, routing secret references, migration state |
| `vuma_cloud` | Cloud sync/roll-up data used by the Cloud API |
| `vuma_company_<code>` | Isolated company data; never exposed to clients |

The registry is the only place that maps a company to a secret reference. The API resolves that
reference server-side after authentication. Desktop and Flutter clients receive HTTPS API URLs,
not PostgreSQL URLs.

## Configuration

Set secrets through the host environment or a secret manager. Do not commit connection strings,
JWT keys, backup keys, or Twilio credentials.

```bash
ConnectionStrings__Registry='postgresql://.../vuma_registry?sslmode=require'
ConnectionStrings__Vuma='postgresql://.../vuma_cloud?sslmode=require'
Vuma__Jwt__SigningKey='replace-with-a-long-random-production-key'
Vuma__Backup__Encryption__Key='replace-with-a-separately-stored-key'
```

Run the Cloud API migration command once against the registry and cloud databases:

```bash
dotnet VumaRetail.CloudApi.dll --migrate
```

Company databases are provisioned and migrated through the existing company provisioning/migration
services. A company must not be enabled until its database migration reports `Current`.

## Neon provisioning checklist

1. Create a Neon account and a project for the development environment.
2. Create `vuma_registry` and `vuma_cloud` databases, or create separate Neon projects when strict
   project-level isolation is preferred.
3. Create one company database per company and store each connection string in the server secret
   store under an opaque reference such as `company/<company-id>/postgres`.
4. Set the two Cloud API connection strings above and run `--migrate`.
5. Register companies through the authenticated API; do not insert enabled companies directly into
   the database.
6. Provision each company database, run company migrations, verify the migration status, and only
   then enable its lifecycle state.
7. Monitor storage, compute-hours and network transfer. Neon documents free-plan limits and
   scale-to-zero behavior on its [pricing page](https://neon.com/pricing).

## Operational boundary

The free tier is for development and pilot use. Before production, add encrypted backups for both
control-plane databases and every company database, alert on suspended/failed migrations, rotate
credentials, and move the secret store outside the application host. A database-per-company plan
also means provisioning automation and backup retention must be treated as first-class operations,
not as a manual SQL step.
