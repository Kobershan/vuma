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
/// Ships each transfer line from the sender's company database. The ledger reference is the
/// transfer-line id, making a retried leg idempotent even when one transfer contains many lines.
/// </summary>
public sealed class TransferShipmentSagaLegDispatcher(IServiceScopeFactory scopes) : ISagaLegDispatcher
{
    public bool CanDispatch(string intentType) => intentType == TransferShipmentSaga.IntentType;

    public async Task DispatchAsync(SagaIntent intent, SagaLeg leg, CancellationToken cancellationToken = default)
    {
        TransferShipmentPayload payload = ReadPayload(intent);
        if (payload.TenantId != intent.TenantId || payload.SenderCompanyId != leg.CompanyId)
        {
            throw new InvalidOperationException("The transfer shipment payload does not match its saga leg.");
        }

        using IServiceScope scope = scopes.CreateScope();
        Bind(scope, intent.TenantId, leg.CompanyId);
        ICompanyDbContextFactory databases = scope.ServiceProvider.GetRequiredService<ICompanyDbContextFactory>();
        await using VumaRetailDbContext db = await databases.CreateAsync(cancellationToken).ConfigureAwait(false);
        IStockLedgerRepository ledger = new StockLedgerRepository(db);
        IStockReservationRepository reservationReader = new StockReservationRepository(db);
        IReservationService reservations = scope.ServiceProvider.GetRequiredService<IReservationService>();
        IStockLedgerPoster poster = new StockLedgerPoster(
            new StockBalanceRepository(db),
            ledger,
            scope.ServiceProvider.GetRequiredService<IInventoryValuationEventPublisher>(),
            scope.ServiceProvider.GetRequiredService<IClock>());

        foreach (TransferReservationLinePayload line in payload.Lines)
        {
            IReadOnlyList<StockLedgerEntry> existing = await ledger
                .ListByReferenceAsync(StockReferenceType.Transfer, line.LineId, cancellationToken)
                .ConfigureAwait(false);
            StockLedgerEntry? outbound = existing.SingleOrDefault(x => x.MovementType == StockMovementType.TransferOut);

            IReadOnlyList<StockReservation> holds = await reservationReader
                .ListOpenByGroupRefAsync($"transfer:{payload.TransferId:N}", cancellationToken)
                .ConfigureAwait(false);
            StockReservation? hold = holds.FirstOrDefault(x =>
                x.LocationId == line.LocationId
                && x.ItemId == line.ItemId
                && x.ItemVariantId == line.ItemVariantId
                && x.Quantity.Value == line.Quantity
                && string.Equals(x.Quantity.UnitOfMeasure, line.UnitOfMeasure, StringComparison.OrdinalIgnoreCase));

            if (outbound is null)
            {
                StockLocation location = await db.StockLocations
                    .SingleOrDefaultAsync(x => x.Id == line.LocationId, cancellationToken)
                    .ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"Transfer source location {line.LocationId} was not found.");

                outbound = await poster.IssueForTransferAsync(
                    location,
                    line.ItemId,
                    line.ItemVariantId,
                    new Quantity(line.Quantity, line.UnitOfMeasure),
                    line.LineId,
                    $"Transfer {payload.TransferId:N}",
                    cancellationToken).ConfigureAwait(false);
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            if (hold is not null)
            {
                await reservations.ConsumeAsync(hold.ReservationId, line.LineId, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task CompensateAsync(SagaIntent intent, SagaLeg leg, CancellationToken cancellationToken = default)
    {
        TransferShipmentPayload payload = ReadPayload(intent);
        if (payload.SenderCompanyId != leg.CompanyId)
        {
            throw new InvalidOperationException("The transfer shipment compensation payload does not match its saga leg.");
        }

        using IServiceScope scope = scopes.CreateScope();
        Bind(scope, intent.TenantId, leg.CompanyId);
        ICompanyDbContextFactory databases = scope.ServiceProvider.GetRequiredService<ICompanyDbContextFactory>();
        await using VumaRetailDbContext db = await databases.CreateAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<StockLedgerEntry> shipped = await db.StockLedgerEntries
            .Where(x => x.ReferenceType == StockReferenceType.Transfer
                && payload.Lines.Select(line => line.LineId).Contains(x.ReferenceId!.Value)
                && x.MovementType == StockMovementType.TransferOut)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (shipped.Count > 0)
        {
            throw new InvalidOperationException("A shipped transfer cannot be compensated without a reversal document.");
        }

        IReservationService reservations = scope.ServiceProvider.GetRequiredService<IReservationService>();
        IReadOnlyList<StockReservation> holds = await new StockReservationRepository(db)
            .ListOpenByGroupRefAsync($"transfer:{payload.TransferId:N}", cancellationToken)
            .ConfigureAwait(false);
        foreach (StockReservation hold in holds.Where(x => x.IntentId == intent.Id && x.LegId == leg.LegId))
        {
            await reservations.ReleaseAsync(hold.ReservationId, "Transfer shipment saga compensation", cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static TransferShipmentPayload ReadPayload(SagaIntent intent)
        => JsonSerializer.Deserialize<TransferShipmentPayload>(intent.Payload)
            ?? throw new InvalidOperationException("The transfer shipment payload is invalid.");

    private static void Bind(IServiceScope scope, Guid tenantId, Guid companyId)
    {
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().SetCompany(companyId);
    }
}
