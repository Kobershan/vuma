using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Registry;

/// <summary>The lifecycle of a company known to the tenant registry.</summary>
public enum CompanyLifecycleState
{
    /// <summary>Database creation and migration have not completed.</summary>
    Provisioning,
    /// <summary>Company database is being seeded.</summary>
    Seeding,
    /// <summary>Connection is registered but the company is not serving yet.</summary>
    Registered,
    /// <summary>Company may be served by business operations.</summary>
    Active,
    /// <summary>Company is retained but no longer accepts business writes.</summary>
    Deactivated,
}

/// <summary>Registry record describing one independently hosted company database.</summary>
public sealed class Company
{
    private Company()
    {
    }

    private Company(
        Guid id,
        Guid tenantId,
        string code,
        string legalName,
        string tradingName,
        string baseCurrency,
        string locale,
        string documentPrefix)
    {
        Id = id;
        TenantId = tenantId;
        Code = Require(code, nameof(code));
        LegalName = Require(legalName, nameof(legalName));
        TradingName = Require(tradingName, nameof(tradingName));
        BaseCurrency = Require(baseCurrency, nameof(baseCurrency));
        Locale = Require(locale, nameof(locale));
        DocumentPrefix = Require(documentPrefix, nameof(documentPrefix));
        LifecycleState = CompanyLifecycleState.Provisioning;
    }

    /// <summary>Stable company identifier.</summary>
    public Guid Id { get; private set; }
    /// <summary>Owning tenant identifier.</summary>
    public Guid TenantId { get; private set; }
    /// <summary>Unique short code within the tenant.</summary>
    public string Code { get; private set; } = string.Empty;
    /// <summary>Registered legal name.</summary>
    public string LegalName { get; private set; } = string.Empty;
    /// <summary>Customer-facing trading name.</summary>
    public string TradingName { get; private set; } = string.Empty;
    /// <summary>Company registration number, when supplied.</summary>
    public string? RegistrationNumber { get; private set; }
    /// <summary>Tax number, when supplied.</summary>
    public string? TaxNumber { get; private set; }
    /// <summary>ISO 4217 reporting currency.</summary>
    public string BaseCurrency { get; private set; } = string.Empty;
    /// <summary>BCP-47 locale.</summary>
    public string Locale { get; private set; } = string.Empty;
    /// <summary>Unique document-number prefix within the tenant.</summary>
    public string DocumentPrefix { get; private set; } = string.Empty;
    /// <summary>Reference to encrypted connection details; never the secret itself.</summary>
    public string? ConnectionSecretRef { get; private set; }
    /// <summary>Last company schema version recorded by the migration runner.</summary>
    public long SchemaVersion { get; private set; }
    /// <summary>Actionable migration status.</summary>
    public string MigrationState { get; private set; } = "Pending";
    /// <summary>Current provisioning lifecycle state.</summary>
    public CompanyLifecycleState LifecycleState { get; private set; }
    /// <summary>Whether business operations may select this company.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Creates a company in the provisioning state.</summary>
    public static Company Create(
        Guid tenantId,
        string code,
        string legalName,
        string tradingName,
        string baseCurrency,
        string locale,
        string documentPrefix)
        => new(UuidV7.NewGuid(), tenantId, code, legalName, tradingName, baseCurrency, locale, documentPrefix);

    /// <summary>Stores only a reference to encrypted connection details.</summary>
    public void SetConnectionSecretRef(string connectionSecretRef)
    {
        ConnectionSecretRef = Require(connectionSecretRef, nameof(connectionSecretRef));
    }

    /// <summary>Advances the registry lifecycle state.</summary>
    public void SetLifecycle(CompanyLifecycleState state, bool isActive = false)
    {
        LifecycleState = state;
        IsActive = state == CompanyLifecycleState.Active && isActive;
    }

    /// <summary>Whether business operations may serve this company.</summary>
    public bool CanServe => LifecycleState == CompanyLifecycleState.Active && IsActive;

    /// <summary>Moves the company to retained, read-only state.</summary>
    public void Deactivate() => SetLifecycle(CompanyLifecycleState.Deactivated);

    /// <summary>Records the company schema version and migration state.</summary>
    public void SetMigration(long schemaVersion, string migrationState)
    {
        SchemaVersion = schemaVersion;
        MigrationState = Require(migrationState, nameof(migrationState));
    }

    private static string Require(string value, string parameterName)
        => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A value is required.", parameterName) : value.Trim();
}
