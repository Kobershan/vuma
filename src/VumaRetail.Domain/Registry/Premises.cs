using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Registry;

/// <summary>
/// A physical site that one or more companies may occupy.
/// Registry data because more than one company can occupy it.
/// </summary>
public sealed class Premises
{
    private Premises() { }

    private Premises(Guid premisesId, Guid tenantId, string code, string name, string address, string geoLocation)
    {
        Id = premisesId;
        TenantId = tenantId;
        Code = Require(code, nameof(code));
        Name = Require(name, nameof(name));
        Address = Require(address, nameof(address));
        GeoLocation = Require(geoLocation, nameof(geoLocation));
        IsActive = true;
    }

    /// <summary>The stable identifier for this premises.</summary>
    public Guid Id { get; private set; }

    /// <summary>The tenant that owns this premises.</summary>
    public Guid TenantId { get; private set; }

    /// <summary>A short code for this premises.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>The human-readable name of the premises.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>The physical address.</summary>
    public string Address { get; private set; } = string.Empty;

    /// <summary>Geographic coordinates (ADR-113 value object).</summary>
    public string GeoLocation { get; private set; } = string.Empty;

    /// <summary>Trading hours for this premises.</summary>
    public string TradingHours { get; private set; } = string.Empty;

    /// <summary>Whether this premises is currently active.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Creates a new premises.</summary>
    public static Premises Create(Guid tenantId, string code, string name, string address, string geoLocation, string? tradingHours = "")
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A tenant is required.", nameof(tenantId));
        }
        var id = UuidV7.NewGuid();
        return new Premises(id, tenantId, code, name, address, geoLocation) { TradingHours = (tradingHours ?? "").Trim() };
    }

    /// <summary>Updates the premises details.</summary>
    public void Update(string? name, string? address, string? geoLocation, string? tradingHours)
    {
        if (name is not null)
        {
            Name = Require(name, nameof(name));
        }
        if (address is not null)
        {
            Address = Require(address, nameof(address));
        }
        if (geoLocation is not null)
        {
            GeoLocation = Require(geoLocation, nameof(geoLocation));
        }
        if (tradingHours is not null)
        {
            TradingHours = tradingHours.Trim();
        }
    }

    /// <summary>Deactivates the premises.</summary>
    public void Deactivate()
    {
        IsActive = false;
    }

    /// <summary>Re-activates the premises.</summary>
    public void Reactivate()
    {
        IsActive = true;
    }

    private static string Require(string value, string parameterName)
        => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A value is required.", parameterName) : value.Trim();
}
