#pragma warning disable CS1591, IDE0011
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Connect;

[Replicated(ReplicationScope.Bidirectional, ConflictPolicy.CloudWins)]
public sealed class CataloguePublication : Entity
{
    private readonly List<CataloguePublicationLine> _lines = [];
    private CataloguePublication(Guid tenantId, Guid connectionId, int version, DateTimeOffset effectiveFrom, string? note)
        : base(tenantId)
    {
        ConnectionId = connectionId;
        Version = version;
        EffectiveFrom = effectiveFrom;
        VersionNote = note?.Trim();
    }
    private CataloguePublication() { }
    public Guid ConnectionId { get; private set; }
    public int Version { get; private set; }
    public DateTimeOffset EffectiveFrom { get; private set; }
    public string? VersionNote { get; private set; }
    public DateTimeOffset? RolledBackAt { get; private set; }
    public IReadOnlyList<CataloguePublicationLine> Lines => _lines;

    public static CataloguePublication Publish(Guid supplierTenantId, Guid connectionId, int version,
        DateTimeOffset effectiveFrom, string? note)
    {
        if (supplierTenantId == Guid.Empty || connectionId == Guid.Empty || version < 1) throw new ArgumentException("Invalid publication.");
        return new CataloguePublication(supplierTenantId, connectionId, version, effectiveFrom, note);
    }

    public CataloguePublicationLine AddLine(string supplierSku, string description, string? barcode, int packSize,
        decimal? minimumOrderQuantity, int? leadTimeDays)
    {
        if (RolledBackAt is not null) throw new InvalidOperationException("A rolled-back publication cannot be changed.");
        if (string.IsNullOrWhiteSpace(supplierSku) || string.IsNullOrWhiteSpace(description) || packSize < 1)
            throw new ArgumentException("Catalogue line is invalid.");
        var line = CataloguePublicationLine.Create(TenantId, Id, supplierSku, description, barcode, packSize,
            minimumOrderQuantity, leadTimeDays);
        _lines.Add(line);
        return line;
    }

    public void Rollback(DateTimeOffset at)
    {
        if (RolledBackAt is not null) return;
        RolledBackAt = at;
    }
}

[Replicated(ReplicationScope.Bidirectional, ConflictPolicy.CloudWins)]
public sealed class CataloguePublicationLine : Entity
{
    private CataloguePublicationLine(Guid tenantId, Guid publicationId, string sku, string description, string? barcode,
        int packSize, decimal? moq, int? leadTimeDays) : base(tenantId)
    {
        PublicationId = publicationId; SupplierSku = sku.Trim(); Description = description.Trim(); Barcode = barcode?.Trim();
        PackSize = packSize; MinimumOrderQuantity = moq; LeadTimeDays = leadTimeDays;
    }
    private CataloguePublicationLine() { }
    public Guid PublicationId { get; private set; }
    public string SupplierSku { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string? Barcode { get; private set; }
    public int PackSize { get; private set; }
    public decimal? MinimumOrderQuantity { get; private set; }
    public int? LeadTimeDays { get; private set; }
    internal static CataloguePublicationLine Create(Guid tenantId, Guid publicationId, string sku, string description, string? barcode, int packSize, decimal? moq, int? leadTimeDays)
        => new(tenantId, publicationId, sku, description, barcode, packSize, moq, leadTimeDays);
}
