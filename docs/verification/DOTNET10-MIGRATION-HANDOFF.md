# .NET 10 migration handoff

Migration checkpoint: 2026-09-18

Completed statically:

- ADR-156 recorded and referenced from the locked stack in `CLAUDE.md`.
- All projects inherit `net10.0`; WPF desktop/gallery override to `net10.0-windows`.
- `VumaRetail.Hardware` remains plain `net10.0`.
- C# language version is pinned to 14.0.
- EF Core, Npgsql, ASP.NET Core, Extensions, SQLite and test SDK references were aligned to
  the verified .NET 10-compatible versions in `Directory.Packages.props`.
- SDK pin, CI setup-dotnet jobs, EF migration tool, and Cloud API container images use .NET 10.
- EF model safeguards, filters, no-tracking reads and raw PostgreSQL lock queries were reviewed
  statically in `DOTNET10-EF10-REVIEW.md`.
- Desktop and server CI publish paths remain self-contained `win-x64` artifacts.

Owner verification required at home (the first and only migration build checkpoint):

```text
dotnet build src/VumaRetail.sln -c Release
```

After that build, run the owner’s normal overnight test pass and EF migration/model checks. This
machine deliberately did not invoke any SDK command, build, restore, compilation or test.
