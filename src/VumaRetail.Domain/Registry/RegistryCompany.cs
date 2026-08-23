using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Registry;

/// <summary>
/// One trading company in the tenant's registry — the row that says a company database exists, what it
/// is called, and where to find it (ADR-099, ADR-118).
/// </summary>
/// <remarks>
/// <para>
/// This is the registry's own address-book entry, not the company's licensing or trading identity — that
/// stays on <c>Tenant</c>, replicated into and read from the company's own database (ADR-139). Editing a
/// company's name here does not change what that company's own till prints on a receipt; the two are kept
/// in step by the company database re-publishing on change, not by a shared row.
/// </para>
/// <para>
/// Lives in <c>VumaRegistryDbContext</c>, not <c>VumaRetailDbContext</c> — there is no foreign key from
/// any company database into this table, and cannot be, because they are different databases (ADR-116).
/// </para>
/// <para>
/// Replicated <see cref="ReplicationScope.StoreToCloud"/>: the store provisions its own companies and
/// the cloud receives a copy for the tenant's roll-up (`docs/MULTI_COMPANY.md` §9). Only the store ever
/// writes one, so <see cref="ConflictPolicy.LastWriterWins"/> never actually has two writers to choose
/// between.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.LastWriterWins)]
public sealed class RegistryCompany : Entity
{
    private RegistryCompany(Guid tenantId, string code, string legalName, string tradingName)
        : base(tenantId)
    {
        Code = code;
        LegalName = legalName;
        TradingName = tradingName;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private RegistryCompany()
    {
    }

    /// <summary>The short code staff and documents use, unique within the tenant, for example <c>SH</c>.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>The registered legal entity name, as it appears on a tax invoice.</summary>
    public string LegalName { get; private set; } = string.Empty;

    /// <summary>The name customers see.</summary>
    public string TradingName { get; private set; } = string.Empty;

    /// <summary>The company's registration number, where it has one.</summary>
    public string? RegistrationNumber { get; private set; }

    /// <summary>The company's tax registration number, where it has one.</summary>
    public string? TaxRegistrationNumber { get; private set; }

    /// <summary>ISO 4217 code this company reports in.</summary>
    public string BaseCurrency { get; private set; } = string.Empty;

    /// <summary>BCP-47 culture used for formatting.</summary>
    public string Locale { get; private set; } = string.Empty;

    /// <summary>The prefix this company's documents are numbered under, for example <c>SH</c> → <c>SH-INV-000001</c>.</summary>
    public string DocumentPrefix { get; private set; } = string.Empty;

    /// <summary>
    /// The company database's connection details, AES-256-GCM encrypted (ADR-118, R10). Never appears in
    /// a log, a support export or a telemetry payload — only the connection secret protector reads it.
    /// </summary>
    public string ConnectionCiphertext { get; private set; } = string.Empty;

    /// <summary>Where this company stands in provisioning (ADR-118).</summary>
    public CompanyLifecycleState LifecycleState { get; private set; } = CompanyLifecycleState.Provisioning;

    /// <summary>
    /// True only once provisioning reached <see cref="CompanyLifecycleState.Active"/> and the company has
    /// not since been deactivated. The gate business operations actually check — <see cref="LifecycleState"/>
    /// alone is not enough, because deactivation does not roll it back (ADR-118: "makes a company
    /// read-only rather than removing it").
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>The provisioning step that failed, or <c>null</c> if the last attempt succeeded.</summary>
    public string? FailedStep { get; private set; }

    /// <summary>Why the last provisioning step failed, or <c>null</c>.</summary>
    public string? FailureReason { get; private set; }

    /// <summary>
    /// The migration identifier this company database was last confirmed to be at, or <c>null</c> before
    /// its first migration run.
    /// </summary>
    public string? SchemaVersion { get; private set; }

    /// <summary>Whether this company's schema matches what the running binary expects (ADR-117).</summary>
    public CompanyMigrationState MigrationState { get; private set; } = CompanyMigrationState.NotMigrated;

    /// <summary>True once <see cref="LifecycleState"/> is <see cref="CompanyLifecycleState.Active"/> and the company has not been deactivated — the actual visibility gate (ADR-118 rule "business operations see Active companies only").</summary>
    public bool IsVisibleToBusinessOperations => LifecycleState is CompanyLifecycleState.Active && IsActive;

    /// <summary>Begins provisioning a company. Starts at <see cref="CompanyLifecycleState.Provisioning"/>.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="code">The short code, upper-cased.</param>
    /// <param name="legalName">The registered legal entity name.</param>
    /// <param name="tradingName">The shopfront brand name.</param>
    /// <param name="baseCurrency">ISO 4217 code.</param>
    /// <param name="locale">BCP-47 culture.</param>
    /// <param name="documentPrefix">The document numbering prefix.</param>
    public static RegistryCompany BeginProvisioning(
        Guid tenantId,
        string code,
        string legalName,
        string tradingName,
        string baseCurrency,
        string locale,
        string documentPrefix)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A company must belong to a tenant.", nameof(tenantId));
        }

        return new RegistryCompany(
            tenantId,
            Require(code, nameof(code)).ToUpperInvariant(),
            Require(legalName, nameof(legalName)),
            Require(tradingName, nameof(tradingName)))
        {
            BaseCurrency = new Money(0m, baseCurrency).Currency,
            Locale = Require(locale, nameof(locale)),
            DocumentPrefix = Require(documentPrefix, nameof(documentPrefix)).ToUpperInvariant(),
        };
    }

    /// <summary>Records the encrypted connection details for the company's database.</summary>
    /// <param name="ciphertext">The AES-256-GCM ciphertext, base64.</param>
    public void SetConnection(string ciphertext)
        => ConnectionCiphertext = Require(ciphertext, nameof(ciphertext));

    /// <summary>Sets registration/tax numbers, where the company has them.</summary>
    /// <param name="registrationNumber">The company registration number, or <c>null</c>.</param>
    /// <param name="taxRegistrationNumber">The tax registration number, or <c>null</c>.</param>
    public void SetRegistrationNumbers(string? registrationNumber, string? taxRegistrationNumber)
    {
        RegistrationNumber = string.IsNullOrWhiteSpace(registrationNumber) ? null : registrationNumber.Trim();
        TaxRegistrationNumber = string.IsNullOrWhiteSpace(taxRegistrationNumber) ? null : taxRegistrationNumber.Trim();
    }

    /// <summary>
    /// Advances to the next lifecycle step, clearing any prior failure. Provisioning is re-runnable from
    /// wherever it stopped, so this never throws for advancing to the same or a later step twice.
    /// </summary>
    /// <param name="state">The step reached.</param>
    public void AdvanceTo(CompanyLifecycleState state)
    {
        if (state < LifecycleState)
        {
            throw new InvalidOperationException(
                $"Cannot move company {Code} backward from {LifecycleState} to {state}.");
        }

        LifecycleState = state;
        FailedStep = null;
        FailureReason = null;

        if (state is CompanyLifecycleState.Active)
        {
            IsActive = true;
        }
    }

    /// <summary>Records that a provisioning step failed, so the operation can be re-run from there.</summary>
    /// <param name="step">The step that failed, for example <c>"Seeding"</c>.</param>
    /// <param name="reason">Why it failed.</param>
    public void RecordFailure(string step, string reason)
    {
        FailedStep = Require(step, nameof(step));
        FailureReason = Require(reason, nameof(reason));
    }

    /// <summary>
    /// Makes an active company read-only rather than removing it (ADR-118). Business operations stop
    /// seeing it; its trading history, and the company database itself, are untouched.
    /// </summary>
    public void Deactivate()
    {
        if (LifecycleState is not CompanyLifecycleState.Active)
        {
            throw new InvalidOperationException(
                $"Company {Code} is not Active and cannot be deactivated (currently {LifecycleState}).");
        }

        IsActive = false;
    }

    /// <summary>Restores a deactivated company to business visibility.</summary>
    public void Reactivate()
    {
        if (LifecycleState is not CompanyLifecycleState.Active)
        {
            throw new InvalidOperationException(
                $"Company {Code} has not completed provisioning and cannot be reactivated (currently {LifecycleState}).");
        }

        IsActive = true;
    }

    /// <summary>Records the outcome of a migration fan-out run against this company database (ADR-117).</summary>
    /// <param name="schemaVersion">The migration identifier now applied.</param>
    /// <param name="state">Whether the company is now up to date or still behind.</param>
    public void RecordMigration(string schemaVersion, CompanyMigrationState state)
    {
        SchemaVersion = Require(schemaVersion, nameof(schemaVersion));
        MigrationState = state;
    }

    private static string Require(string value, string parameterName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{parameterName} is required.", parameterName)
            : value.Trim();
}
