using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Abstractions.Sync;
using VumaRetail.Application.Catalog;
using VumaRetail.Application.Inventory;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Sync;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Persistence.Interceptors;
using VumaRetail.Infrastructure.Persistence.Repositories;
using VumaRetail.Infrastructure.Registry;

namespace VumaRetail.Infrastructure.Inventory;

/// <summary>
/// Takes and releases holds on stock, always inside one company's own database, one serialisable
/// transaction with the availability re-check (ADR-102, Stage 08c business rule 1).
/// </summary>
/// <remarks>
/// <para>
/// One instance per operation (scoped): it opens the acting company's context once through
/// <c>ICompanyDbContextFactory</c> — honouring that factory's one-context rule — and reuses it
/// for every call in the scope. A saga with one leg per company gives each leg its own scope and
/// therefore its own instance and its own transaction; there is no shared state to interleave.
/// Like <c>DbContext</c> itself, an instance is not safe for concurrent use.
/// </para>
/// <para>
/// The race this exists to prevent is check-then-act: two orders each reading "5 available" and
/// each holding 5. The defence is a <c>SELECT … FOR UPDATE</c> on the
/// <c>available_balances</c> row inside a serialisable transaction, a reload after the lock, and
/// the re-check after the reload. A concurrent stock movement that commits between the read and
/// the lock either blocks on the lock (then the reload sees it) or aborts this transaction with
/// <c>40001</c> (then the retry sees it). Either way the re-check runs against the truth, and a
/// stale group projection can cause a shortfall but never a negative.
/// </para>
/// <para>
/// Outbox and audit: factory-created contexts carry the audit interceptor (wired by
/// <c>CompanyDbContextFactory</c>), so the stamp and the R6 trail are automatic. Replication
/// rows for the appended reservation entities are captured explicitly through the same
/// primitives <c>OutboxBehaviour</c> uses — the pipeline behaviour only sees the ambient
/// context, which this service deliberately never writes to, so its own transaction stays the
/// only writer and the only committer.
/// </para>
/// </remarks>
public sealed class ReservationService : IReservationService, IAsyncDisposable, IDisposable
{
    private const int MaxAttempts = 3;
    private const int ExpireBatchSize = 100;

    private readonly ICompanyDbContextFactory _companies;
    private readonly IClock _clock;
    private readonly AuditStamper _stamper;
    private readonly IReplicationRegistry _replication;
    private readonly IReplicaWriter _replicas;
    private readonly IHybridClock _hybridClock;
    private readonly INodeIdentity _node;
    private readonly IGroupAvailabilityPublisher? _publisher;
    private readonly ILogger<ReservationService> _logger;

    private VumaRetailDbContext? _companyDb;
    private bool _disposed;

    /// <summary>Builds the service. All collaborators are scoped; the company context is opened lazily.</summary>
    public ReservationService(
        ICompanyDbContextFactory companies,
        IClock clock,
        AuditStamper stamper,
        IReplicationRegistry replication,
        IReplicaWriter replicas,
        IHybridClock hybridClock,
        INodeIdentity node,
        ILogger<ReservationService> logger,
        IGroupAvailabilityPublisher? publisher = null)
    {
        _companies = companies;
        _clock = clock;
        _stamper = stamper;
        _replication = replication;
        _replicas = replicas;
        _hybridClock = hybridClock;
        _node = node;
        _logger = logger;
        _publisher = publisher;
    }

    /// <inheritdoc />
    public Task<ReserveOutcome> ReserveAsync(
        Guid locationId,
        Guid? itemId,
        Guid? itemVariantId,
        Quantity demanded,
        ReservationSource source,
        Guid sourceDocumentId,
        string? groupDocumentRef = null,
        DateTimeOffset? expiresAt = null,
        Guid? intentId = null,
        Guid? legId = null,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(demanded);

        if (locationId == Guid.Empty)
        {
            throw new ArgumentException("A reservation must name a location.", nameof(locationId));
        }

        if (demanded.IsNegative || demanded.IsZero)
        {
            throw InventoryRuleException.QuantityMustBePositive();
        }

        return ExecuteWithRetryAsync(
            () => ReserveOnceAsync(
                locationId, itemId, itemVariantId, demanded, source, sourceDocumentId,
                groupDocumentRef, expiresAt, intentId, legId, reason, cancellationToken),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task ConsumeAsync(Guid reservationId, Guid consumedByReferenceId, CancellationToken cancellationToken = default)
    {
        if (reservationId == Guid.Empty)
        {
            throw new ArgumentException("A reservation is required.", nameof(reservationId));
        }

        if (consumedByReferenceId == Guid.Empty)
        {
            throw new ArgumentException("A consumption must name what consumed the hold.", nameof(consumedByReferenceId));
        }

        return ExecuteWithRetryAsync(
            () => CloseOnceAsync(reservationId, held => held.Consume(consumedByReferenceId, _clock.UtcNow), cancellationToken),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task ReleaseAsync(Guid reservationId, string? reason = null, CancellationToken cancellationToken = default)
    {
        if (reservationId == Guid.Empty)
        {
            throw new ArgumentException("A reservation is required.", nameof(reservationId));
        }

        return ExecuteWithRetryAsync(
            () => CloseOnceAsync(reservationId, held => held.Release(reason), cancellationToken),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> ExpireDueAsync(CancellationToken cancellationToken = default)
        => ExecuteWithRetryAsync(() => ExpireOnceAsync(cancellationToken), cancellationToken);

    private async Task<int> ExpireOnceAsync(CancellationToken cancellationToken)
    {
        VumaRetailDbContext db = await CompanyDbAsync(cancellationToken).ConfigureAwait(false);

        await using var transaction = await db.Database
            .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            DateTimeOffset now = _clock.UtcNow;
            var due = new StockReservationRepository(db);
            IReadOnlyList<StockReservation> holds = await due
                .ListExpiredAsync(now, ExpireBatchSize, cancellationToken)
                .ConfigureAwait(false);

            var balances = new AvailableBalanceRepository(db);
            foreach (StockReservation held in holds)
            {
                StockReservation expired = held.Expire();
                db.StockReservations.Add(expired);

                AvailableBalance? position = await balances
                    .FindAsync(held.LocationId, held.ItemId, held.ItemVariantId, cancellationToken)
                    .ConfigureAwait(false);
                position?.ApplyClose(held.Quantity);

                Capture(expired);
            }

            _stamper.Stamp(db);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            _stamper.Complete();

            return holds.Count;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_companyDb is not null)
        {
            await _companyDb.DisposeAsync().ConfigureAwait(false);
            _companyDb = null;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Scopes are not always disposed asynchronously (<c>using IServiceScope</c> in seeders and
    /// test harnesses), and a scoped service that only implements <c>IAsyncDisposable</c> makes
    /// such a scope throw on dispose. Both paths close the same company context.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _companyDb?.Dispose();
        _companyDb = null;
    }

    private async Task<ReserveOutcome> ReserveOnceAsync(
        Guid locationId,
        Guid? itemId,
        Guid? itemVariantId,
        Quantity demanded,
        ReservationSource source,
        Guid sourceDocumentId,
        string? groupDocumentRef,
        DateTimeOffset? expiresAt,
        Guid? intentId,
        Guid? legId,
        string? reason,
        CancellationToken cancellationToken)
    {
        VumaRetailDbContext db = await CompanyDbAsync(cancellationToken).ConfigureAwait(false);

        await using var transaction = await db.Database
            .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            bool hasItem = itemId is not null && itemId != Guid.Empty;
            bool hasVariant = itemVariantId is not null && itemVariantId != Guid.Empty;
            if (hasItem == hasVariant)
            {
                throw InventoryRuleException.ExactlyOneItemOrVariantRequired();
            }

            var locations = new StockLocationRepository(db);
            StockLocation location = await locations.FindAsync(locationId, cancellationToken).ConfigureAwait(false)
                ?? throw new InventoryNotFoundException("stock location", locationId);

            if (!location.IsActive)
            {
                throw new InventoryRuleException(
                    "INVENTORY_LOCATION_RETIRED",
                    $"Location '{location.Code}' is retired and can hold no new reservations.");
            }

            string unitOfMeasure = await new StockKeepingUnitResolver(
                    new ItemRepository(db),
                    new ItemVariantRepository(db),
                    new UnitOfMeasureRepository(db))
                .ResolveUnitOfMeasureCodeAsync(itemId, itemVariantId, cancellationToken)
                .ConfigureAwait(false);

            if (!string.Equals(unitOfMeasure, demanded.UnitOfMeasure, StringComparison.Ordinal))
            {
                throw InventoryRuleException.UnitOfMeasureMismatch(unitOfMeasure, demanded.UnitOfMeasure);
            }

            // A retried saga leg replays its (intent, leg, line): the unique index turns the replay
            // into this lookup's hit rather than a second hold (ADR-116). Top-ups never replay a leg —
            // a re-sourced remainder is a plain hold under the same group reference, never a second
            // row on the leg's key — so a hit here is always the idempotent answer, never a stale one.
            if (intentId.HasValue && legId.HasValue)
            {
                StockReservation? replayed = await new StockReservationRepository(db).FindLegHoldAsync(
                        intentId.Value, legId.Value, locationId, itemId, itemVariantId, cancellationToken)
                    .ConfigureAwait(false);
                if (replayed is not null)
                {
                    Quantity after = await ReadAvailableAsync(db, locationId, itemId, itemVariantId, unitOfMeasure, cancellationToken)
                        .ConfigureAwait(false);
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return new ReserveOutcome(replayed.ReservationId, replayed.Quantity, demanded - replayed.Quantity, after, _clock.UtcNow);
                }
            }

            var balances = new AvailableBalanceRepository(db);
            AvailableBalance position = await OpenOrFindAsync(db, balances, location, itemId, itemVariantId, unitOfMeasure, cancellationToken)
                .ConfigureAwait(false);

            await LockAsync(db, position.Id, cancellationToken).ConfigureAwait(false);
            await db.Entry(position).ReloadAsync(cancellationToken).ConfigureAwait(false);

            Quantity onHand = await ReadOnHandAsync(db, locationId, itemId, itemVariantId, unitOfMeasure, cancellationToken)
                .ConfigureAwait(false);
            Quantity inStaging = await new EfStagingQuantityReader(db).ReadStagingAsync(
                    locationId, itemId, itemVariantId, unitOfMeasure, cancellationToken)
                .ConfigureAwait(false);
            position.RefreshStaging(inStaging);

            Quantity available = onHand - position.Reserved - position.InStaging;
            Quantity holdQuantity = available.Value <= 0m
                ? Quantity.Zero(unitOfMeasure)
                : available.Value >= demanded.Value ? demanded : available;

            StockReservation? hold = null;
            if (!holdQuantity.IsZero)
            {
                Guid companyId = location.CompanyId ?? db.CurrentCompanyId
                    ?? throw new InvalidOperationException("The acting company is unknown; a hold must belong to a company.");
                hold = StockReservation.Hold(
                    location.TenantId,
                    location.StoreId,
                    companyId,
                    locationId,
                    itemId,
                    itemVariantId,
                    holdQuantity,
                    source,
                    sourceDocumentId,
                    groupDocumentRef,
                    expiresAt,
                    intentId,
                    legId,
                    reason);
                db.StockReservations.Add(hold);
                position.ApplyHold(holdQuantity);
                Capture(hold);
            }

            _stamper.Stamp(db);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            _stamper.Complete();

            Quantity remaining = onHand - position.Reserved - position.InStaging;
            var outcome = new ReserveOutcome(
                hold?.ReservationId,
                holdQuantity,
                demanded - holdQuantity,
                remaining,
                _clock.UtcNow);

            await PublishAsync(db, location, itemId, itemVariantId, onHand, position, unitOfMeasure, cancellationToken)
                .ConfigureAwait(false);

            return outcome;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private async Task CloseOnceAsync(
        Guid reservationId,
        Func<StockReservation, StockReservation> close,
        CancellationToken cancellationToken)
    {
        VumaRetailDbContext db = await CompanyDbAsync(cancellationToken).ConfigureAwait(false);

        await using var transaction = await db.Database
            .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            var held = new StockReservationRepository(db);
            StockReservation? open = await held.FindOpenAsync(reservationId, cancellationToken).ConfigureAwait(false);

            if (open is null)
            {
                throw await ClosedChainExceptionAsync(db, reservationId, cancellationToken).ConfigureAwait(false);
            }

            var balances = new AvailableBalanceRepository(db);
            AvailableBalance? position = await balances
                .FindAsync(open.LocationId, open.ItemId, open.ItemVariantId, cancellationToken)
                .ConfigureAwait(false);

            if (position is not null)
            {
                await LockAsync(db, position.Id, cancellationToken).ConfigureAwait(false);
                await db.Entry(position).ReloadAsync(cancellationToken).ConfigureAwait(false);
            }

            StockReservation terminal = close(open);
            db.StockReservations.Add(terminal);
            position?.ApplyClose(open.Quantity);

            // Only the new terminal row is captured: re-stamping the already-persisted open row
            // would flip it to Modified and trip the immutable-record guard. The open row needs
            // no new outbox row — nothing about it changed; its state is derived from the chain.
            Capture(terminal);
            _stamper.Stamp(db);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            _stamper.Complete();

            if (position is not null)
            {
                StockLocation? location = await new StockLocationRepository(db)
                    .FindAsync(open.LocationId, cancellationToken)
                    .ConfigureAwait(false);
                if (location is not null)
                {
                    Quantity onHand = await ReadOnHandAsync(
                            db, open.LocationId, open.ItemId, open.ItemVariantId,
                            open.Quantity.UnitOfMeasure, cancellationToken)
                        .ConfigureAwait(false);
                    await PublishAsync(db, location, open.ItemId, open.ItemVariantId, onHand, position, open.Quantity.UnitOfMeasure, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<Exception> ClosedChainExceptionAsync(
        VumaRetailDbContext db,
        Guid reservationId,
        CancellationToken cancellationToken)
    {
        bool exists = await db.StockReservations
            .AnyAsync(reservation => reservation.ReservationId == reservationId, cancellationToken)
            .ConfigureAwait(false);

        return exists
            ? InventoryRuleException.ReservationNotHeld(reservationId, ReservationState.Released)
            : new InventoryNotFoundException("stock reservation", reservationId);
    }

    private static async Task<AvailableBalance> OpenOrFindAsync(
        VumaRetailDbContext db,
        AvailableBalanceRepository balances,
        StockLocation location,
        Guid? itemId,
        Guid? itemVariantId,
        string unitOfMeasure,
        CancellationToken cancellationToken)
    {
        AvailableBalance? position = await balances
            .FindAsync(location.Id, itemId, itemVariantId, cancellationToken)
            .ConfigureAwait(false);

        if (position is not null)
        {
            return position;
        }

        var opened = AvailableBalance.Open(
            location.TenantId,
            location.StoreId,
            location.CompanyId ?? db.CurrentCompanyId
                ?? throw new InvalidOperationException("The acting company is unknown; a position must belong to a company."),
            location.Id,
            itemId,
            itemVariantId,
            unitOfMeasure);
        balances.Add(opened);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return opened;
    }

    private static async Task LockAsync(VumaRetailDbContext db, Guid positionId, CancellationToken cancellationToken)
    {
        // SqlQuery maps a scalar result by the column name "Value" — hence the alias.
        _ = await db.Database
            .SqlQuery<Guid>($"SELECT id AS \"Value\" FROM inventory.available_balances WHERE id = {positionId} FOR UPDATE")
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<Quantity> ReadOnHandAsync(
        VumaRetailDbContext db,
        Guid locationId,
        Guid? itemId,
        Guid? itemVariantId,
        string unitOfMeasure,
        CancellationToken cancellationToken)
    {
        StockBalance? balance = await new StockBalanceRepository(db)
            .FindAsync(locationId, itemId, itemVariantId, cancellationToken)
            .ConfigureAwait(false);

        if (balance is null)
        {
            return Quantity.Zero(unitOfMeasure);
        }

        if (!string.Equals(balance.QuantityOnHand.UnitOfMeasure, unitOfMeasure, StringComparison.Ordinal))
        {
            throw InventoryRuleException.UnitOfMeasureMismatch(balance.QuantityOnHand.UnitOfMeasure, unitOfMeasure);
        }

        return balance.QuantityOnHand;
    }

    private static async Task<Quantity> ReadAvailableAsync(
        VumaRetailDbContext db,
        Guid locationId,
        Guid? itemId,
        Guid? itemVariantId,
        string unitOfMeasure,
        CancellationToken cancellationToken)
    {
        Quantity onHand = await ReadOnHandAsync(db, locationId, itemId, itemVariantId, unitOfMeasure, cancellationToken)
            .ConfigureAwait(false);
        AvailableBalance? position = await new AvailableBalanceRepository(db)
            .FindAsync(locationId, itemId, itemVariantId, cancellationToken)
            .ConfigureAwait(false);
        Quantity reserved = position?.Reserved ?? Quantity.Zero(unitOfMeasure);
        Quantity staging = position?.InStaging ?? Quantity.Zero(unitOfMeasure);
        return onHand - reserved - staging;
    }

    private async Task PublishAsync(
        VumaRetailDbContext db,
        StockLocation location,
        Guid? itemId,
        Guid? itemVariantId,
        Quantity onHand,
        AvailableBalance position,
        string unitOfMeasure,
        CancellationToken cancellationToken)
    {
        if (_publisher is null)
        {
            return;
        }

        try
        {
            await _publisher.PublishAsync(
                new AvailabilitySnapshot(
                    location.TenantId,
                    location.CompanyId ?? db.CurrentCompanyId
                        ?? throw new InvalidOperationException("The acting company is unknown."),
                    location.Id,
                    itemId,
                    itemVariantId,
                    onHand.Value,
                    position.Reserved.Value,
                    position.InStaging.Value,
                    unitOfMeasure,
                    _clock.UtcNow),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // The hold already committed in the company's database; a registry that is down must
            // never unwind it (MULTI_COMPANY.md §3: registry down means keep trading locally).
            // The relay heals the projection from the outbox, and the stale threshold discloses
            // the gap until it does.
            _logger.LogWarning(exception, "Availability publish failed for location {LocationId}; the relay will heal it.", location.Id);
        }
    }

    private void Capture(params StockReservation[] rows)
    {
        VumaRetailDbContext db = _companyDb
            ?? throw new InvalidOperationException("The company database is not open.");

        CompanyOutboxCapture.Capture(db, _replication, _replicas, _hybridClock, _node, _clock, rows);
    }

    private async Task<TResult> ExecuteWithRetryAsync<TResult>(
        Func<Task<TResult>> body,
        CancellationToken cancellationToken)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await body().ConfigureAwait(false);
            }
            catch (Exception exception) when (IsTransient(exception) && attempt < MaxAttempts)
            {
                _logger.LogInformation(
                    "Reservation transaction attempt {Attempt} hit a transient conflict; retrying.",
                    attempt);

                // The rolled-back attempt's tracked entities would otherwise be re-added as
                // duplicates on the next attempt. Clearing detaches them; the retry re-reads
                // everything, and the stamper's per-save set is reset with the tracker.
                if (_companyDb is not null)
                {
                    _companyDb.ChangeTracker.Clear();
                }

                _stamper.Complete();
            }
        }
    }

    private Task ExecuteWithRetryAsync(Func<Task> body, CancellationToken cancellationToken)
        => ExecuteWithRetryAsync(async () => { await body().ConfigureAwait(false); return true; }, cancellationToken);

    // SaveChanges wraps provider errors in DbUpdateException, and EF's execution strategy wraps
    // suspected-transient provider errors in InvalidOperationException on top of that — so the
    // SQLSTATE hunt walks the whole chain. A 40001/40P01/23505 anywhere in it means the database
    // reported a transient conflict for this operation; anything else is a real bug and propagates.
    private static bool IsTransient(Exception exception)
    {
        Exception? current = exception;
        while (current is not null)
        {
            if (current is PostgresException postgres
                && postgres.SqlState is "40001" or "40P01" or "23505")
            {
                return true;
            }

            current = current.InnerException;
        }

        return false;
    }

    private async Task<VumaRetailDbContext> CompanyDbAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ReservationService));
        }

        if (_companyDb is null)
        {
            _companyDb = await _companies.CreateAsync(cancellationToken).ConfigureAwait(false);
        }

        return _companyDb;
    }
}
