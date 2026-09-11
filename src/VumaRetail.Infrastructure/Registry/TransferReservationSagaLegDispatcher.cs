using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Inventory;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Registry;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Persistence.Repositories;

namespace VumaRetail.Infrastructure.Registry;

/// <summary>
/// Reserves and compensates one transfer source leg in one company database. The registry
/// transfer never opens a company context directly; every company write is isolated here.
/// </summary>
public sealed class TransferReservationSagaLegDispatcher(
    IServiceScopeFactory scopes) : ISagaLegDispatcher
{
    public bool CanDispatch(string intentType) => intentType == TransferReservationSaga.IntentType;

    public async Task DispatchAsync(SagaIntent intent, SagaLeg leg, CancellationToken cancellationToken = default)
    {
        TransferReservationPayload payload = ReadPayload(intent);
        if (payload.TenantId != intent.TenantId || payload.SenderCompanyId != leg.CompanyId)
        {
            throw new InvalidOperationException("The transfer reservation payload does not match its saga leg.");
        }

        using IServiceScope scope = scopes.CreateScope();
        Bind(scope, intent.TenantId, leg.CompanyId);
        IReservationService reservations = scope.ServiceProvider.GetRequiredService<IReservationService>();

        foreach (TransferReservationLinePayload line in payload.Lines)
        {
            ReserveOutcome outcome = await reservations.ReserveAsync(
                line.LocationId,
                line.ItemId,
                line.ItemVariantId,
                new Domain.Primitives.Quantity(line.Quantity, line.UnitOfMeasure),
                ReservationSource.Transfer,
                payload.TransferId,
                $"transfer:{payload.TransferId:N}",
                intentId: intent.Id,
                legId: leg.LegId,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!outcome.Shortfall.IsZero)
            {
                throw new InvalidOperationException($"Insufficient source stock for transfer line {line.LineId}.");
            }
        }
    }

    public async Task CompensateAsync(SagaIntent intent, SagaLeg leg, CancellationToken cancellationToken = default)
    {
        TransferReservationPayload payload = ReadPayload(intent);
        if (payload.SenderCompanyId != leg.CompanyId)
        {
            throw new InvalidOperationException("The transfer compensation payload does not match its saga leg.");
        }

        using IServiceScope scope = scopes.CreateScope();
        Bind(scope, intent.TenantId, leg.CompanyId);
        ICompanyDbContextFactory databases = scope.ServiceProvider.GetRequiredService<ICompanyDbContextFactory>();
        await using VumaRetailDbContext db = await databases.CreateAsync(cancellationToken).ConfigureAwait(false);
        IReservationService reservations = scope.ServiceProvider.GetRequiredService<IReservationService>();
        IReadOnlyList<StockReservation> holds = await new StockReservationRepository(db)
            .ListOpenByGroupRefAsync($"transfer:{payload.TransferId:N}", cancellationToken)
            .ConfigureAwait(false);

        foreach (StockReservation hold in holds.Where(x => x.IntentId == intent.Id && x.LegId == leg.LegId))
        {
            await reservations.ReleaseAsync(hold.ReservationId, "Transfer reservation saga compensation", cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static TransferReservationPayload ReadPayload(SagaIntent intent)
        => JsonSerializer.Deserialize<TransferReservationPayload>(intent.Payload)
            ?? throw new InvalidOperationException("The transfer reservation payload is invalid.");

    private static void Bind(IServiceScope scope, Guid tenantId, Guid companyId)
    {
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().SetCompany(companyId);
    }
}
