# TASK-25-004 — Employee document metadata

Stores tenant-scoped metadata for employee documents while keeping document bytes in external storage.
The record is append-only, references the employee by ID, and requires a normalized SHA-256 checksum.

Verification: `EmployeeDocumentTests` passes 4/4 and the `Stage25EmployeeDocuments` EF migration was
generated on 2026-09-13. The HR document API routes are covered by the host build; retention/deletion,
secure download authorization, and full PostgreSQL document acceptance remain. The real OpenAPI
contract test `Employee_document_routes_reach_the_openapi_document` passes 1/1.
