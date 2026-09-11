# Vuma operations dashboard

This is the responsive dashboard shell for the Vuma back-office and mobile web experience. It consumes
the existing authenticated `/api/v1` surface and uses the generated `design/tokens.css` source of truth.
It deliberately does not store access tokens in browser storage: the host authentication boundary owns
the session and requests use same-origin credentials.

For local preview, serve the repository root with any static-file server and open `dashboard/index.html`.
The dashboard is responsive down to mobile width, supports light/dark themes, keyboard navigation, reduced
motion through the generated token system, and keeps status meaning in both text and colour.
