using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Registry;

/// <summary>
/// Terminal registration — a device that can sell for one or more companies.
/// A till may sell for a sister company only where SharedTill is granted.
/// </summary>
public sealed class RegistryTerminal
{
    private RegistryTerminal() { }

    private RegistryTerminal(Guid id, Guid tenantId, Guid premisesId, string terminalId, string deviceCertThumbprint)
    {
        Id = id;
        TenantId = tenantId;
        PremisesId = premisesId;
        TerminalId = Require(terminalId, nameof(terminalId));
        DeviceCertThumbprint = Require(deviceCertThumbprint, nameof(deviceCertThumbprint));
        IsActive = true;
    }

    /// <summary>The terminal's registry identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>The tenant this terminal belongs to.</summary>
    public Guid TenantId { get; private set; }

    /// <summary>The premises this terminal is located at.</summary>
    public Guid PremisesId { get; private set; }

    /// <summary>The terminal identifier.</summary>
    public string TerminalId { get; private set; } = string.Empty;

    /// <summary>The device certificate thumbprint.</summary>
    public string DeviceCertThumbprint { get; private set; } = string.Empty;

    /// <summary>Whether the terminal is active.</summary>
    public bool IsActive { get; private set; }

    /// <summary>The companies this terminal may sell for.</summary>
    public List<Guid> CompanyIds { get; private set; } = [];

    /// <summary>Registers a terminal at a premises.</summary>
    public static RegistryTerminal Create(Guid tenantId, Guid premisesId, string terminalId, string deviceCertThumbprint)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A tenant is required.", nameof(tenantId));
        }
        if (premisesId == Guid.Empty)
        {
            throw new ArgumentException("A premises is required.", nameof(premisesId));
        }
        return new RegistryTerminal(UuidV7.NewGuid(), tenantId, premisesId, terminalId, deviceCertThumbprint);
    }

    /// <summary>Adds a company this terminal may sell for.</summary>
    public void AddCompany(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("A company is required.", nameof(companyId));
        }
        if (!CompanyIds.Contains(companyId))
        {
            CompanyIds.Add(companyId);
        }
    }

    /// <summary>Removes a company from this terminal's authorized list.</summary>
    public void RemoveCompany(Guid companyId)
    {
        CompanyIds.Remove(companyId);
    }

    /// <summary>Sets the companies this terminal may sell for.</summary>
    public void SetCompanies(IEnumerable<Guid> companyIds)
    {
        CompanyIds = companyIds.Where(c => c != Guid.Empty).Distinct().ToList();
    }

    /// <summary>Deactivates the terminal.</summary>
    public void Deactivate()
    {
        IsActive = false;
    }

    /// <summary>Re-activates the terminal.</summary>
    public void Reactivate()
    {
        IsActive = true;
    }

    private static string Require(string value, string parameterName)
        => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A value is required.", parameterName) : value.Trim();
}
