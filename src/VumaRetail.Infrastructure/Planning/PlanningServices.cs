using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Planning;
using VumaRetail.Application.Abstractions.Sales;
using VumaRetail.Application.Inventory;
using VumaRetail.Application.Inventory.Commands;
using VumaRetail.Application.Procurement.Commands;
using VumaRetail.Application.Sales.Commands;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Sales;
using VumaRetail.Infrastructure.Persistence;

namespace VumaRetail.Infrastructure.Planning;

/// <summary>Reads sale issues out of the stock ledger for the demand rollup (Stage 15).</summary>
public sealed class DemandHistorySource(VumaRetailDbContext context) : Application.Planning.IDemandHistorySource
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Application.Planning.SaleIssueRecord>> ListSaleIssuesAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
        => await context.StockLedgerEntries.AsNoTracking()
            .Where(entry => entry.MovementType == StockMovementType.SaleIssue
                && entry.CreatedAt >= from
                && entry.CreatedAt <= to
                && entry.CompanyId != null)
            .OrderBy(entry => entry.CreatedAt)
            .Select(entry => new Application.Planning.SaleIssueRecord(
                entry.CompanyId!.Value,
                entry.LocationId,
                entry.ItemId,
                entry.ItemVariantId,
                entry.Quantity.Value,
                entry.Quantity.UnitOfMeasure,
                entry.CreatedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
}

/// <summary>Raises Stage 12 requisitions for planning accepts, through Stage 12's own commands.</summary>
public sealed class PlanningProcurementWriter(
    ICommandHandler<CreatePurchaseRequisitionCommand, Guid> create,
    ICommandHandler<AddPurchaseRequisitionLineCommand, Guid> addLine) : IPlanningProcurementWriter
{
    /// <inheritdoc />
    public async Task<Guid> RaiseRequisitionAsync(
        Guid? locationId,
        DateOnly requiredBy,
        string justification,
        IReadOnlyList<PlannedRequisitionLine> lines,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lines);

        Guid requisitionId = await create
            .HandleAsync(new CreatePurchaseRequisitionCommand(locationId, requiredBy, justification), cancellationToken)
            .ConfigureAwait(false);

        foreach (PlannedRequisitionLine line in lines)
        {
            await addLine
                .HandleAsync(
                    new AddPurchaseRequisitionLineCommand(
                        requisitionId,
                        line.ItemId,
                        line.ItemVariantId,
                        line.Description,
                        new Quantity(line.Quantity, line.Uom),
                        line.EstimatedUnitCost),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return requisitionId;
    }
}

/// <summary>Moves stock for planning accepts, through Stage 08's transfer command.</summary>
public sealed class PlanningTransferWriter(
    ICommandHandler<TransferStockCommand, Guid> transfer) : IPlanningTransferWriter
{
    /// <inheritdoc />
    public Task<Guid> TransferAsync(
        Guid sourceLocationId,
        Guid destinationLocationId,
        Guid? itemId,
        Guid? itemVariantId,
        decimal quantity,
        string uom,
        string? note,
        CancellationToken cancellationToken = default)
        => transfer.HandleAsync(
            new TransferStockCommand(
                sourceLocationId, destinationLocationId, itemId, itemVariantId,
                new Quantity(quantity, uom), note),
            cancellationToken);
}

/// <summary>Creates and retires SKU promotions for planning, through Stage 10's own commands.</summary>
public sealed class PlanningPromotionWriter(
    ICommandHandler<CreatePromotionCommand, Guid> create,
    ICommandHandler<AddPromotionLineCommand, Guid> addLine,
    ICommandHandler<ActivatePromotionCommand, Unit> activate,
    ICommandHandler<DeactivatePromotionCommand, Unit> deactivate) : IPlanningPromotionWriter
{
    /// <inheritdoc />
    public async Task<Guid> CreateSkuPromotionAsync(
        string code,
        string name,
        decimal discountPercent,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo,
        Guid? storeId,
        Guid? itemId,
        Guid? itemVariantId,
        CancellationToken cancellationToken = default)
    {
        Guid promotionId = await create
            .HandleAsync(
                new CreatePromotionCommand(
                    code, name, PromotionKind.PercentageOff, effectiveFrom, effectiveTo,
                    DiscountPercentage: discountPercent, StoreId: storeId),
                cancellationToken)
            .ConfigureAwait(false);

        await addLine
            .HandleAsync(new AddPromotionLineCommand(promotionId, itemId, itemVariantId), cancellationToken)
            .ConfigureAwait(false);

        await activate
            .HandleAsync(new ActivatePromotionCommand(promotionId), cancellationToken)
            .ConfigureAwait(false);

        return promotionId;
    }

    /// <inheritdoc />
    public Task DeactivatePromotionAsync(Guid promotionId, CancellationToken cancellationToken = default)
        => deactivate.HandleAsync(new DeactivatePromotionCommand(promotionId), cancellationToken);
}

/// <summary>Resolves live prices off the winning price list and average cost off the balance.</summary>
public sealed class PlanningPriceReader(
    IPriceListRepository priceLists,
    IStockBalanceRepository balances) : IPlanningPriceReader
{
    /// <inheritdoc />
    public async Task<PriceSnapshot?> TryReadAsync(
        Guid locationId,
        Guid? itemId,
        Guid? itemVariantId,
        Guid? storeId,
        DateOnly onDate,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PriceList> candidates = await priceLists
            .ListCandidatesAsync(itemId, itemVariantId, storeId, onDate, cancellationToken)
            .ConfigureAwait(false);

        PriceListLine? line = candidates
            .SelectMany(list => list.Lines)
            .FirstOrDefault(candidate => candidate.ItemId == itemId && candidate.ItemVariantId == itemVariantId);

        if (line is null)
        {
            return null;
        }

        StockBalance? balance = await balances
            .FindAsync(locationId, itemId, itemVariantId, cancellationToken)
            .ConfigureAwait(false);

        return new PriceSnapshot(
            line.UnitPrice.Amount,
            line.UnitPrice.Currency,
            balance?.AverageCost.Amount);
    }
}
