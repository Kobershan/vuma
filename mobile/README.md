# Vuma Flutter client

One Flutter client targets Android, iOS and Windows and uses the Vuma API with the shared green,
Apple-inspired visual direction.

```bash
flutter pub get
flutter run --dart-define=VUMA_API_BASE_URL=https://server.example.com/api/v1
flutter build apk --release --dart-define=VUMA_API_BASE_URL=https://server.example.com/api/v1
flutter build windows --release --dart-define=VUMA_API_BASE_URL=https://server.example.com/api/v1
```

## Live PostgreSQL-backed testing

The app does not connect to PostgreSQL directly. It connects to the CloudApi or StoreServer over
HTTPS; that server authenticates the user, applies tenant permissions, and reads PostgreSQL. This
keeps database credentials out of the APK and prevents a mobile client from bypassing tenant
isolation.

Start the API with its `ConnectionStrings:Vuma` and `ConnectionStrings:Registry` values configured,
then build or run the app with the API URL:

```bash
flutter run --dart-define=VUMA_API_BASE_URL=https://api.example.com/api/v1
flutter build apk --release --dart-define=VUMA_API_BASE_URL=https://api.example.com/api/v1
```

On first launch, sign in with an API user. The dashboard then calls the live
`GET /api/v1/dashboard/overview` endpoint using the returned bearer token. For a same-network test,
use the server's LAN HTTPS address instead of `api.example.com`; do not expose PostgreSQL port 5432
to the phone.

Email is not used. WhatsApp notifications remain server-side through Twilio. Production sign-in and
tenant selection use the existing identity/session APIs; unauthenticated API responses are surfaced
as an error card rather than exposing local data.
