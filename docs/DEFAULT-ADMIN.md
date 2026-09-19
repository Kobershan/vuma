# Bootstrap administrator

The development/demo seed creates one bootstrap administrator so a fresh installation can be
entered before staff accounts exist:

The installer generates a random bootstrap password, displays it once with a save warning, and passes
it through `VUMA_BOOTSTRAP_PASSWORD`. Use that one-time value with username `admin`.

Run `scripts/seed.sh` (or `scripts/seed.ps1` on Windows) against the configured StoreServer database.
The seed applies both company and registry migrations before inserting the account and demo roles.

Sign in with `admin`, open **Administration → Staff administration**, create the real administrator
and staff logins, then deactivate the bootstrap account. Deactivation is intentional: it ends access
and preserves the bootstrap account's audit/history row. Assign `Owner` only to trusted administrators;
use `Store Manager` or `Cashier` for ordinary staff.

The staff-management API is authenticated and permission-gated:

- `GET /api/v1/staff`
- `GET /api/v1/staff-roles`
- `POST /api/v1/staff`
- `POST /api/v1/staff/{userId}/roles`
- `POST /api/v1/staff/{userId}/deactivate`

The production bootstrap password must be changed or the bootstrap account deactivated immediately
after the first real administrator is created. Development-only seeds use a non-production placeholder.
