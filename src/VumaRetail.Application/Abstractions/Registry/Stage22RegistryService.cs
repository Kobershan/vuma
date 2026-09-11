#pragma warning disable CS1591
using VumaRetail.Domain.Registry;

namespace VumaRetail.Application.Abstractions.Registry;

public sealed record Stage22TransferLine(
    Guid? ItemId,
    Guid? ItemVariantId,
    decimal Quantity,
    string UnitOfMeasure,
    Guid SenderLocationId,
    Guid? ReceiverLocationId = null,
    string? BatchReference = null,
    DateOnly? ExpiryDate = null,
    string? SerialNumber = null);

public interface IStage22RegistryService
{
    Task<(BusinessRegistration Business, GroupSettings Settings)> CreateBusinessAsync(Guid businessId, string name, BusinessType type, decimal threshold, TransferCostingMethod costing, DiscrepancyOwner owner, string storeCodePrefix, CancellationToken cancellationToken = default);
    Task<BusinessCompanyMembership> AddCompanyAsync(Guid businessId, Guid companyId, CancellationToken cancellationToken = default);
    Task<BusinessRegistration> ChangeBusinessTypeAsync(Guid businessId, BusinessType type, CancellationToken cancellationToken = default);
    Task<GroupHierarchyNode> AddHierarchyNodeAsync(Guid businessId, Guid companyId, HierarchyNodeType nodeType, OwnershipType ownership, Guid? parentNodeId, string? storeCode, bool stockHolding, CancellationToken cancellationToken = default);
    Task<StockTransferRequest> CreateTransferAsync(Guid requesterCompanyId, Guid senderCompanyId, Guid receiverCompanyId, Guid holdingCompanyId, decimal totalValue, bool centralBuying, IReadOnlyCollection<Stage22TransferLine>? lines = null, CancellationToken cancellationToken = default);
    Task<StockTransferRequest> TransitionTransferAsync(Guid transferId, string action, decimal? quantity = null, decimal? requestedQuantity = null, string? reason = null, CancellationToken cancellationToken = default);
    Task<StockTransferDeliveryNote> CreateDeliveryNoteAsync(Guid transferId, string? driverReference = null, CancellationToken cancellationToken = default);
    Task<PremisesSkuRouting> AddPremisesSkuRoutingAsync(Guid premisesId, string skuOrBarcode, Guid companyId, bool isBarcode, CancellationToken cancellationToken = default);
    Task<OwnedStockOnHandProjection> PublishOwnedStockProjectionAsync(OwnedStockOnHandProjection projection, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OwnedStockOnHandProjection>> ListOwnedStockAsync(Guid businessId, Guid? companyId = null, CancellationToken cancellationToken = default);
}
