using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Loyalty;

namespace VumaRetail.Application.Loyalty.Queries;

/// <summary>One loyalty member, as read back out.</summary>
public sealed record LoyaltyMemberEntry(
    Guid Id,
    Guid CustomerId,
    string OrbitMemberId,
    DateTimeOffset EnrolledAt,
    string? TierId,
    decimal BalanceCache,
    decimal DisplayBalance,
    DateTimeOffset BalanceCacheAsAt,
    bool IsStale);

/// <summary>Reads one member by customer.</summary>
/// <param name="CustomerId">The customer.</param>
public sealed record GetMemberQuery(Guid CustomerId) : IQuery<LoyaltyMemberEntry>;

/// <summary>Reads the member.</summary>
/// <param name="members">Member persistence.</param>
/// <param name="clock">The only source of time.</param>
public sealed class GetMemberQueryHandler(ILoyaltyMemberRepository members, IClock clock)
    : IQueryHandler<GetMemberQuery, LoyaltyMemberEntry>
{
    /// <summary>Minutes before a cache counts as stale.</summary>
    public const double StaleAfterMinutes = 5;

    /// <inheritdoc />
    public async Task<LoyaltyMemberEntry> HandleAsync(GetMemberQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        LoyaltyMember member = await members
            .FindByCustomerAsync(query.CustomerId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new LoyaltyMemberNotFoundException();

        return ToEntry(member, clock.UtcNow);
    }

    internal static LoyaltyMemberEntry ToEntry(LoyaltyMember member, DateTimeOffset now) => new(
        member.Id, member.CustomerId, member.OrbitMemberId, member.EnrolledAt, member.TierId,
        member.BalanceCache, LoyaltyCalculator.ToDisplay(member.BalanceCache),
        member.BalanceCacheAsAt, member.BalanceCacheAsAt.AddMinutes(StaleAfterMinutes) <= now);
}

/// <summary>Reads a member's balance from cache, labelled with its age. Never calls Orbit.</summary>
/// <param name="CustomerId">The member.</param>
public sealed record GetBalanceQuery(Guid CustomerId) : IQuery<BalanceEntry>;

/// <summary>One cached balance, honestly labelled.</summary>
/// <param name="CustomerId">The member.</param>
/// <param name="Balance">Cached balance, scale 4.</param>
/// <param name="DisplayBalance">Display value at 2dp.</param>
/// <param name="TierId">Cached tier, if known.</param>
/// <param name="AsAt">When the cache was confirmed, UTC.</param>
/// <param name="IsStale">True when older than five minutes.</param>
public sealed record BalanceEntry(
    Guid CustomerId, decimal Balance, decimal DisplayBalance, string? TierId, DateTimeOffset AsAt, bool IsStale);

/// <summary>Reads the cached balance.</summary>
/// <param name="members">Member persistence.</param>
/// <param name="clock">The only source of time.</param>
public sealed class GetBalanceQueryHandler(ILoyaltyMemberRepository members, IClock clock)
    : IQueryHandler<GetBalanceQuery, BalanceEntry>
{
    /// <inheritdoc />
    public async Task<BalanceEntry> HandleAsync(GetBalanceQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        LoyaltyMember member = await members
            .FindByCustomerAsync(query.CustomerId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new LoyaltyMemberNotFoundException();

        DateTimeOffset now = clock.UtcNow;
        return new BalanceEntry(
            query.CustomerId, member.BalanceCache,
            LoyaltyCalculator.ToDisplay(member.BalanceCache), member.TierId,
            member.BalanceCacheAsAt,
            member.BalanceCacheAsAt.AddMinutes(GetMemberQueryHandler.StaleAfterMinutes) <= now);
    }
}

/// <summary>One transaction, as read back out.</summary>
public sealed record LoyaltyTransactionEntry(
    Guid Id,
    string Type,
    decimal Amount,
    string Currency,
    string Status,
    string? Reference,
    string? OrbitTransactionId,
    DateTimeOffset OccurredAt);

/// <summary>Lists a member's transactions, newest first.</summary>
/// <param name="CustomerId">The member.</param>
/// <param name="Limit">Maximum rows.</param>
public sealed record ListTransactionsQuery(Guid CustomerId, int? Limit)
    : IQuery<IReadOnlyList<LoyaltyTransactionEntry>>;

/// <summary>Reads the transaction log.</summary>
/// <param name="members">Member persistence (existence check).</param>
/// <param name="transactions">Transaction persistence.</param>
public sealed class ListTransactionsQueryHandler(
    ILoyaltyMemberRepository members,
    ILoyaltyTransactionRepository transactions)
    : IQueryHandler<ListTransactionsQuery, IReadOnlyList<LoyaltyTransactionEntry>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<LoyaltyTransactionEntry>> HandleAsync(
        ListTransactionsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        _ = await members
            .FindByCustomerAsync(query.CustomerId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new LoyaltyMemberNotFoundException();

        IReadOnlyList<LoyaltyTransaction> rows = await transactions
            .ListForMemberAsync(query.CustomerId, Paging.Clamp(query.Limit), cancellationToken)
            .ConfigureAwait(false);

        return [.. rows.Select(row => new LoyaltyTransactionEntry(
            row.Id, row.TransactionType.ToString(), row.Amount, row.Currency, row.Status.ToString(),
            row.Reference, row.OrbitTransactionId, row.OccurredAt))];
    }
}

/// <summary>One tier, as read back out.</summary>
public sealed record TierEntry(
    string TierId, string Name, string DisplayName, decimal ThresholdPoints, decimal Multiplier);

/// <summary>Lists cached tiers for a company, threshold ascending.</summary>
/// <param name="CompanyId">The company.</param>
public sealed record ListTiersQuery(Guid CompanyId) : IQuery<IReadOnlyList<TierEntry>>;

/// <summary>Reads the cached tiers.</summary>
/// <param name="tiers">Tier persistence.</param>
public sealed class ListTiersQueryHandler(ILoyaltyTierRepository tiers)
    : IQueryHandler<ListTiersQuery, IReadOnlyList<TierEntry>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<TierEntry>> HandleAsync(ListTiersQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<LoyaltyTier> rows = await tiers
            .ListAsync(query.CompanyId, cancellationToken)
            .ConfigureAwait(false);

        return [.. rows.Select(row => new TierEntry(
            row.TierId, row.Name, row.DisplayName, row.ThresholdPoints, row.Multiplier))];
    }
}

/// <summary>A member's tier and progress to the next one.</summary>
/// <param name="CustomerId">The member.</param>
/// <param name="TierId">Current tier, if known.</param>
/// <param name="TierName">Current tier name, if known.</param>
/// <param name="NextTierId">Next tier, if any.</param>
/// <param name="PointsToNext">Points to the next tier, if known.</param>
/// <param name="Balance">Cached balance.</param>
public sealed record MemberTierEntry(
    Guid CustomerId, string? TierId, string? TierName, string? NextTierId, decimal? PointsToNext, decimal Balance);

/// <summary>Reads a member's tier and progress.</summary>
/// <param name="CustomerId">The member.</param>
public sealed record GetMemberTierQuery(Guid CustomerId) : IQuery<MemberTierEntry>;

/// <summary>Reads tier progress off the cache.</summary>
/// <param name="members">Member persistence.</param>
/// <param name="tiers">Tier persistence.</param>
public sealed class GetMemberTierQueryHandler(
    ILoyaltyMemberRepository members,
    ILoyaltyTierRepository tiers) : IQueryHandler<GetMemberTierQuery, MemberTierEntry>
{
    /// <inheritdoc />
    public async Task<MemberTierEntry> HandleAsync(GetMemberTierQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        LoyaltyMember member = await members
            .FindByCustomerAsync(query.CustomerId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new LoyaltyMemberNotFoundException();

        IReadOnlyList<LoyaltyTier> all = await tiers
            .ListAsync(member.CompanyId!.Value, cancellationToken)
            .ConfigureAwait(false);

        LoyaltyTier? current = all.FirstOrDefault(tier => tier.TierId == member.TierId);
        LoyaltyTier? next = all
            .Where(tier => tier.ThresholdPoints > member.BalanceCache
                && tier.TierId != member.TierId)
            .OrderBy(tier => tier.ThresholdPoints)
            .FirstOrDefault();

        return new MemberTierEntry(
            query.CustomerId, member.TierId, current?.DisplayName, next?.TierId,
            next is null ? null : next.ThresholdPoints - member.BalanceCache,
            member.BalanceCache);
    }
}

/// <summary>One reward, as read back out.</summary>
public sealed record RewardEntry(
    string RewardId, string Name, string? Description, decimal CostInPoints,
    string? TierId, bool IsAvailable);

/// <summary>Lists cached rewards for a company.</summary>
/// <param name="CompanyId">The company.</param>
/// <param name="TierId">Restricts to a tier, or <c>null</c> for all.</param>
public sealed record ListRewardsQuery(Guid CompanyId, string? TierId)
    : IQuery<IReadOnlyList<RewardEntry>>;

/// <summary>Reads the cached rewards.</summary>
/// <param name="rewards">Reward persistence.</param>
public sealed class ListRewardsQueryHandler(ILoyaltyRewardRepository rewards)
    : IQueryHandler<ListRewardsQuery, IReadOnlyList<RewardEntry>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<RewardEntry>> HandleAsync(ListRewardsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<LoyaltyReward> rows = await rewards
            .ListAsync(query.CompanyId, query.TierId, cancellationToken)
            .ConfigureAwait(false);

        return [.. rows.Select(row => new RewardEntry(
            row.RewardId, row.Name, row.Description, row.CostInPoints, row.TierId, row.IsAvailable))];
    }
}

/// <summary>Lists queued transactions due for retry, oldest first. The retry worker's feed.</summary>
/// <param name="Limit">Maximum rows.</param>
public sealed record ListQueuedTransactionsQuery(int Limit) : IQuery<IReadOnlyList<Guid>>;

/// <summary>Reads the retry queue.</summary>
/// <param name="transactions">Transaction persistence.</param>
/// <param name="clock">The only source of time.</param>
public sealed class ListQueuedTransactionsQueryHandler(
    ILoyaltyTransactionRepository transactions,
    IClock clock) : IQueryHandler<ListQueuedTransactionsQuery, IReadOnlyList<Guid>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> HandleAsync(
        ListQueuedTransactionsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Due means queued at least a minute ago: the inline attempt just failed, and hammering
        // Orbit in a tight loop helps nobody.
        IReadOnlyList<LoyaltyTransaction> rows = await transactions
            .ListQueuedAsync(clock.UtcNow.AddMinutes(-1), Paging.Clamp(query.Limit), cancellationToken)
            .ConfigureAwait(false);

        return [.. rows.Select(row => row.Id)];
    }
}

/// <summary>One member's reconciliation line.</summary>/// <param name="CustomerId">The member.</param>
/// <param name="VumaBalance">Sum of confirmed earns minus burns.</param>
/// <param name="OrbitBalance">Orbit's reported balance.</param>
/// <param name="Difference">Vuma minus Orbit. Zero is agreement.</param>
public sealed record ReconciliationLine(Guid CustomerId, decimal VumaBalance, decimal OrbitBalance, decimal Difference);

/// <summary>Runs the loyalty reconciliation: Vuma's confirmed log against Orbit's ledger.</summary>
/// <param name="CompanyId">The company.</param>
/// <param name="CustomerIds">Members to check. Empty means all with confirmed transactions — bounded by Limit.</param>
/// <param name="Limit">Maximum members when unbounded.</param>
public sealed record GetReconciliationReportQuery(Guid CompanyId, IReadOnlyList<Guid> CustomerIds, int? Limit)
    : IQuery<IReadOnlyList<ReconciliationLine>>;

/// <summary>Compares the logs. Read-only: discrepancies are reported, never auto-corrected.</summary>
/// <param name="members">Member persistence.</param>
/// <param name="transactions">Transaction persistence.</param>
/// <param name="orbit">The loyalty engine boundary.</param>
public sealed class GetReconciliationReportQueryHandler(
    ILoyaltyMemberRepository members,
    ILoyaltyTransactionRepository transactions,
    IOrbitClient orbit) : IQueryHandler<GetReconciliationReportQuery, IReadOnlyList<ReconciliationLine>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ReconciliationLine>> HandleAsync(
        GetReconciliationReportQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var lines = new List<ReconciliationLine>();

        foreach (Guid customerId in query.CustomerIds.Take(Paging.Clamp(query.Limit)))
        {
            LoyaltyMember? member = await members
                .FindByCustomerAsync(customerId, cancellationToken)
                .ConfigureAwait(false);

            if (member is null)
            {
                continue;
            }

            IReadOnlyList<LoyaltyTransaction> confirmed = await transactions
                .ListConfirmedAsync(customerId, cancellationToken)
                .ConfigureAwait(false);

            decimal vuma = confirmed.Sum(row =>
                row.TransactionType == TransactionType.Burn ? -row.Amount : row.Amount);

            OrbitBalanceResult live = await orbit
                .GetBalanceAsync(member.OrbitMemberId, cancellationToken)
                .ConfigureAwait(false);

            lines.Add(new ReconciliationLine(customerId, vuma, live.Balance, vuma - live.Balance));
        }

        return lines;
    }
}
