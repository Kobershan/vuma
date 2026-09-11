using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Inventory;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Persistence.Repositories;

namespace VumaRetail.Infrastructure.Registry;

/// <summary>
/// Posts receiver-company transfer receipts at the cost fixed by the sender shipment. Each
/// cumulative receipt delta has a deterministic ledger reference, so retries cannot duplicate it.
/// </summary>
public sealed class TransferReceiptSagaLegDispatcher(IServiceScopeFactory scopes) : ISagaLegDispatcher
{
    public bool CanDispatch(string intentType) => intentType == TransferReceiptSaga.IntentType;

    public async Task DispatchAsync(SagaIntent intent, SagaLeg leg, CancellationToken cancellationToken = default)
    {
        TransferReceiptPayload payload = ReadPayload(intent);
        if (payload.TenantId != intent.TenantId || payload.ReceiverCompanyId != leg.CompanyId)
        {
            throw new InvalidOperationException("The transfer receipt payload does not match its saga leg.");
        }

        using IServiceScope scope = scopes.CreateScope();
        Bind(scope, intent.TenantId, leg.CompanyId);
        ICompanyDbContextFactory databases = scope.ServiceProvider.GetRequiredService<ICompanyDbContextFactory>();
        await using VumaRetailDbContext db = await databases.CreateAsync(cancellationToken).ConfigureAwait(false);
        IStockLedgerRepository ledger = new StockLedgerRepository(db);
        IStockLedgerPoster poster = new StockLedgerPoster(
            new StockBalanceRepository(db),
            ledger,
            scope.ServiceProvider.GetRequiredService<IInventoryValuationEventPublisher>(),
            scope.ServiceProvider.GetRequiredService<IClock>());

        foreach (TransferReceiptLinePayload line in payload.Lines)
        {
            Guid receiptReferenceId = ReceiptReference(payload.TransferId, line.LineId, line.Quantity);
            IReadOnlyList<StockLedgerEntry> existing = await ledger
                .ListByReferenceAsync(StockReferenceType.Transfer, receiptReferenceId, cancellationToken)
                .ConfigureAwait(false);
            if (existing.Any(entry => entry.MovementType == StockMovementType.TransferIn))
            {
                continue;
            }

            StockLocation location = await db.StockLocations
                .SingleOrDefaultAsync(x => x.Id == line.LocationId, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Transfer receiver location {line.LocationId} was not found.");

            await poster.ReceiveForTransferAsync(
                location,
                line.ItemId,
                line.ItemVariantId,
                new Quantity(line.Quantity, line.UnitOfMeasure),
                new Money(line.UnitCost, line.Currency),
                receiptReferenceId,
                $"Transfer {payload.TransferId:N}",
                cancellationToken,
                batchReference: line.BatchReference,
                expiryDate: line.ExpiryDate,
                serialNumber: line.SerialNumber).ConfigureAwait(false);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task CompensateAsync(SagaIntent intent, SagaLeg leg, CancellationToken cancellationToken = default)
    {
        TransferReceiptPayload payload = ReadPayload(intent);
        if (payload.ReceiverCompanyId != leg.CompanyId)
        {
            throw new InvalidOperationException("The transfer receipt compensation payload does not match its saga leg.");
        }

        using IServiceScope scope = scopes.CreateScope();
        Bind(scope, intent.TenantId, leg.CompanyId);
        ICompanyDbContextFactory databases = scope.ServiceProvider.GetRequiredService<ICompanyDbContextFactory>();
        await using VumaRetailDbContext db = await databases.CreateAsync(cancellationToken).ConfigureAwait(false);
        IStockLedgerRepository ledger = new StockLedgerRepository(db);
        IStockLedgerPoster poster = new StockLedgerPoster(
            new StockBalanceRepository(db),
            ledger,
            scope.ServiceProvider.GetRequiredService<IInventoryValuationEventPublisher>(),
            scope.ServiceProvider.GetRequiredService<IClock>());

        foreach (TransferReceiptLinePayload line in payload.Lines)
        {
            Guid receiptReferenceId = ReceiptReference(payload.TransferId, line.LineId, line.Quantity);
            IReadOnlyList<StockLedgerEntry> entries = await ledger
                .ListByReferenceAsync(StockReferenceType.Transfer, receiptReferenceId, cancellationToken)
                .ConfigureAwait(false);
            StockLedgerEntry? receipt = entries.SingleOrDefault(entry => entry.MovementType == StockMovementType.TransferIn);
            if (receipt is null)
            {
                continue;
            }

            Guid reversalReferenceId = ReversalReference(receiptReferenceId);
            if (entries.Any(entry => entry.MovementType == StockMovementType.TransferOut
                && entry.ReferenceId == reversalReferenceId))
            {
                continue;
            }

            StockLocation location = await db.StockLocations
                .SingleOrDefaultAsync(x => x.Id == line.LocationId, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Transfer receiver location {line.LocationId} was not found.");
            await poster.ReverseTransferReceiptAsync(
                location,
                line.ItemId,
                line.ItemVariantId,
                new Quantity(line.Quantity, line.UnitOfMeasure),
                reversalReferenceId,
                $"Reverse transfer receipt {payload.TransferId:N}",
                cancellationToken,
                batchReference: line.BatchReference,
                expiryDate: line.ExpiryDate,
                serialNumber: line.SerialNumber).ConfigureAwait(false);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static TransferReceiptPayload ReadPayload(SagaIntent intent)
        => JsonSerializer.Deserialize<TransferReceiptPayload>(intent.Payload)
            ?? throw new InvalidOperationException("The transfer receipt payload is invalid.");

    private static Guid ReceiptReference(Guid transferId, Guid lineId, decimal quantity)
        => StableGuid($"receipt|{transferId:N}|{lineId:N}|{quantity.ToString(System.Globalization.CultureInfo.InvariantCulture)}");

    private static Guid ReversalReference(Guid receiptReferenceId) => StableGuid($"reversal|{receiptReferenceId:N}");

    private static Guid StableGuid(string value)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static void Bind(IServiceScope scope, Guid tenantId, Guid companyId)
    {
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().SetCompany(companyId);
    }
}
