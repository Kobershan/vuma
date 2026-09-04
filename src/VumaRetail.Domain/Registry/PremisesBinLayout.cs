using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Registry;

/// <summary>
/// The master zone/bin definitions for a premises, mirrored into each occupying company's
/// warehouse schema (ADR-124).
/// </summary>
public sealed class PremisesBinLayout
{
    private PremisesBinLayout() { }

    private PremisesBinLayout(Guid id, Guid premisesId, string zoneCode, string binCode, string description)
    {
        Id = id;
        PremisesId = premisesId;
        ZoneCode = Require(zoneCode, nameof(zoneCode));
        BinCode = Require(binCode, nameof(binCode));
        Description = description?.Trim() ?? string.Empty;
    }

    /// <summary>The layout record identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>The premises this layout belongs to.</summary>
    public Guid PremisesId { get; private set; }

    /// <summary>The zone code (e.g. "A1", "RECEIVING").</summary>
    public string ZoneCode { get; private set; } = string.Empty;

    /// <summary>The bin code within the zone.</summary>
    public string BinCode { get; private set; } = string.Empty;

    /// <summary>Description of the bin.</summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>
    /// Whether the bin can hold stock from multiple companies.
    /// A bin never lets a quantity span two companies (ADR-124).
    /// </summary>
    public bool IsShared { get; private set; }

    /// <summary>Creates a new bin layout entry for a premises.</summary>
    public static PremisesBinLayout Create(Guid premisesId, string zoneCode, string binCode, string? description = "", bool isShared = false)
    {
        if (premisesId == Guid.Empty)
        {
            throw new ArgumentException("A premises is required.", nameof(premisesId));
        }
        return new PremisesBinLayout(UuidV7.NewGuid(), premisesId, zoneCode, binCode, description ?? "")
        {
            IsShared = isShared
        };
    }

    private static string Require(string value, string parameterName)
        => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A value is required.", parameterName) : value.Trim();
}
