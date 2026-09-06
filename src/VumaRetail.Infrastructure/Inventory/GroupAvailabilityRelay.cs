using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VumaRetail.Application.Inventory;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry;
using VumaRetail.Domain.Sync;
using VumaRetail.Infrastructure.Persistence;

namespace VumaRetail.Infrastructure.Inventory;

/// <summary>How the group availability projection decides a contributor has gone quiet (ADR-119).</summary>
public sealed class GroupAvailabilityOptions
{
    /// <summary>Section name for configuration binding.</summary>
    public const string SectionName = "Vuma:Availability";

    /// <summary>After this long without a publish, a contributor is shown as stale. Default 15 minutes.</summary>
    public TimeSpan StaleAfter { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>How many outbox rows the relay applies per company per pass. Default 200.</summary>
    public int RelayBatchSize { get; set; } = 200;
}

/// <summary>Publishes availability snapshots to the registry projection (ADR-119).</summary>
/// <param name="registry">The registry database.</param>
/// <param name="clock">The only source of time.</param>
/// <remarks>
/// Upsert by natural key, idempotent by construction: publishing the same figure twice changes
/// nothing observable except <c>AsAt</c>, so a retry after a crash between the company commit
/// and this publish is always safe. Each publish is its own registry transaction — there is no
/// transaction spanning the company and the registry databases (ADR-116).
/// </remarks>
public sealed class RegistryAvailabilityPublisher(VumaRegistryDbContext registry)
    : IGroupAvailabilityPublisher
{
    /// <inheritdoc />
    public async Task PublishAsync(AvailabilitySnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        string companyCode = await registry.Companies
            .AsNoTracking()
            .Where(company => company.TenantId == snapshot.TenantId && company.Id == snapshot.CompanyId)
            .Select(company => company.Code)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("The publishing company is not registered.");

        GroupAvailabilityRow? existing = await registry.GroupAvailabilityRows
            .FirstOrDefaultAsync(row => row.TenantId == snapshot.TenantId
                && row.CompanyId == snapshot.CompanyId
                && row.LocationId == snapshot.LocationId
                && row.ItemId == snapshot.ItemId
                && row.ItemVariantId == snapshot.ItemVariantId,
                cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            registry.GroupAvailabilityRows.Add(GroupAvailabilityRow.Publish(
                snapshot.TenantId,
                snapshot.CompanyId,
                companyCode,
                snapshot.LocationId,
                snapshot.ItemId,
                snapshot.ItemVariantId,
                snapshot.OnHand,
                snapshot.Reserved,
                snapshot.InStaging,
                snapshot.UnitOfMeasure,
                snapshot.AsAt));
        }
        else
        {
            existing.Refresh(snapshot.OnHand, snapshot.Reserved, snapshot.InStaging, snapshot.AsAt);
        }

        await registry.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Heals the registry availability projection from company outboxes and rebuilds it from scratch.
/// </summary>
/// <remarks>
/// <para>
/// Steady state reaches the projection through <see cref="IGroupAvailabilityPublisher"/> on every
/// reservation write. This relay is the catch-up: it tails one company's
/// <c>sync.outbox_messages</c> for <c>StockReservation</c> rows past the per-company cursor and
/// re-publishes every touched stock-keeping unit's current figures. At-least-once with an
/// idempotent upsert — a crash between applying rows and advancing the cursor replays harmlessly.
/// </para>
/// <para>
/// The company database is only ever read; the registry is the only database written, in one
/// transaction per pass. There is no distributed transaction anywhere in this path (ADR-116).
/// </para>
/// </remarks>
public sealed class GroupAvailabilityRelay
{
    private readonly IOptions<GroupAvailabilityOptions> _options;

    /// <summary>Builds the relay.</summary>
    public GroupAvailabilityRelay(IOptions<GroupAvailabilityOptions> options)
    {
        _options = options;
    }

    /// <summary>Applies one company's pending reservation outbox rows to the projection.</summary>
    /// <param name="companyDb">The company's database. Read-only in this method.</param>
    /// <param name="registry">The registry database. The only database written.</param>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="companyId">The company being relayed.</param>
    /// <param name="now">When this pass ran.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>How many outbox rows were applied.</returns>
    public async Task<int> RelayCompanyAsync(
        VumaRetailDbContext companyDb,
        VumaRegistryDbContext registry,
        Guid tenantId,
        Guid companyId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(companyDb);
        ArgumentNullException.ThrowIfNull(registry);

        GroupAvailabilityCursor? cursor = await registry.GroupAvailabilityCursors
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.CompanyId == companyId, cancellationToken)
            .ConfigureAwait(false);

        cursor ??= GroupAvailabilityCursor.Start(tenantId, companyId, now);
        if (registry.Entry(cursor).State == EntityState.Detached)
        {
            registry.GroupAvailabilityCursors.Add(cursor);
        }

        List<OutboxMessage> pending = await companyDb.OutboxMessages
            .AsNoTracking()
            .Where(message => message.TenantId == tenantId
                && message.EntityType == nameof(StockReservation))
            .OrderBy(message => message.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // The cursor compares UUID v7 row ids, which are time-ordered: only rows after the cursor
        // are new. (String comparison is ordinal on the canonical form — adequate because every
        // id here was minted by UuidV7 on one node.)
        List<OutboxMessage> fresh = pending
            .Where(message => string.Compare(message.Id.ToString("D"), cursor.LastOutboxRowId.ToString("D"), StringComparison.Ordinal) > 0)
            .Take(_options.Value.RelayBatchSize)
            .ToList();

        if (fresh.Count == 0)
        {
            return 0;
        }

        string companyCode = await registry.Companies
            .AsNoTracking()
            .Where(company => company.TenantId == tenantId && company.Id == companyId)
            .Select(company => company.Code)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("The relayed company is not registered.");

        HashSet<Guid> touched = fresh.Select(message => message.EntityId).ToHashSet();

        List<StockReservation> reservations = await companyDb.StockReservations
            .AsNoTracking()
            .Where(reservation => touched.Contains(reservation.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var affected = reservations
            .Select(reservation => (reservation.LocationId, reservation.ItemId, reservation.ItemVariantId))
            .Distinct()
            .ToList();

        foreach ((Guid locationId, Guid? itemId, Guid? itemVariantId) in affected)
        {
            await PublishCurrentAsync(companyDb, registry, tenantId, companyId, companyCode, locationId, itemId, itemVariantId, now, cancellationToken)
                .ConfigureAwait(false);
        }

        cursor.Advance(fresh[^1].Id, now);
        await registry.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return fresh.Count;
    }

    /// <summary>Rebuilds the projection for the given companies from their current figures.</summary>
    /// <remarks>
    /// The rebuild-equals-incremental test calls this and compares row for row against the
    /// projection the writes maintained. Production callers use the admin rebuild endpoint after
    /// an outage or a restore.
    /// </remarks>
    /// <param name="companies">One open company database per company, keyed by company id.</param>
    /// <param name="registry">The registry database.</param>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="now">When the rebuild ran.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    public async Task RebuildAsync(
        IReadOnlyDictionary<Guid, VumaRetailDbContext> companies,
        VumaRegistryDbContext registry,
        Guid tenantId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(companies);
        ArgumentNullException.ThrowIfNull(registry);

        Dictionary<Guid, string> codes = await registry.Companies
            .AsNoTracking()
            .Where(company => company.TenantId == tenantId && companies.Keys.Contains(company.Id))
            .ToDictionaryAsync(company => company.Id, company => company.Code, cancellationToken)
            .ConfigureAwait(false);

        foreach ((Guid companyId, VumaRetailDbContext companyDb) in companies)
        {
            if (!codes.TryGetValue(companyId, out string? companyCode))
            {
                throw new InvalidOperationException("A rebuilt company is not registered.");
            }

            List<AvailableBalance> positions = await companyDb.AvailableBalances
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            List<StockBalance> balances = await companyDb.StockBalances
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (AvailableBalance position in positions)
            {
                StockBalance? onHand = balances.FirstOrDefault(balance =>
                    balance.LocationId == position.LocationId
                    && balance.ItemId == position.ItemId
                    && balance.ItemVariantId == position.ItemVariantId);

                string unit = onHand?.QuantityOnHand.UnitOfMeasure ?? position.Reserved.UnitOfMeasure;
                decimal onHandValue = onHand?.QuantityOnHand.Value ?? 0m;

                GroupAvailabilityRow? existing = await registry.GroupAvailabilityRows
                    .FirstOrDefaultAsync(row => row.TenantId == tenantId
                        && row.CompanyId == companyId
                        && row.LocationId == position.LocationId
                        && row.ItemId == position.ItemId
                        && row.ItemVariantId == position.ItemVariantId,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (existing is null)
                {
                    registry.GroupAvailabilityRows.Add(GroupAvailabilityRow.Publish(
                        tenantId, companyId, companyCode, position.LocationId,
                        position.ItemId, position.ItemVariantId,
                        onHandValue, position.Reserved.Value, position.InStaging.Value, unit, now));
                }
                else
                {
                    existing.Refresh(onHandValue, position.Reserved.Value, position.InStaging.Value, now);
                }
            }

            GroupAvailabilityCursor? cursor = await registry.GroupAvailabilityCursors
                .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.CompanyId == companyId, cancellationToken)
                .ConfigureAwait(false);

            cursor ??= GroupAvailabilityCursor.Start(tenantId, companyId, now);
            if (registry.Entry(cursor).State == EntityState.Detached)
            {
                registry.GroupAvailabilityCursors.Add(cursor);
            }

            Guid latest = await companyDb.OutboxMessages
                .AsNoTracking()
                .Where(message => message.TenantId == tenantId && message.EntityType == nameof(StockReservation))
                .OrderByDescending(message => message.Id)
                .Select(message => message.Id)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (latest != Guid.Empty)
            {
                cursor.Advance(latest, now);
            }
        }

        await registry.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task PublishCurrentAsync(
        VumaRetailDbContext companyDb,
        VumaRegistryDbContext registry,
        Guid tenantId,
        Guid companyId,
        string companyCode,
        Guid locationId,
        Guid? itemId,
        Guid? itemVariantId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        AvailableBalance? position = await companyDb.AvailableBalances
            .AsNoTracking()
            .FirstOrDefaultAsync(balance => balance.LocationId == locationId
                && balance.ItemId == itemId
                && balance.ItemVariantId == itemVariantId,
                cancellationToken)
            .ConfigureAwait(false);

        StockBalance? balance = await companyDb.StockBalances
            .AsNoTracking()
            .FirstOrDefaultAsync(stock => stock.LocationId == locationId
                && stock.ItemId == itemId
                && stock.ItemVariantId == itemVariantId,
                cancellationToken)
            .ConfigureAwait(false);

        string unit = balance?.QuantityOnHand.UnitOfMeasure
            ?? position?.Reserved.UnitOfMeasure
            ?? "EA";
        decimal onHand = balance?.QuantityOnHand.Value ?? 0m;

        var snapshot = new AvailabilitySnapshot(
            tenantId, companyId, locationId, itemId, itemVariantId,
            onHand, position?.Reserved.Value ?? 0m, position?.InStaging.Value ?? 0m, unit, now);

        GroupAvailabilityRow? existing = await registry.GroupAvailabilityRows
            .FirstOrDefaultAsync(row => row.TenantId == tenantId
                && row.CompanyId == companyId
                && row.LocationId == locationId
                && row.ItemId == itemId
                && row.ItemVariantId == itemVariantId,
                cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            registry.GroupAvailabilityRows.Add(GroupAvailabilityRow.Publish(
                snapshot.TenantId, snapshot.CompanyId, companyCode, snapshot.LocationId,
                snapshot.ItemId, snapshot.ItemVariantId,
                snapshot.OnHand, snapshot.Reserved, snapshot.InStaging,
                snapshot.UnitOfMeasure, snapshot.AsAt));
        }
        else
        {
            existing.Refresh(snapshot.OnHand, snapshot.Reserved, snapshot.InStaging, snapshot.AsAt);
        }
    }
}

/// <summary>Reads the registry availability projection — one read, never N company queries (ADR-119).</summary>
public sealed class RegistryAvailabilityReader(
    VumaRegistryDbContext registry,
    Application.Abstractions.IClock clock) : IRegistryAvailabilityReader
{
    /// <inheritdoc />
    public async Task<GroupAvailabilityView> ReadAsync(
        Guid? itemId,
        Guid? itemVariantId,
        TimeSpan staleAfter,
        CancellationToken cancellationToken = default)
    {
        bool hasItem = itemId is not null && itemId != Guid.Empty;
        bool hasVariant = itemVariantId is not null && itemVariantId != Guid.Empty;
        if (hasItem == hasVariant)
        {
            throw InventoryRuleException.ExactlyOneItemOrVariantRequired();
        }

        DateTimeOffset now = clock.UtcNow;

        List<GroupAvailabilityRow> rows = await registry.GroupAvailabilityRows
            .AsNoTracking()
            .Where(row => row.ItemId == itemId && row.ItemVariantId == itemVariantId)
            .OrderBy(row => row.CompanyCode)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<GroupAvailabilityContribution> contributions = rows.Select(row =>
        {
            var promise = new AvailableToPromise(
                new Quantity(row.OnHand, row.UnitOfMeasure),
                new Quantity(row.Reserved, row.UnitOfMeasure),
                new Quantity(row.InStaging, row.UnitOfMeasure),
                Quantity.Zero(row.UnitOfMeasure),
                row.AsAt);
            return new GroupAvailabilityContribution(
                row.CompanyId, row.CompanyCode, promise, row.AsAt, now - row.AsAt > staleAfter);
        }).ToList();

        return new GroupAvailabilityView(itemId, itemVariantId, contributions, now);
    }
}
