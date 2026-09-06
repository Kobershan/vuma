#pragma warning disable CS1591
using VumaRetail.Domain.Registry;

namespace VumaRetail.Application.Abstractions.Registry;

/// <summary>Resolves a barcode to one or more companies without guessing.</summary>
public interface IBarcodeResolver
{
    /// <summary>Probe the routing index for a barcode. Returns candidates, never a single guess.</summary>
    Task<BarcodeResolution> ResolveAsync(string barcode, CancellationToken cancellationToken = default);
    
    /// <summary>Rebuilds the routing index by asking every company to republish.</summary>
    Task RebuildAsync(CancellationToken cancellationToken = default);
    
    /// <summary>Published by a company when a barcode is created, changed, or retired.</summary>
    Task PublishAsync(Guid tenantId, Guid companyId, BarcodeEntry entry, CancellationToken cancellationToken = default);
}

/// <summary>One barcode record in the routing index.</summary>
public sealed record BarcodeEntry(string Barcode, Guid ItemId, Guid? VariantId, string ItemCode, string Description, DateTimeOffset AsAt);

/// <summary>Result of a barcode resolution. Carries candidates or a local-fallback indication.</summary>
public sealed record BarcodeResolution(IReadOnlyList<BarcodeCandidate> Candidates, bool IsLocalFallback)
{
    /// <summary>Whether there is exactly one candidate.</summary>
    public bool IsSingle => Candidates.Count == 1;
    /// <summary>Whether there are multiple candidates.</summary>
    public bool IsMultiple => Candidates.Count > 1;
    /// <summary>Whether there are no candidates.</summary>
    public bool IsNone => Candidates.Count == 0;
}

/// <summary>One candidate barcode resolution.</summary>
public sealed record BarcodeCandidate(Guid CompanyId, string CompanyCode, Guid ItemId, Guid? VariantId, string ItemCode, string Description, DateTimeOffset AsAt);
