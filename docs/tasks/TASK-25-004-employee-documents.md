# TASK-25-004 — Employee document metadata

Stores tenant-scoped metadata for employee documents while keeping document bytes in external storage.
The record is append-only, references the employee by ID, and requires a normalized SHA-256 checksum.

Verification: `EmployeeDocumentTests` passes 7/7 and the `Stage25EmployeeDocuments` EF migration was
generated on 2026-09-13. The HR document API routes are covered by the host build; retention/deletion,
secure download authorization, and full PostgreSQL document acceptance remain. The real OpenAPI
contract test `Employee_document_routes_reach_the_openapi_document` passes 1/1.

2026-09-13: The document listing query now returns metadata-only DTOs and no longer exposes the
external storage blob key. Secure, expiring download authorization remains a separate follow-up.

2026-09-13: Added a 15-minute opaque HMAC-signed download grant endpoint and constant-time token
validation. Expired document metadata is refused, the signing key is configuration-only and requires
at least 32 bytes, and the blob key is not included in the grant payload. Storage adapter integration
and retention/deletion remain.

2026-09-14: Record, list and download-authorization handlers now explicitly validate the loaded
employee/document company against the active company; new document metadata is stamped with that
company before persistence. `EmployeeDocumentTests` passes 8/8, including cross-company refusal.
