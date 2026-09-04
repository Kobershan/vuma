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
    /// <summary>The last durable provisioning step reached.</summary>
    public string ProvisioningStep { get; private set; } = "provisioning";
    /// <summary>Operator-safe description of the last provisioning failure.</summary>
    public string? ProvisioningError { get; private set; }
    /// <summary>Number of attempts made for the current provisioning step.</summary>
    public int ProvisioningAttempts { get; private set; }
    /// <summary>Current provisioning lifecycle state.</summary>
    public CompanyLifecycleState LifecycleState { get; private set; }
    /// <summary>
    /// The vendor-issued Operator ID this company was provisioned under (ADR-121).
    /// </summary>
    /// <remarks>
    /// Projected from the signed licence, never set by a tenant command. A company whose
    /// operator is still <see cref="Guid.Empty"/> predates operator assignment and can be
    /// linked to nothing until the vendor assigns it.
    /// </remarks>
    public Guid OperatorId { get; private set; }
    /// <summary>Whether business operations may select this company.</summary>
    public bool IsActive { get; private set; }
    /// <summary>When the company entered retained read-only state.</summary>
    public DateTimeOffset? DeactivatedAt { get; private set; }
    /// <summary>The actor that deactivated the company.</summary>
    public string? DeactivatedBy { get; private set; }
    /// <summary>The operator-supplied reason for deactivation.</summary>
    public string? DeactivationReason { get; private set; }

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

    /// <summary>
    /// Assigns the owning Operator ID. Vendor-side only: called during provisioning from the
    /// signed licence, never from a tenant command (ADR-121).
    /// </summary>
    /// <param name="operatorId">The operator that owns this company.</param>
    /// <remarks>
    /// Set-once. Changing ownership of a company is a vendor-side operation with billing
    /// consequences, not an edit — a second, different assignment is refused.
    /// </remarks>
    public void AssignOperator(Guid operatorId)
    {
        if (operatorId == Guid.Empty)
        {
            throw new ArgumentException("An operator identifier is required.", nameof(operatorId));
        }

        if (OperatorId != Guid.Empty && OperatorId != operatorId)
        {
            throw new InvalidOperationException("A company's Operator ID cannot be changed once assigned.");
        }

        OperatorId = operatorId;
    }

    /// <summary>Stores only a reference to encrypted connection details.</summary>
    public void SetConnectionSecretRef(string connectionSecretRef)
    {
        ConnectionSecretRef = Require(connectionSecretRef, nameof(connectionSecretRef));
    }

    /// <summary>Advances the registry lifecycle state.</summary>
    public void SetLifecycle(CompanyLifecycleState state, bool isActive = false)
    {
        if (state == LifecycleState)
        {
            IsActive = state == CompanyLifecycleState.Active && isActive;
            return;
        }

        bool validTransition = (LifecycleState, state) switch
        {
            (CompanyLifecycleState.Provisioning, CompanyLifecycleState.Seeding) => true,
            (CompanyLifecycleState.Seeding, CompanyLifecycleState.Registered) => true,
            (CompanyLifecycleState.Registered, CompanyLifecycleState.Active) => true,
            (CompanyLifecycleState.Active, CompanyLifecycleState.Deactivated) => true,
            _ => false,
        };

        if (!validTransition)
        {
            throw new InvalidOperationException(
                $"Company lifecycle cannot move from {LifecycleState} to {state}.");
        }

        if (state == CompanyLifecycleState.Active && string.IsNullOrWhiteSpace(ConnectionSecretRef))
        {
            throw new InvalidOperationException("An active company requires a connection secret reference.");
        }

        LifecycleState = state;
        IsActive = state == CompanyLifecycleState.Active && isActive;
    }

    /// <summary>Whether business operations may serve this company.</summary>
    public bool CanServe => LifecycleState == CompanyLifecycleState.Active && IsActive;

    /// <summary>Moves the company to retained, read-only state.</summary>
    public bool Deactivate(string actor, string reason, DateTimeOffset occurredAt)
    {
        if (string.IsNullOrWhiteSpace(actor)) { throw new ArgumentException("An actor is required.", nameof(actor)); }
        if (string.IsNullOrWhiteSpace(reason)) { throw new ArgumentException("A reason is required.", nameof(reason)); }
        if (LifecycleState == CompanyLifecycleState.Deactivated) { return false; }
        SetLifecycle(CompanyLifecycleState.Deactivated);
        DeactivatedAt = occurredAt;
        DeactivatedBy = actor.Trim();
        DeactivationReason = reason.Trim();
        return true;
    }

    /// <summary>Records the company schema version and migration state.</summary>
    public void SetMigration(long schemaVersion, string migrationState)
    {
        SchemaVersion = schemaVersion;
        MigrationState = Require(migrationState, nameof(migrationState));
    }

    /// <summary>Records durable progress after a successful, idempotent step.</summary>
    public void RecordProvisioningProgress(string step)
    {
        ProvisioningStep = Require(step, nameof(step));
        ProvisioningError = null;
        ProvisioningAttempts++;
    }

    /// <summary>Records a redacted failure without making the company serveable.</summary>
    public void RecordProvisioningFailure(string error)
    {
        ProvisioningError = Require(error, nameof(error));
        ProvisioningAttempts++;
        IsActive = false;
    }

    private static string Require(string value, string parameterName)
        => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A value is required.", parameterName) : value.Trim();
}
