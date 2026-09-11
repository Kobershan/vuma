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
    private readonly ICompanyDbContextFactory _companyDatabases;
    private readonly IClock _clock;

    public BarcodeResolver(
        VumaRegistryDbContext registry,
        ICompanyContext companyContext,
        ICompanyDbContextFactory companyDatabases,
        IClock clock)
    {
        _registry = registry;
        _companyContext = companyContext;
        _companyDatabases = companyDatabases;
        _clock = clock;
    }

    public async Task<BarcodeResolution> ResolveAsync(string barcode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(barcode))
            throw new ArgumentException("Barcode is required.", nameof(barcode));

        string scanned = barcode.Trim();
        List<CatalogRoutingIndexEntry> entries;
        try
        {
            entries = await _registry.CatalogRoutingIndex
                .AsNoTracking()
                .Where(e => e.Barcode == scanned && !e.IsRetired)
                .OrderBy(e => e.CompanyCode)
                .ThenBy(e => e.CompanyId)
                .ToListAsync(cancellationToken);
        }
        catch (Npgsql.NpgsqlException)
        {
            return await ResolveLocallyAsync(scanned, cancellationToken);
        }

        if (entries.Count == 0)
        {
            return await ResolveLocallyAsync(scanned, cancellationToken);
        }

        var candidates = entries.Select(e => new BarcodeCandidate(
            e.CompanyId, e.CompanyCode, e.ItemId, e.VariantId, e.ItemCode, e.Description, e.AsAt)).ToList();

        return new BarcodeResolution(candidates, IsLocalFallback: false);
    }

    public async Task RebuildAsync(CancellationToken cancellationToken = default)
    {
        // A rebuild must ask every company to republish. Erasing a live projection before that
        // fan-out has completed makes a registry outage look like an empty catalogue, so this
        // method deliberately refuses the old destructive placeholder until a publisher is wired.
        throw new NotSupportedException("Routing rebuild requires a company catalogue republisher.");
    }

    public async Task PublishAsync(Guid tenantId, Guid companyId, BarcodeEntry entry, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("A tenant is required.", nameof(tenantId));
        if (companyId == Guid.Empty) throw new ArgumentException("A company is required.", nameof(companyId));
        if (string.IsNullOrWhiteSpace(entry.Barcode)) throw new ArgumentException("A barcode is required.", nameof(entry));

        string companyCode = await _registry.Companies.AsNoTracking()
            .Where(company => company.TenantId == tenantId && company.Id == companyId)
            .Select(company => company.Code)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("The publishing company is not registered.");
        string scanned = entry.Barcode.Trim();
        var existing = await _registry.CatalogRoutingIndex
            .FirstOrDefaultAsync(e => e.TenantId == tenantId && e.CompanyId == companyId && e.Barcode == scanned, cancellationToken);

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
                CompanyCode = companyCode,
                Barcode = scanned,
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

    private async Task<BarcodeResolution> ResolveLocallyAsync(string barcode, CancellationToken cancellationToken)
    {
        Guid? companyId = _companyContext.CompanyId;
        if (companyId is null) return new BarcodeResolution([], IsLocalFallback: true);

        await using VumaRetailDbContext company = await _companyDatabases
            .CreateAsync(CompanyAccessMode.Read, cancellationToken);
        Domain.Catalog.Barcode? local = await company.Barcodes.AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Code == barcode, cancellationToken);
        if (local is null) return new BarcodeResolution([], IsLocalFallback: true);

        Guid itemId = local.ItemId ?? Guid.Empty;
        Domain.Catalog.Item? item;
        if (local.ItemVariantId is { } variantId)
        {
            Domain.Catalog.ItemVariant? variant = await company.ItemVariants.AsNoTracking()
                .FirstOrDefaultAsync(candidate => candidate.Id == variantId, cancellationToken);
            itemId = variant?.ItemId ?? Guid.Empty;
        }
        item = itemId == Guid.Empty ? null : await company.Items.AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == itemId, cancellationToken);
        if (item is null || !item.IsActive) return new BarcodeResolution([], IsLocalFallback: true);

        return new BarcodeResolution(
            [new BarcodeCandidate(companyId.Value, "local", itemId, local.ItemVariantId,
                item.Code, item.Description ?? item.Name, _clock.UtcNow)],
            IsLocalFallback: true);
    }
}
