using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Abstractions.Sync;
using System.Text.Json;
using VumaRetail.Domain.Registry;
using VumaRetail.Infrastructure.Persistence;

namespace VumaRetail.Infrastructure.Registry;

public sealed class Stage22RegistryService(
    VumaRegistryDbContext registry,
    ITenantContext tenant,
    IClock clock,
    ISagaCoordinator? sagaCoordinator = null,
    IOperatorContext? operatorContext = null,
    IHybridClock? hybridClock = null,
    IPrincipalAccessor? principal = null) : IStage22RegistryService
{
    public async Task<(BusinessRegistration Business, GroupSettings Settings)> CreateBusinessAsync(Guid businessId, string name, BusinessType type, decimal threshold, TransferCostingMethod costing, DiscrepancyOwner owner, string storeCodePrefix, CancellationToken cancellationToken = default)
    {
        if (await registry.BusinessRegistrations.AnyAsync(x => x.Id == businessId, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("A business with this identity already exists.");
        }

        var business = BusinessRegistration.Create(businessId, tenant.TenantId, name, type, clock.UtcNow);
        var settings = GroupSettings.Create(tenant.TenantId, businessId, threshold, costing, owner, storeCodePrefix);
        registry.BusinessRegistrations.Add(business); registry.GroupSettings.Add(settings); await registry.CommitAsync(cancellationToken).ConfigureAwait(false); return (business, settings);
    }

    public async Task<BusinessCompanyMembership> AddCompanyAsync(Guid businessId, Guid companyId, CancellationToken cancellationToken = default)
    {
        if (!await registry.BusinessRegistrations.AnyAsync(x => x.Id == businessId && x.TenantId == tenant.TenantId, cancellationToken).ConfigureAwait(false)
            || !await registry.Companies.AnyAsync(x => x.Id == companyId && x.TenantId == tenant.TenantId, cancellationToken).ConfigureAwait(false))
        {
            throw new KeyNotFoundException("Business or company was not found.");
        }

        BusinessCompanyMembership? existing = await registry.BusinessCompanyMemberships
            .SingleOrDefaultAsync(x => x.TenantId == tenant.TenantId && x.BusinessId == businessId && x.CompanyId == companyId, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null) return existing;
        var membership = BusinessCompanyMembership.Create(tenant.TenantId, businessId, companyId); registry.BusinessCompanyMemberships.Add(membership); await registry.CommitAsync(cancellationToken).ConfigureAwait(false); return membership;
    }

    public async Task<BusinessRegistration> ChangeBusinessTypeAsync(Guid businessId, BusinessType type, CancellationToken cancellationToken = default)
    {
        BusinessRegistration business = await registry.BusinessRegistrations
            .SingleOrDefaultAsync(x => x.Id == businessId && x.TenantId == tenant.TenantId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Business was not found.");
        business.ChangeType(type, clock.UtcNow);
        await registry.CommitAsync(cancellationToken).ConfigureAwait(false);
        return business;
    }

    public async Task<GroupHierarchyNode> AddHierarchyNodeAsync(Guid businessId, Guid companyId, HierarchyNodeType nodeType, OwnershipType ownership, Guid? parentNodeId, string? storeCode, bool stockHolding, CancellationToken cancellationToken = default)
    {
        GroupSettings settings = await registry.GroupSettings
            .SingleOrDefaultAsync(x => x.TenantId == tenant.TenantId && x.BusinessId == businessId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Group settings were not found.");
        bool member = await registry.BusinessCompanyMemberships.AnyAsync(
            x => x.TenantId == tenant.TenantId && x.BusinessId == businessId && x.CompanyId == companyId,
            cancellationToken).ConfigureAwait(false);
        if (!member) throw new InvalidOperationException("The company is not a member of this business.");
        if (nodeType == HierarchyNodeType.Store && !settings.IsValidStoreCode(storeCode ?? string.Empty)) throw new InvalidOperationException("Store code does not match the group prefix.");
        List<GroupHierarchyNode> nodes = await registry.GroupHierarchyNodes
            .Where(x => x.TenantId == tenant.TenantId && x.BusinessId == businessId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var node = GroupHierarchyNode.Create(tenant.TenantId, businessId, companyId, nodeType, ownership, storeCode, stockHolding);
        node.SetParent(parentNodeId, nodes.Append(node).ToArray());
        registry.GroupHierarchyNodes.Add(node);
        await registry.CommitAsync(cancellationToken).ConfigureAwait(false);
        return node;
    }

    public async Task<StockTransferRequest> CreateTransferAsync(Guid requesterCompanyId, Guid senderCompanyId, Guid receiverCompanyId, Guid holdingCompanyId, decimal totalValue, bool centralBuying, IReadOnlyCollection<Stage22TransferLine>? lines = null, CancellationToken cancellationToken = default)
    {
        GroupHierarchyNode sender = await registry.GroupHierarchyNodes.SingleOrDefaultAsync(
            x => x.TenantId == tenant.TenantId && x.CompanyId == senderCompanyId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Sender is not an owned hierarchy node.");
        GroupHierarchyNode receiver = await registry.GroupHierarchyNodes.SingleOrDefaultAsync(
            x => x.TenantId == tenant.TenantId && x.CompanyId == receiverCompanyId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Receiver is not an owned hierarchy node.");
        if (sender.BusinessId != receiver.BusinessId)
        {
            throw new InvalidOperationException("Sender and receiver must belong to the same business.");
        }
        bool requesterMember = await registry.BusinessCompanyMemberships.AnyAsync(
            x => x.TenantId == tenant.TenantId && x.BusinessId == sender.BusinessId && x.CompanyId == requesterCompanyId,
            cancellationToken).ConfigureAwait(false);
        bool holdingMember = await registry.BusinessCompanyMemberships.AnyAsync(
            x => x.TenantId == tenant.TenantId && x.BusinessId == sender.BusinessId && x.CompanyId == holdingCompanyId,
            cancellationToken).ConfigureAwait(false);
        if (!requesterMember || !holdingMember)
        {
            throw new InvalidOperationException("Requester and holding company must belong to the transfer business.");
        }
        if (!sender.IsControlEligible || !receiver.IsControlEligible) throw new InvalidOperationException("Franchised companies cannot participate in transfers.");
        GroupSettings settings = await registry.GroupSettings.SingleAsync(
            x => x.TenantId == tenant.TenantId && x.BusinessId == sender.BusinessId, cancellationToken)
            .ConfigureAwait(false);
        StockTransferRequest transfer = StockTransferRequest.Create(tenant.TenantId, requesterCompanyId, senderCompanyId, receiverCompanyId, holdingCompanyId, totalValue, centralBuying);
        if (lines is { Count: > 0 })
        {
            foreach (Stage22TransferLine line in lines)
            {
                StockTransferLine transferLine = StockTransferLine.Create(
                    tenant.TenantId, transfer.Id, line.ItemId, line.ItemVariantId,
                    line.Quantity, line.UnitOfMeasure, line.SenderLocationId, line.ReceiverLocationId);
                transfer.Lines.Add(transferLine);
            }
        }
        GroupHierarchyNode? holdingNode = await registry.GroupHierarchyNodes.SingleOrDefaultAsync(
            x => x.TenantId == tenant.TenantId && x.BusinessId == sender.BusinessId && x.CompanyId == holdingCompanyId,
            cancellationToken).ConfigureAwait(false);
        bool receiverIsDirectChildOfHolding = holdingNode is not null && receiver.ParentNodeId == holdingNode.Id;
        transfer.Check(settings, receiverIsDirectChildOfHolding);
        registry.StockTransferRequests.Add(transfer);
        foreach (StockTransferLine line in transfer.Lines)
        {
            registry.StockTransferLines.Add(line);
        }
        await registry.CommitAsync(cancellationToken).ConfigureAwait(false);
        return transfer;
    }

    public async Task<StockTransferRequest> TransitionTransferAsync(Guid transferId, string action, decimal? quantity = null, decimal? requestedQuantity = null, string? reason = null, CancellationToken cancellationToken = default)
    {
        StockTransferRequest transfer = await registry.StockTransferRequests
            .SingleOrDefaultAsync(x => x.Id == transferId && x.TenantId == tenant.TenantId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Transfer was not found.");
        List<StockTransferLine> persistedLines = await registry.StockTransferLines
            .Where(x => x.TenantId == tenant.TenantId && x.TransferId == transfer.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        transfer.Lines.AddRange(persistedLines);
        switch (action.Trim().ToLowerInvariant())
        {
            case "approve": transfer.ApproveRegional(); break;
            case "accept": transfer.Accept(); break;
            case "decline": transfer.Decline(); break;
            case "reserve": await ReserveTransferAsync(transfer, persistedLines, cancellationToken).ConfigureAwait(false); break;
            case "pick": transfer.Pick(); break;
            case "ship": await ShipTransferAsync(transfer, persistedLines, cancellationToken).ConfigureAwait(false); break;
            case "in-transit": transfer.MoveInTransit(); break;
            case "receive": await ReceiveTransferAsync(transfer, persistedLines, quantity ?? throw new ArgumentException("Quantity is required."), cancellationToken).ConfigureAwait(false); break;
            case "reconcile": transfer.Reconcile(requestedQuantity ?? throw new ArgumentException("Requested quantity is required."), reason); break;
            case "cancel": transfer.Cancel(); break;
            case "remainder": return await CreateRelatedTransferAsync(transfer, persistedLines, TransferRelation.Remainder, cancellationToken).ConfigureAwait(false);
            case "reverse": return await CreateRelatedTransferAsync(transfer, persistedLines, TransferRelation.Reverse, cancellationToken).ConfigureAwait(false);
            default: throw new ArgumentException("Unknown transfer action.", nameof(action));
        }
        await registry.CommitAsync(cancellationToken).ConfigureAwait(false); return transfer;
    }

    public async Task<StockTransferDeliveryNote> CreateDeliveryNoteAsync(
        Guid transferId,
        string? driverReference = null,
        CancellationToken cancellationToken = default)
    {
        StockTransferRequest transfer = await registry.StockTransferRequests
            .SingleOrDefaultAsync(x => x.Id == transferId && x.TenantId == tenant.TenantId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Transfer was not found.");
        List<StockTransferLine> lines = await registry.StockTransferLines
            .Where(x => x.TenantId == tenant.TenantId && x.TransferId == transferId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        transfer.Lines.AddRange(lines);

        StockTransferDeliveryNote? existing = await registry.StockTransferDeliveryNotes
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.TenantId == tenant.TenantId && x.TransferId == transferId, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null) return existing;

        StockTransferDeliveryNote note = StockTransferDeliveryNote.Create(transfer, clock.UtcNow, driverReference);
        registry.StockTransferDeliveryNotes.Add(note);
        foreach (StockTransferDeliveryNoteLine line in note.Lines)
        {
            registry.StockTransferDeliveryNoteLines.Add(line);
        }
        await registry.CommitAsync(cancellationToken).ConfigureAwait(false);
        return note;
    }

    private async Task<StockTransferRequest> CreateRelatedTransferAsync(
        StockTransferRequest source,
        IReadOnlyList<StockTransferLine> sourceLines,
        TransferRelation relation,
        CancellationToken cancellationToken)
    {
        if (source.Status is not (TransferStatus.Received or TransferStatus.Reconciled))
        {
            throw new InvalidOperationException("A related transfer can only be created after receipt.");
        }

        StockTransferRequest? existing = await registry.StockTransferRequests
            .SingleOrDefaultAsync(x => x.TenantId == source.TenantId
                && x.RelatedTransferId == source.Id
                && x.Relation == relation, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            existing.Lines.AddRange(await registry.StockTransferLines
                .Where(x => x.TenantId == source.TenantId && x.TransferId == existing.Id)
                .ToListAsync(cancellationToken).ConfigureAwait(false));
            return existing;
        }

        List<StockTransferLine> lines = [];
        foreach (StockTransferLine line in sourceLines)
        {
            decimal quantity = relation == TransferRelation.Reverse
                ? line.ReceivedQuantity ?? 0m
                : line.Quantity - (line.ReceivedQuantity ?? 0m);
            if (quantity <= 0m) continue;
            lines.Add(StockTransferLine.Create(
                source.TenantId, source.Id, line.ItemId, line.ItemVariantId,
                quantity, line.UnitOfMeasure, line.SenderLocationId, line.ReceiverLocationId));
        }
        if (lines.Count == 0)
        {
            throw new InvalidOperationException(relation == TransferRelation.Reverse
                ? "There is no received quantity to reverse."
                : "There is no unreceived quantity remaining.");
        }

        decimal sourceQuantity = sourceLines.Sum(line => line.Quantity);
        decimal relatedQuantity = lines.Sum(line => line.Quantity);
        decimal totalValue = sourceQuantity == 0m ? 0m : source.TotalValue * relatedQuantity / sourceQuantity;
        StockTransferRequest related = StockTransferRequest.CreateRelated(source, relation, lines, totalValue);
        registry.StockTransferRequests.Add(related);
        foreach (StockTransferLine line in related.Lines)
        {
            registry.StockTransferLines.Add(line);
        }
        await registry.CommitAsync(cancellationToken).ConfigureAwait(false);
        return related;
    }

    private async Task ReserveTransferAsync(
        StockTransferRequest transfer,
        IReadOnlyList<StockTransferLine> lines,
        CancellationToken cancellationToken)
    {
        if (transfer.Status == TransferStatus.Reserved)
        {
            return;
        }

        if (lines.Count == 0)
        {
            transfer.Reserve();
            return;
        }

        if (sagaCoordinator is null || operatorContext is null || hybridClock is null || principal is null)
        {
            throw new InvalidOperationException("Transfer reservation saga services are not configured.");
        }

        TransferReservationPayload payload = new(
            transfer.TenantId,
            transfer.Id,
            transfer.SenderCompanyId,
            lines.Select(line => new TransferReservationLinePayload(
                line.Id, line.SenderLocationId, line.ItemId, line.ItemVariantId,
                line.Quantity, line.UnitOfMeasure, line.ReceiverLocationId)).ToArray());
        SagaIntent intent = SagaIntent.Create(
            transfer.TenantId,
            TransferReservationSaga.IntentType,
            $"transfer-reservation:{transfer.Id:N}",
            clock.UtcNow,
            JsonSerializer.Serialize(payload));
        intent.Authorize(operatorContext.RequireOperatorId(), principal.Principal, hybridClock.Next().ToString());
        intent.AddLeg(transfer.SenderCompanyId);

        SagaResult result = await sagaCoordinator.ExecuteAsync(intent, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException("Transfer source reservation is still pending or failed; retry the reservation leg.");
        }

        transfer.Reserve();
    }

    private async Task ShipTransferAsync(
        StockTransferRequest transfer,
        IReadOnlyList<StockTransferLine> lines,
        CancellationToken cancellationToken)
    {
        if (lines.Count == 0)
        {
            transfer.Ship();
            return;
        }

        if (sagaCoordinator is null || operatorContext is null || hybridClock is null || principal is null)
        {
            throw new InvalidOperationException("Transfer shipment saga services are not configured.");
        }

        TransferShipmentPayload payload = new(
            transfer.TenantId,
            transfer.Id,
            transfer.SenderCompanyId,
            lines.Select(line => new TransferReservationLinePayload(
                line.Id, line.SenderLocationId, line.ItemId, line.ItemVariantId,
                line.Quantity, line.UnitOfMeasure, line.ReceiverLocationId)).ToArray());
        SagaIntent intent = SagaIntent.Create(
            transfer.TenantId,
            TransferShipmentSaga.IntentType,
            $"transfer-shipment:{transfer.Id:N}",
            clock.UtcNow,
            JsonSerializer.Serialize(payload));
        intent.Authorize(operatorContext.RequireOperatorId(), principal.Principal, hybridClock.Next().ToString());
        intent.AddLeg(transfer.SenderCompanyId);

        SagaResult result = await sagaCoordinator.ExecuteAsync(intent, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException("Transfer shipment is still pending or failed; retry the shipment leg.");
        }

        transfer.Ship();
    }

    private async Task ReceiveTransferAsync(
        StockTransferRequest transfer,
        IReadOnlyList<StockTransferLine> lines,
        decimal quantity,
        CancellationToken cancellationToken)
    {
        if (quantity < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Received quantity cannot be negative.");
        }

        if (lines.Count == 0)
        {
            transfer.Receive(quantity);
            return;
        }

        decimal requestedTotal = lines.Sum(line => line.Quantity);
        if (quantity > requestedTotal)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Received quantity cannot exceed the transfer quantity.");
        }

        decimal previousTotal = transfer.ReceivedQuantity ?? 0m;
        if (quantity <= previousTotal)
        {
            transfer.Receive(quantity);
            return;
        }

        if (sagaCoordinator is null || operatorContext is null || hybridClock is null || principal is null)
        {
            throw new InvalidOperationException("Transfer receipt saga services are not configured.");
        }

        decimal remaining = quantity;
        List<TransferReceiptLinePayload> receiptLines = [];
        foreach (StockTransferLine line in lines)
        {
            decimal target = Math.Min(line.Quantity, remaining);
            decimal alreadyReceived = line.ReceivedQuantity ?? 0m;
            decimal delta = target - alreadyReceived;
            if (delta > 0m)
            {
                if (line.ReceiverLocationId is not Guid receiverLocationId
                    || line.UnitCostAtTransferAmount is not decimal unitCost
                    || string.IsNullOrWhiteSpace(line.UnitCostAtTransferCurrency))
                {
                    throw new InvalidOperationException($"Transfer line {line.Id} is missing receiver location or shipment cost.");
                }

                receiptLines.Add(new TransferReceiptLinePayload(
                    line.Id, receiverLocationId, line.ItemId, line.ItemVariantId,
                    delta, line.UnitOfMeasure, unitCost, line.UnitCostAtTransferCurrency));
            }
            remaining -= target;
        }

        TransferReceiptPayload payload = new(
            transfer.TenantId, transfer.Id, transfer.ReceiverCompanyId, receiptLines);
        SagaIntent intent = SagaIntent.Create(
            transfer.TenantId,
            TransferReceiptSaga.IntentType,
            $"transfer-receipt:{transfer.Id:N}:{quantity.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            clock.UtcNow,
            JsonSerializer.Serialize(payload));
        intent.Authorize(operatorContext.RequireOperatorId(), principal.Principal, hybridClock.Next().ToString());
        intent.AddLeg(transfer.ReceiverCompanyId);

        SagaResult result = await sagaCoordinator.ExecuteAsync(intent, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException("Transfer receipt is still pending or failed; retry the receipt leg.");
        }

        transfer.Receive(quantity);
    }

    public async Task<PremisesSkuRouting> AddPremisesSkuRoutingAsync(Guid premisesId, string skuOrBarcode, Guid companyId, bool isBarcode, CancellationToken cancellationToken = default)
    {
        bool premisesExists = await registry.Premises.AnyAsync(
            x => x.Id == premisesId && x.TenantId == tenant.TenantId && x.IsActive, cancellationToken).ConfigureAwait(false);
        bool companyExists = await registry.Companies.AnyAsync(
            x => x.Id == companyId && x.TenantId == tenant.TenantId, cancellationToken).ConfigureAwait(false);
        bool occupant = await registry.PremisesOccupancies.AnyAsync(
            x => x.TenantId == tenant.TenantId && x.PremisesId == premisesId && x.CompanyId == companyId && x.OccupiesTo == null,
            cancellationToken).ConfigureAwait(false);
        if (!premisesExists || !companyExists || !occupant)
        {
            throw new KeyNotFoundException("The active premises occupancy was not found.");
        }

        PremisesSkuRouting route = PremisesSkuRouting.Create(tenant.TenantId, premisesId, skuOrBarcode, companyId, isBarcode);
        List<PremisesSkuRouting> existing = await registry.PremisesSkuRoutings
            .Where(x => x.TenantId == tenant.TenantId && x.PremisesId == premisesId && x.IsBarcode == isBarcode)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        PremisesSkuRouting.EnsureUnique(existing, route);
        if (existing.Any(x => string.Equals(x.SkuOrBarcode, route.SkuOrBarcode, StringComparison.OrdinalIgnoreCase) && x.CompanyId == companyId))
        {
            return existing.First(x => string.Equals(x.SkuOrBarcode, route.SkuOrBarcode, StringComparison.OrdinalIgnoreCase) && x.CompanyId == companyId);
        }

        registry.PremisesSkuRoutings.Add(route);
        await registry.CommitAsync(cancellationToken).ConfigureAwait(false);
        return route;
    }

    public async Task<OwnedStockOnHandProjection> PublishOwnedStockProjectionAsync(OwnedStockOnHandProjection projection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projection);
        if (projection.TenantId != tenant.TenantId)
        {
            throw new KeyNotFoundException("The stock projection was not found.");
        }
        GroupHierarchyNode node = await registry.GroupHierarchyNodes.SingleOrDefaultAsync(
            x => x.TenantId == tenant.TenantId && x.BusinessId == projection.BusinessId && x.CompanyId == projection.CompanyId,
            cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("The owned hierarchy node was not found.");
        if (!node.IsVisibilityEligible)
        {
            throw new InvalidOperationException("Franchised stock cannot enter the owned-stock projection.");
        }
        if (projection.OnHand < 0m || projection.Reserved < 0m || projection.InStaging < 0m || projection.Available < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(projection), "Stock quantities cannot be negative.");
        }

        OwnedStockOnHandProjection? existing = await registry.OwnedStockOnHandProjections.SingleOrDefaultAsync(
            x => x.TenantId == tenant.TenantId && x.BusinessId == projection.BusinessId && x.CompanyId == projection.CompanyId
                && x.LocationId == projection.LocationId && x.ItemId == projection.ItemId && x.ItemVariantId == projection.ItemVariantId,
            cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            throw new InvalidOperationException("A stock projection already exists for this company location and SKU.");
        }

        registry.OwnedStockOnHandProjections.Add(projection);
        await registry.CommitAsync(cancellationToken).ConfigureAwait(false);
        return projection;
    }

    public async Task<IReadOnlyList<OwnedStockOnHandProjection>> ListOwnedStockAsync(Guid businessId, Guid? companyId = null, CancellationToken cancellationToken = default)
    {
        return await registry.OwnedStockOnHandProjections.AsNoTracking()
            .Where(x => x.TenantId == tenant.TenantId && x.BusinessId == businessId && (companyId == null || x.CompanyId == companyId))
            .OrderBy(x => x.CompanyId).ThenBy(x => x.LocationId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }
}
