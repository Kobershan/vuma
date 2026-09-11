using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Inventory;
using VumaRetail.Domain.Inventory;

namespace VumaRetail.Infrastructure.Persistence.Repositories;

/// <summary>EF Core implementation of <see cref="IStockLocationRepository"/>.</summary>
/// <param name="context">The database context.</param>
public sealed class StockLocationRepository(VumaRetailDbContext context) : IStockLocationRepository
{
    /// <inheritdoc />
    public Task<StockLocation?> FindAsync(Guid locationId, CancellationToken cancellationToken = default)
        => context.StockLocations.FirstOrDefaultAsync(location => location.Id == locationId, cancellationToken);

    /// <inheritdoc />
    public Task<StockLocation?> FindByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        string normalized = code.Trim().ToUpperInvariant();

        return context.StockLocations.FirstOrDefaultAsync(location => location.Code == normalized, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StockLocation>> ListAsync(CancellationToken cancellationToken = default)
        => await context.StockLocations
            .AsNoTracking()
            .OrderBy(location => location.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(StockLocation location) => context.StockLocations.Add(location);
}

/// <summary>EF Core implementation of <see cref="IStockLedgerRepository"/>.</summary>
/// <param name="context">The database context.</param>
/// <remarks>
/// There is deliberately no <c>Remove</c> and no update path. The table is append-only (ADR-005) and
/// <c>AuditInterceptor</c> refuses any modification to an <c>IImmutableRecord</c> — this repository
/// simply gives that no surface to be called through.
/// </remarks>
public sealed class StockLedgerRepository(VumaRetailDbContext context) : IStockLedgerRepository
{
    /// <inheritdoc />
    public async Task<(IReadOnlyList<StockLedgerEntry> Entries, bool HasMore)> ListPageAsync(
        Guid locationId,
        Guid? itemId,
        Guid? itemVariantId,
        KeysetCursor? after,
        int limit,
        CancellationToken cancellationToken = default)
    {
        IQueryable<StockLedgerEntry> query = context.StockLedgerEntries
            .AsNoTracking()
            .Where(entry => entry.LocationId == locationId);

        if (itemId is { } item)
        {
            query = query.Where(entry => entry.ItemId == item);
        }

        if (itemVariantId is { } variant)
        {
            query = query.Where(entry => entry.ItemVariantId == variant);
        }

        if (after is { } cursor && DateTimeOffset.TryParse(
                cursor.SortKey,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out DateTimeOffset createdAt))
        {
            // Newest first, so "the next page" is everything strictly older than the cursor — the
            // mirror image of the ascending keyset ItemRepository uses (docs/API_STANDARDS.md §8).
            // Id breaks the tie, so two entries posted in the same tick can never straddle a page
            // boundary and be repeated or skipped.
            query = query.Where(entry => entry.CreatedAt < createdAt
                || (entry.CreatedAt == createdAt && entry.Id.CompareTo(cursor.Id) < 0));
        }

        List<StockLedgerEntry> page = await query
            .OrderByDescending(entry => entry.CreatedAt)
            .ThenByDescending(entry => entry.Id)
            .Take(limit + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        bool hasMore = page.Count > limit;

        if (hasMore)
        {
            page.RemoveAt(page.Count - 1);
        }

        return (page, hasMore);
    }

    /// <inheritdoc />
    public Task<StockLedgerEntry?> FindAsync(Guid ledgerEntryId, CancellationToken cancellationToken = default)
        => context.StockLedgerEntries.FirstOrDefaultAsync(entry => entry.Id == ledgerEntryId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<StockLedgerEntry>> ListByReferenceAsync(
        StockReferenceType referenceType, Guid referenceId, CancellationToken cancellationToken = default)
        => await context.StockLedgerEntries
            .AsNoTracking()
            .Where(entry => entry.ReferenceType == referenceType && entry.ReferenceId == referenceId)
            .OrderBy(entry => entry.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<decimal> SumTrackedQuantityAsync(
        Guid locationId,
        Guid? itemId,
        Guid? itemVariantId,
        string? batchReference,
        DateOnly? expiryDate,
        string? serialNumber,
        CancellationToken cancellationToken = default)
    {
        string? batch = StockTracking.Normalise(batchReference, nameof(batchReference));
        string? serial = StockTracking.Normalise(serialNumber, nameof(serialNumber));
        return await context.StockLedgerEntries
            .Where(entry => entry.LocationId == locationId
                && entry.ItemId == itemId
                && entry.ItemVariantId == itemVariantId
                && entry.BatchReference == batch
                && entry.ExpiryDate == expiryDate
                && entry.SerialNumber == serial)
            .Select(entry => (decimal?)entry.Quantity.Value)
            .SumAsync(cancellationToken)
            .ConfigureAwait(false) ?? 0m;
    }

    /// <inheritdoc />
    public void Add(StockLedgerEntry entry) => context.StockLedgerEntries.Add(entry);
}

/// <summary>EF Core implementation of <see cref="IStockBalanceRepository"/>.</summary>
/// <param name="context">The database context.</param>
public sealed class StockBalanceRepository(VumaRetailDbContext context) : IStockBalanceRepository
{
    /// <inheritdoc />
    /// <remarks>
    /// Tracked, not <c>AsNoTracking</c>: every caller of this is <c>StockLedgerPoster</c> about to
    /// mutate the balance it gets back, and a detached entity would silently drop the write.
    /// </remarks>
    public Task<StockBalance?> FindAsync(
        Guid locationId,
        Guid? itemId,
        Guid? itemVariantId,
        CancellationToken cancellationToken = default)
        => context.StockBalances.FirstOrDefaultAsync(
            balance => balance.LocationId == locationId
                && balance.ItemId == itemId
                && balance.ItemVariantId == itemVariantId,
            cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<StockBalance>> ListForLocationAsync(
        Guid locationId,
        CancellationToken cancellationToken = default)
        => await context.StockBalances
            .AsNoTracking()
            .Where(balance => balance.LocationId == locationId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(StockBalance balance) => context.StockBalances.Add(balance);
}

/// <summary>EF Core implementation of <see cref="IStockTransferRepository"/>.</summary>
/// <param name="context">The database context.</param>
public sealed class StockTransferRepository(VumaRetailDbContext context) : IStockTransferRepository
{
    /// <inheritdoc />
    public Task<StockTransfer?> FindAsync(Guid transferId, CancellationToken cancellationToken = default)
        => context.StockTransfers.FirstOrDefaultAsync(transfer => transfer.Id == transferId, cancellationToken);

    /// <inheritdoc />
    public void Add(StockTransfer transfer) => context.StockTransfers.Add(transfer);
}

/// <summary>EF Core implementation of <see cref="IStocktakeRepository"/>.</summary>
/// <param name="context">The database context.</param>
public sealed class StocktakeRepository(VumaRetailDbContext context) : IStocktakeRepository
{
    /// <inheritdoc />
    public Task<StocktakeSession?> FindSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
        => context.StocktakeSessions.FirstOrDefaultAsync(session => session.Id == sessionId, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// Tracked. <c>FinalizeStocktakeCommandHandler</c> reads these to post each variance, and
    /// <c>RecordStocktakeCountCommandHandler</c> re-counts one of them in place.
    /// </remarks>
    public async Task<IReadOnlyList<StocktakeLine>> ListLinesAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
        => await context.StocktakeLines
            .Where(line => line.StocktakeSessionId == sessionId)
            .OrderBy(line => line.CreatedAt)
            .ThenBy(line => line.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public Task<StocktakeLine?> FindLineAsync(
        Guid sessionId,
        Guid? itemId,
        Guid? itemVariantId,
        CancellationToken cancellationToken = default)
        => context.StocktakeLines.FirstOrDefaultAsync(
            line => line.StocktakeSessionId == sessionId
                && line.ItemId == itemId
                && line.ItemVariantId == itemVariantId,
            cancellationToken);

    /// <inheritdoc />
    public void AddSession(StocktakeSession session) => context.StocktakeSessions.Add(session);

    /// <inheritdoc />
    public void AddLine(StocktakeLine line) => context.StocktakeLines.Add(line);
}

/// <summary>EF Core implementation of <see cref="IStockReservationRepository"/> (Stage 08c).</summary>
/// <param name="context">The database context.</param>
/// <remarks>
/// No update path and no remove: the table is append-only (ADR-103) and <c>AuditInterceptor</c>
/// refuses any modification to an <c>IImmutableRecord</c>. Terminal rows are added, never written
/// over the hold they close — same shape as <see cref="StockLedgerRepository"/>.
/// </remarks>
public sealed class StockReservationRepository(VumaRetailDbContext context) : IStockReservationRepository
{
    /// <inheritdoc />
    public Task<StockReservation?> FindOpenAsync(Guid reservationId, CancellationToken cancellationToken = default)
        // Chain-aware: a row's born-state stays Held forever, so the seq-0 row of a closed chain
        // still matches a bare state filter. Openness is the absence of a terminal row — without
        // the anti-join, closing an already-closed chain appends a bogus second terminal row
        // instead of refusing (found by Stage 09b compensation, which filters the same way).
        => context.StockReservations
            .Where(reservation => reservation.ReservationId == reservationId
                && reservation.State == ReservationState.Held
                && !context.StockReservations.Any(terminal =>
                    terminal.ReservationId == reservationId
                    && terminal.State != ReservationState.Held))
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<StockReservation>> ListChainAsync(
        Guid reservationId,
        CancellationToken cancellationToken = default)
        => await context.StockReservations
            .Where(reservation => reservation.ReservationId == reservationId)
            .OrderBy(reservation => reservation.SequenceNumber)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<StockReservation>> ListOpenAsync(
        Guid locationId,
        Guid? itemId,
        Guid? itemVariantId,
        CancellationToken cancellationToken = default)
        => await context.StockReservations
            .Where(reservation => reservation.LocationId == locationId
                && reservation.ItemId == itemId
                && reservation.ItemVariantId == itemVariantId
                && reservation.State == ReservationState.Held)
            .OrderBy(reservation => reservation.CreatedAt)
            .ThenBy(reservation => reservation.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public Task<StockReservation?> FindLegHoldAsync(
        Guid intentId,
        Guid legId,
        Guid locationId,
        Guid? itemId,
        Guid? itemVariantId,
        string? batchReference = null,
        DateOnly? expiryDate = null,
        string? serialNumber = null,
        CancellationToken cancellationToken = default)
        => context.StockReservations
            .Where(reservation => reservation.IntentId == intentId
                && reservation.LegId == legId
                && reservation.LocationId == locationId
                && reservation.ItemId == itemId
                && reservation.ItemVariantId == itemVariantId
                && reservation.BatchReference == batchReference
                && reservation.ExpiryDate == expiryDate
                && reservation.SerialNumber == serialNumber
                && reservation.State == ReservationState.Held)
            .OrderBy(reservation => reservation.SequenceNumber)
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<StockReservation>> ListOpenByGroupRefAsync(
        string groupDocumentRef,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupDocumentRef);

        return await context.StockReservations
            .Where(reservation => reservation.GroupDocumentRef == groupDocumentRef
                && reservation.State == ReservationState.Held)
            .OrderBy(reservation => reservation.CreatedAt)
            .ThenBy(reservation => reservation.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StockReservation>> ListExpiredAsync(
        DateTimeOffset now,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "The batch limit must be positive.");
        }

        return await context.StockReservations
            .Where(reservation => reservation.State == ReservationState.Held
                && reservation.ExpiresAt != null
                && reservation.ExpiresAt <= now)
            .OrderBy(reservation => reservation.ExpiresAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Add(StockReservation reservation) => context.StockReservations.Add(reservation);
}

/// <summary>EF Core implementation of <see cref="IAvailableBalanceRepository"/> (Stage 08c).</summary>
/// <param name="context">The database context.</param>
public sealed class AvailableBalanceRepository(VumaRetailDbContext context) : IAvailableBalanceRepository
{
    /// <inheritdoc />
    /// <remarks>
    /// Tracked, not <c>AsNoTracking</c>: every caller of this is <c>ReservationService</c> about
    /// to mutate the position it gets back inside a locked transaction, and a detached entity
    /// would silently drop the write — the same reason <c>StockBalanceRepository</c> tracks.
    /// </remarks>
    public Task<AvailableBalance?> FindAsync(
        Guid locationId,
        Guid? itemId,
        Guid? itemVariantId,
        CancellationToken cancellationToken = default)
        => context.AvailableBalances.FirstOrDefaultAsync(
            balance => balance.LocationId == locationId
                && balance.ItemId == itemId
                && balance.ItemVariantId == itemVariantId,
            cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<AvailableBalance>> ListForLocationAsync(
        Guid locationId,
        CancellationToken cancellationToken = default)
        => await context.AvailableBalances
            .AsNoTracking()
            .Where(balance => balance.LocationId == locationId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(AvailableBalance balance) => context.AvailableBalances.Add(balance);
}
