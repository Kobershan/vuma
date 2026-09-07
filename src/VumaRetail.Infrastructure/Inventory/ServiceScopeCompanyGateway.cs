using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Abstractions.Sync;
using VumaRetail.Application.Inventory;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Orders;
using VumaRetail.Domain.Primitives;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Persistence.Repositories;
using VumaRetail.Infrastructure.Registry;

namespace VumaRetail.Infrastructure.Inventory;

/// <summary>
/// Does one company's work inside that company's own database, each call in a child scope bound
/// to the company — one leg, one transaction, never two companies in one scope (ADR-116).
/// </summary>
/// <param name="scopes">Creates one child scope per call.</param>
/// <param name="tenant">The ambient tenant, propagated into each child scope.</param>
/// <param name="logger">Where a failed call is recorded.</param>
/// <remarks>
/// The principal flows through <c>IHttpContextAccessor</c> into child scopes, so legs taken from
/// a signed-in session audit as that user; legs taken by the expiry job or a console audit as the
/// ambient principal there. Either way the saga intent records who asked.
/// </remarks>
public sealed class ServiceScopeCompanyGateway(
    IServiceScopeFactory scopes,
    IReplicationRegistry replication,
    IReplicaWriter replicas,
    IHybridClock hybridClock,
    INodeIdentity node,
    IClock clock,
    ILogger<ServiceScopeCompanyGateway> logger) : ISourcingCompanyGateway
{
    private readonly IClock _clock = clock;
    /// <inheritdoc />
    public async Task<ReserveOutcome> ReserveLegAsync(ReservationLegRequest leg, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(leg);

        using IServiceScope scope = scopes.CreateScope();
        Bind(scope, leg.TenantId, leg.CompanyId);

        IReservationService reservations = scope.ServiceProvider.GetRequiredService<IReservationService>();

        ReserveOutcome outcome = await reservations.ReserveAsync(
            leg.LocationId,
            leg.ItemId,
            leg.ItemVariantId,
            leg.Planned,
            ReservationSource.Order,
            leg.SourceDocumentId,
            leg.GroupDocumentRef,
            leg.ExpiresAt,
            leg.IntentId,
            leg.LegId,
            reason: null,
            cancellationToken).ConfigureAwait(false);

        logger.LogDebug(
            "Reservation leg {LegId} in company {CompanyId} held {Held} short {Shortfall}.",
            leg.LegId,
            leg.CompanyId,
            outcome.Held.Value,
            outcome.Shortfall.Value);

        return outcome;
    }

    /// <inheritdoc />
    public async Task<LocalAvailability> ReadLocalAsync(
        Guid tenantId,
        Guid companyId,
        Guid locationId,
        Guid? itemId,
        Guid? itemVariantId,
        CancellationToken cancellationToken = default)
    {
        using IServiceScope scope = scopes.CreateScope();
        Bind(scope, tenantId, companyId);

        IAvailabilityService availability = scope.ServiceProvider.GetRequiredService<IAvailabilityService>();

        return await availability.GetLocalAsync(locationId, itemId, itemVariantId, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ReleaseHoldAsync(
        Guid tenantId,
        Guid companyId,
        Guid reservationId,
        CancellationToken cancellationToken = default)
    {
        using IServiceScope scope = scopes.CreateScope();
        Bind(scope, tenantId, companyId);

        IReservationService reservations = scope.ServiceProvider.GetRequiredService<IReservationService>();

        await reservations.ReleaseAsync(reservationId, "Sourcing re-resource", cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> ReleaseGroupHoldsAsync(
        Guid tenantId,
        Guid companyId,
        string groupDocumentRef,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupDocumentRef);

        using IServiceScope scope = scopes.CreateScope();
        Bind(scope, tenantId, companyId);

        ICompanyDbContextFactory companies = scope.ServiceProvider.GetRequiredService<ICompanyDbContextFactory>();
        await using VumaRetailDbContext companyDb = await companies.CreateAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var transaction = await companyDb.Database
            .BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            var holds = new StockReservationRepository(companyDb);
            IReadOnlyList<StockReservation> open = await holds
                .ListOpenByGroupRefAsync(groupDocumentRef, cancellationToken)
                .ConfigureAwait(false);

            var balances = new AvailableBalanceRepository(companyDb);
            var audit = scope.ServiceProvider.GetRequiredService<Persistence.Interceptors.AuditStamper>();

            foreach (StockReservation held in open)
            {
                StockReservation released = held.Release("Sourcing compensation");
                companyDb.StockReservations.Add(released);

                AvailableBalance? position = await balances
                    .FindAsync(held.LocationId, held.ItemId, held.ItemVariantId, cancellationToken)
                    .ConfigureAwait(false);
                position?.ApplyClose(held.Quantity);

                CompanyOutboxCapture.Capture(
                    companyDb, replication, replicas, hybridClock, node, clock, released);
            }

            audit.Stamp(companyDb);
            await companyDb.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            audit.Complete();

            return open.Count;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Guid> WriteSplitOrderAsync(
        Guid tenantId,
        Guid companyId,
        string groupDocumentRef,
        SourcingSourceOrder source,
        SplitOrderDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupDocumentRef);

        using IServiceScope scope = scopes.CreateScope();
        Bind(scope, tenantId, companyId);

        ICompanyDbContextFactory companies = scope.ServiceProvider.GetRequiredService<ICompanyDbContextFactory>();
        await using VumaRetailDbContext companyDb = await companies.CreateAsync(cancellationToken)
            .ConfigureAwait(false);

        ITenantContext scopedTenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var audit = scope.ServiceProvider.GetRequiredService<Persistence.Interceptors.AuditStamper>();

        await using var transaction = await companyDb.Database
            .BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            var numbers = new DocumentNumberSequence(companyDb, scopedTenant);
            string orderNumber = await numbers.NextAsync("ORD", cancellationToken).ConfigureAwait(false);

            StockLocation location = await new StockLocationRepository(companyDb)
                .FindAsync(draft.LocationId, cancellationToken).ConfigureAwait(false)
                ?? throw new InventoryNotFoundException("stock location", draft.LocationId);

            SalesOrder order = SalesOrder.Create(
                tenantId,
                location.StoreId,
                orderNumber,
                source.PartnerId,
                source.Channel,
                source.FulfilmentType,
                location.Id,
                source.DeliveryAddress,
                source.Currency,
_clock.UtcNow,
                 requestedFulfilmentDate: null);
            order.AssignGroupDocument(groupDocumentRef);
            order.AssignCompany(companyId);

            foreach (SplitOrderLineDraft line in draft.Lines)
            {
                SalesOrderLine orderLine = order.AddLine(line.ItemId, line.ItemVariantId, line.Quantity);
                orderLine.ApplyPricing(
                    line.UnitPrice,
                    line.DiscountAmount,
                    line.TaxAmount,
                    priceListId: null,
                    promotionsSummary: string.Empty);
                orderLine.RecordAllocationOutcome(new Quantity(0m, line.Quantity.UnitOfMeasure));
            }

            order.Confirm(_clock.UtcNow);

            companyDb.SalesOrders.Add(order);
            foreach (SalesOrderLine orderLine in order.Lines)
            {
                companyDb.SalesOrderLines.Add(orderLine);
            }

            CompanyOutboxCapture.Capture(companyDb, replication, replicas, hybridClock, node, clock, order);
            foreach (SalesOrderLine orderLine in order.Lines)
            {
                CompanyOutboxCapture.Capture(companyDb, replication, replicas, hybridClock, node, clock, orderLine);
            }

            audit.Stamp(companyDb);
            await companyDb.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            audit.Complete();

            logger.LogDebug(
                "Split segment {OrderId} ({OrderNumber}) written in company {CompanyId} for {GroupRef}.",
                order.Id,
                orderNumber,
                companyId,
                groupDocumentRef);

            return order.Id;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private static void Bind(IServiceScope scope, Guid tenantId, Guid companyId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A company call needs its tenant.", nameof(tenantId));
        }

        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("A company call needs its company.", nameof(companyId));
        }

        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().SetCompany(companyId);
    }
}
