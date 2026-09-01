using VumaRetail.Domain.Registry;
using VumaRetail.Domain.Primitives;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace VumaRetail.Infrastructure.Registry;

/// <summary>Resolves barcodes through the routing index with fallback to local company.</summary>
public sealed class BarcodeResolver : IBarcodeResolver
{
    private readonly VumaRegistryDbContext _registry;
    private readonly ICompanyContext _companyContext;
    private readonly IClock _clock;

    public BarcodeResolver(VumaRegistryDbContext registry, ICompanyContext companyContext, IClock clock)
    {
        _registry = registry;
        _companyContext = companyContext;
        _clock = clock;
    }

    public async Task<BarcodeResolution> ResolveAsync(string barcode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(barcode))
            throw new ArgumentException("Barcode is required.", nameof(barcode));

        var entries = await _registry.CatalogRoutingIndex
            .Where(e => e.Barcode == barcode && !e.IsRetired)
            .OrderBy(e => e.CompanyId)
            .ToListAsync(cancellationToken);

        if (entries.Count == 0)
        {
            var localCompanyId = _companyContext.CompanyId;
            if (localCompanyId is not null)
            {
                return new BarcodeResolution(
                    new[] { new BarcodeCandidate(localCompanyId.Value, "local", Guid.Empty, null, barcode, "Local resolution", _clock.UtcNow) },
                    IsLocalFallback: true);
            }
            return new BarcodeResolution(Array.Empty<BarcodeCandidate>(), IsLocalFallback: false);
        }

        var candidates = entries.Select(e => new BarcodeCandidate(
            e.CompanyId, e.CompanyCode, e.ItemId, e.VariantId, e.ItemCode, e.Description, e.AsAt)).ToList();

        return new BarcodeResolution(candidates, IsLocalFallback: false);
    }

    public async Task RebuildAsync(CancellationToken cancellationToken = default)
    {
        _registry.CatalogRoutingIndex.RemoveRange(_registry.CatalogRoutingIndex);
        await _registry.CommitAsync(cancellationToken);
    }

    public async Task PublishAsync(Guid tenantId, Guid companyId, BarcodeEntry entry, CancellationToken cancellationToken = default)
    {
        var existing = await _registry.CatalogRoutingIndex
            .FirstOrDefaultAsync(e => e.TenantId == tenantId && e.CompanyId == companyId && e.Barcode == entry.Barcode, cancellationToken);

        if (existing is not null)
        {
            existing.Update(entry.ItemId, entry.VariantId, entry.ItemCode, entry.Description, entry.AsAt);
        }
        else
        {
            _registry.CatalogRoutingIndex.Add(new CatalogRoutingIndexEntry
            {
                Id = UuidV7.NewGuid(),
                TenantId = tenantId,
                CompanyId = companyId,
                Barcode = entry.Barcode,
                ItemId = entry.ItemId,
                VariantId = entry.VariantId,
                ItemCode = entry.ItemCode,
                Description = entry.Description,
                AsAt = entry.AsAt,
                IsRetired = false
            });
        }

        await _registry.CommitAsync(cancellationToken);
    }
}
