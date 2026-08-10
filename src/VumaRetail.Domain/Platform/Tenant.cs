using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Platform;

/// <summary>
/// A customer of Vuma. The isolation boundary every other row hangs off through <c>tenant_id</c>.
/// </summary>
/// <remarks>
/// <para>
/// The localisation properties exist here rather than in configuration files because
/// <c>CLAUDE.md</c> §9 requires every one of them to be changeable per tenant from the admin UI. The
/// South African defaults — <c>en-ZA</c>, <c>ZAR</c>, <c>Africa/Johannesburg</c> — are seed values
/// applied by <see cref="CreateWithSouthAfricanDefaults"/>, not constants in the calculation path.
/// Tax is deliberately absent: it is a rules engine (ADR-014), built in Stage 07, not a rate on a row.
/// </para>
/// <para>
/// Replicated <see cref="ReplicationScope.CloudToStore"/>: the cloud tier owns tenant configuration
/// and licensing (R2), and a store server receives it. A store editing its own tenant record and
/// winning would let a lapsed tenant edit its way out of read-only.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.CloudToStore, ConflictPolicy.CloudWins)]
public sealed class Tenant : Entity
{
    private Tenant(Guid id, string legalName, string tradingName)
        : base(id)
    {
        // A tenant's TenantId is its own Id: it is the root of its own isolation boundary, so the
        // global query filter treats it exactly like every other row instead of needing a special case.
        Id = id;
        LegalName = legalName;
        TradingName = tradingName;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private Tenant()
    {
    }

    /// <summary>The registered legal entity name, as it appears on a tax invoice.</summary>
    public string LegalName { get; private set; } = string.Empty;

    /// <summary>The name customers see — the shopfront brand, which is often not the legal name.</summary>
    public string TradingName { get; private set; } = string.Empty;

    /// <summary>BCP-47 culture used for formatting, for example <c>en-ZA</c>.</summary>
    public string Locale { get; private set; } = string.Empty;

    /// <summary>ISO 4217 code the tenant reports in, for example <c>ZAR</c>.</summary>
    public string BaseCurrency { get; private set; } = string.Empty;

    /// <summary>IANA timezone, for example <c>Africa/Johannesburg</c>. Never a UTC offset — offsets move.</summary>
    public string TimeZone { get; private set; } = string.Empty;

    /// <summary>The tenant's tax registration number, where they have one.</summary>
    public string? TaxRegistrationNumber { get; private set; }

    /// <summary>True once activation has completed and the tenant may trade.</summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// Creates a tenant carrying the <c>CLAUDE.md</c> §9 South African deployment defaults.
    /// </summary>
    /// <param name="legalName">The registered legal entity name.</param>
    /// <param name="tradingName">The shopfront brand name.</param>
    /// <returns>An inactive tenant; activation is Stage 04b's job.</returns>
    public static Tenant CreateWithSouthAfricanDefaults(string legalName, string tradingName)
    {
        Tenant tenant = new(UuidV7.NewGuid(), Require(legalName, nameof(legalName)), Require(tradingName, nameof(tradingName)))
        {
            Locale = "en-ZA",
            BaseCurrency = "ZAR",
            TimeZone = "Africa/Johannesburg",
        };

        return tenant;
    }

    /// <summary>Creates a tenant with explicit localisation, for a deployment outside South Africa.</summary>
    /// <param name="legalName">The registered legal entity name.</param>
    /// <param name="tradingName">The shopfront brand name.</param>
    /// <param name="locale">BCP-47 culture.</param>
    /// <param name="baseCurrency">ISO 4217 reporting currency.</param>
    /// <param name="timeZone">IANA timezone identifier.</param>
    public static Tenant Create(string legalName, string tradingName, string locale, string baseCurrency, string timeZone)
    {
        Tenant tenant = new(UuidV7.NewGuid(), Require(legalName, nameof(legalName)), Require(tradingName, nameof(tradingName)))
        {
            Locale = Require(locale, nameof(locale)),
            BaseCurrency = new Money(0m, baseCurrency).Currency,
            TimeZone = Require(timeZone, nameof(timeZone)),
        };

        return tenant;
    }

    /// <summary>Changes the tenant's localisation. Every one of these is tenant-editable (§9).</summary>
    /// <param name="locale">BCP-47 culture.</param>
    /// <param name="baseCurrency">ISO 4217 reporting currency.</param>
    /// <param name="timeZone">IANA timezone identifier.</param>
    public void SetLocalisation(string locale, string baseCurrency, string timeZone)
    {
        Locale = Require(locale, nameof(locale));
        // Round-tripping through Money is the cheapest way to get the ISO 4217 validation for free.
        BaseCurrency = new Money(0m, baseCurrency).Currency;
        TimeZone = Require(timeZone, nameof(timeZone));
    }

    /// <summary>Records the tenant's tax registration number.</summary>
    /// <param name="number">The registration number, or <c>null</c> to clear it.</param>
    public void SetTaxRegistrationNumber(string? number)
        => TaxRegistrationNumber = string.IsNullOrWhiteSpace(number) ? null : number.Trim();

    /// <summary>Marks the tenant as activated and able to trade.</summary>
    public void Activate() => IsActive = true;

    /// <summary>Marks the tenant as inactive. Distinct from read-only enforcement, which is ADR-028's.</summary>
    public void Deactivate() => IsActive = false;

    private static string Require(string value, string parameterName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{parameterName} is required.", parameterName)
            : value.Trim();
}
