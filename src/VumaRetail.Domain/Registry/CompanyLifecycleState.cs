namespace VumaRetail.Domain.Registry;

/// <summary>
/// A company's progress through provisioning (ADR-118). Business operations see <see cref="Active"/>
/// companies only — nothing earlier is a company yet, it is a company being built.
/// </summary>
public enum CompanyLifecycleState
{
    /// <summary>The database has been created. Migrations have not necessarily run yet.</summary>
    Provisioning = 0,

    /// <summary>The migration chain has applied; the chart of accounts, numbering, tax rules and
    /// permissions are being seeded.</summary>
    Seeding = 1,

    /// <summary>Seeded and its connection registered. Not yet visible to business operations.</summary>
    Registered = 2,

    /// <summary>Trading. The only state in which <see cref="RegistryCompany.IsActive"/> can be true.</summary>
    Active = 3,
}

/// <summary>How a company database compares to the migration chain the running binary expects (ADR-117).</summary>
public enum CompanyMigrationState
{
    /// <summary>No migration has run against this company database yet.</summary>
    NotMigrated = 0,

    /// <summary>The company database's schema matches what this binary expects.</summary>
    UpToDate = 1,

    /// <summary>The company database is behind. Its endpoints must refuse rather than guess.</summary>
    Behind = 2,
}
