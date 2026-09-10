using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Domain.Registry;
using VumaRetail.Infrastructure.Persistence;

namespace VumaRetail.Infrastructure.Registry;

public sealed class Stage22RegistryService(VumaRegistryDbContext registry, ITenantContext tenant, IClock clock) : IStage22RegistryService
{
    public async Task<(BusinessRegistration Business, GroupSettings Settings)> CreateBusinessAsync(Guid businessId, string name, BusinessType type, decimal threshold, TransferCostingMethod costing, DiscrepancyOwner owner, string storeCodePrefix, CancellationToken cancellationToken = default)
    {
        var business = BusinessRegistration.Create(businessId, tenant.TenantId, name, type, clock.UtcNow);
        var settings = GroupSettings.Create(tenant.TenantId, businessId, threshold, costing, owner, storeCodePrefix);
        registry.BusinessRegistrations.Add(business); registry.GroupSettings.Add(settings); await registry.CommitAsync(cancellationToken).ConfigureAwait(false); return (business, settings);
    }

    public async Task<BusinessCompanyMembership> AddCompanyAsync(Guid businessId, Guid companyId, CancellationToken cancellationToken = default)
    {
        if (!await registry.BusinessRegistrations.AnyAsync(x => x.Id == businessId, cancellationToken).ConfigureAwait(false) || !await registry.Companies.AnyAsync(x => x.Id == companyId, cancellationToken).ConfigureAwait(false)) throw new KeyNotFoundException("Business or company was not found.");
        BusinessCompanyMembership? existing = await registry.BusinessCompanyMemberships.SingleOrDefaultAsync(x => x.BusinessId == businessId && x.CompanyId == companyId, cancellationToken).ConfigureAwait(false); if (existing is not null) return existing;
        var membership = BusinessCompanyMembership.Create(tenant.TenantId, businessId, companyId); registry.BusinessCompanyMemberships.Add(membership); await registry.CommitAsync(cancellationToken).ConfigureAwait(false); return membership;
    }

    public async Task<BusinessRegistration> ChangeBusinessTypeAsync(Guid businessId, BusinessType type, CancellationToken cancellationToken = default)
    {
        BusinessRegistration business = await registry.BusinessRegistrations.SingleOrDefaultAsync(x => x.Id == businessId, cancellationToken).ConfigureAwait(false) ?? throw new KeyNotFoundException("Business was not found."); business.ChangeType(type, clock.UtcNow); await registry.CommitAsync(cancellationToken).ConfigureAwait(false); return business;
    }

    public async Task<GroupHierarchyNode> AddHierarchyNodeAsync(Guid businessId, Guid companyId, HierarchyNodeType nodeType, OwnershipType ownership, Guid? parentNodeId, string? storeCode, bool stockHolding, CancellationToken cancellationToken = default)
    {
        GroupSettings settings = await registry.GroupSettings.SingleOrDefaultAsync(x => x.BusinessId == businessId, cancellationToken).ConfigureAwait(false) ?? throw new KeyNotFoundException("Group settings were not found.");
        if (nodeType == HierarchyNodeType.Store && !settings.IsValidStoreCode(storeCode ?? string.Empty)) throw new InvalidOperationException("Store code does not match the group prefix.");
        List<GroupHierarchyNode> nodes = await registry.GroupHierarchyNodes.Where(x => x.BusinessId == businessId).ToListAsync(cancellationToken).ConfigureAwait(false); var node = GroupHierarchyNode.Create(tenant.TenantId, businessId, companyId, nodeType, ownership, storeCode, stockHolding); node.SetParent(parentNodeId, nodes.Append(node).ToArray()); registry.GroupHierarchyNodes.Add(node); await registry.CommitAsync(cancellationToken).ConfigureAwait(false); return node;
    }

    public async Task<StockTransferRequest> CreateTransferAsync(Guid requesterCompanyId, Guid senderCompanyId, Guid receiverCompanyId, Guid holdingCompanyId, decimal totalValue, bool centralBuying, CancellationToken cancellationToken = default)
    {
        GroupHierarchyNode sender = await registry.GroupHierarchyNodes.SingleOrDefaultAsync(x => x.CompanyId == senderCompanyId, cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("Sender is not an owned hierarchy node.");
        GroupHierarchyNode receiver = await registry.GroupHierarchyNodes.SingleOrDefaultAsync(x => x.CompanyId == receiverCompanyId, cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("Receiver is not an owned hierarchy node.");
        if (!sender.IsControlEligible || !receiver.IsControlEligible) throw new InvalidOperationException("Franchised companies cannot participate in transfers.");
        GroupSettings settings = await registry.GroupSettings.SingleAsync(x => x.BusinessId == sender.BusinessId, cancellationToken).ConfigureAwait(false); var transfer = StockTransferRequest.Create(tenant.TenantId, requesterCompanyId, senderCompanyId, receiverCompanyId, holdingCompanyId, totalValue, centralBuying); transfer.Check(settings, false); registry.StockTransferRequests.Add(transfer); await registry.CommitAsync(cancellationToken).ConfigureAwait(false); return transfer;
    }

    public async Task<StockTransferRequest> TransitionTransferAsync(Guid transferId, string action, decimal? quantity = null, decimal? requestedQuantity = null, string? reason = null, CancellationToken cancellationToken = default)
    {
        StockTransferRequest transfer = await registry.StockTransferRequests.SingleOrDefaultAsync(x => x.Id == transferId, cancellationToken).ConfigureAwait(false) ?? throw new KeyNotFoundException("Transfer was not found.");
        switch (action.Trim().ToLowerInvariant()) { case "approve": transfer.ApproveRegional(); break; case "accept": transfer.Accept(); break; case "decline": transfer.Decline(); break; case "reserve": transfer.Reserve(); break; case "pick": transfer.Pick(); break; case "ship": transfer.Ship(); transfer.MoveInTransit(); break; case "receive": transfer.Receive(quantity ?? throw new ArgumentException("Quantity is required.")); break; case "reconcile": transfer.Reconcile(requestedQuantity ?? throw new ArgumentException("Requested quantity is required."), reason); break; case "cancel": transfer.Cancel(); break; default: throw new ArgumentException("Unknown transfer action.", nameof(action)); }
        await registry.CommitAsync(cancellationToken).ConfigureAwait(false); return transfer;
    }
}
