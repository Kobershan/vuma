# TASK-25-004 — Employee document metadata

Stores tenant-scoped metadata for employee documents while keeping document bytes in external storage.
The record is append-only, references the employee by ID, and requires a normalized SHA-256 checksum.

Verification: `EmployeeDocumentTests` passes 6/6 and the `Stage25EmployeeDocuments` EF migration was
generated on 2026-09-13. The HR document API routes are covered by the host build; retention/deletion,
secure download authorization, and full PostgreSQL document acceptance remain. The real OpenAPI
contract test `Employee_document_routes_reach_the_openapi_document` passes 1/1.

2026-09-13: The document listing query now returns metadata-only DTOs and no longer exposes the
external storage blob key. Secure, expiring download authorization remains a separate follow-up.

2026-09-13: Added a 15-minute opaque HMAC-signed download grant endpoint. Expired document metadata
is refused, the signing key is configuration-only and requires at least 32 bytes, and the blob key
is not included in the grant payload. Storage-side grant validation and retention/deletion remain.
