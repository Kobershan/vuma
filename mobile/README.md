# Vuma Flutter client

One Flutter client targets Android, iOS and Windows and uses the Vuma API with the shared green,
Apple-inspired visual direction.

```bash
flutter pub get
flutter run --dart-define=VUMA_API_BASE_URL=https://server.example.com/api/v1
flutter build apk --release --dart-define=VUMA_API_BASE_URL=https://server.example.com/api/v1
flutter build windows --release --dart-define=VUMA_API_BASE_URL=https://server.example.com/api/v1
```

Email is not used. WhatsApp notifications remain server-side through Twilio. Production sign-in and
tenant selection use the existing identity/session APIs; unauthenticated API responses are surfaced
as an error card rather than exposing local data.
