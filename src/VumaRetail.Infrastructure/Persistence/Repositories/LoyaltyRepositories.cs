using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Loyalty;
using VumaRetail.Domain.Loyalty;
using VumaRetail.Infrastructure.Persistence;

namespace VumaRetail.Infrastructure.Persistence.Repositories;

/// <summary>EF Core implementation of the loyalty member store (Stage 20).</summary>
public sealed class LoyaltyMemberRepository(VumaRetailDbContext context) : ILoyaltyMemberRepository
{
    /// <inheritdoc />
    public Task<LoyaltyMember?> FindByCustomerAsync(Guid customerId, CancellationToken cancellationToken = default)
        => context.LoyaltyMembers.FirstOrDefaultAsync(
            member => member.CustomerId == customerId, cancellationToken);

    /// <inheritdoc />
    public Task<LoyaltyMember?> FindByOrbitIdAsync(string orbitMemberId, CancellationToken cancellationToken = default)
        => context.LoyaltyMembers.FirstOrDefaultAsync(
            member => member.OrbitMemberId == orbitMemberId, cancellationToken);

    /// <inheritdoc />
    public void Add(LoyaltyMember member) => context.LoyaltyMembers.Add(member);
}

/// <summary>EF Core implementation of the loyalty transaction log (Stage 20).</summary>
public sealed class LoyaltyTransactionRepository(VumaRetailDbContext context) : ILoyaltyTransactionRepository
{
    /// <inheritdoc />
    public Task<LoyaltyTransaction?> FindAsync(Guid transactionId, CancellationToken cancellationToken = default)
        => context.LoyaltyTransactions.FirstOrDefaultAsync(
            transaction => transaction.Id == transactionId, cancellationToken);

    /// <inheritdoc />
    public Task<LoyaltyTransaction?> FindByKeyAsync(
        Guid companyId, Guid idempotencyKey, CancellationToken cancellationToken = default)
        => context.LoyaltyTransactions.FirstOrDefaultAsync(
            transaction => transaction.CompanyId == companyId
                && transaction.IdempotencyKey == idempotencyKey,
            cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<LoyaltyTransaction>> ListForMemberAsync(
        Guid customerId, int limit, CancellationToken cancellationToken = default)
        => await context.LoyaltyTransactions.AsNoTracking()
            .Where(transaction => transaction.CustomerId == customerId)
            .OrderByDescending(transaction => transaction.OccurredAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<LoyaltyTransaction>> ListQueuedAsync(
        DateTimeOffset notBefore, int limit, CancellationToken cancellationToken = default)
        => await context.LoyaltyTransactions
            .Where(transaction => transaction.Status == TransactionStatus.QueuedForRetry
                && transaction.OccurredAt <= notBefore)
            .OrderBy(transaction => transaction.OccurredAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<LoyaltyTransaction>> ListConfirmedAsync(
        Guid customerId, CancellationToken cancellationToken = default)
        => await context.LoyaltyTransactions.AsNoTracking()
            .Where(transaction => transaction.CustomerId == customerId
                && transaction.Status == TransactionStatus.Confirmed)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(LoyaltyTransaction transaction) => context.LoyaltyTransactions.Add(transaction);
}

/// <summary>EF Core implementation of the cached tier store (Stage 20).</summary>
public sealed class LoyaltyTierRepository(VumaRetailDbContext context) : ILoyaltyTierRepository
{
    /// <inheritdoc />
    public Task<LoyaltyTier?> FindAsync(Guid companyId, string tierId, CancellationToken cancellationToken = default)
        => context.LoyaltyTiers.FirstOrDefaultAsync(
            tier => tier.CompanyId == companyId && tier.TierId == tierId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<LoyaltyTier>> ListAsync(Guid companyId, CancellationToken cancellationToken = default)
        => await context.LoyaltyTiers.AsNoTracking()
            .Where(tier => tier.CompanyId == companyId)
            .OrderBy(tier => tier.ThresholdPoints)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(LoyaltyTier tier) => context.LoyaltyTiers.Add(tier);
}

/// <summary>EF Core implementation of the cached reward store (Stage 20).</summary>
public sealed class LoyaltyRewardRepository(VumaRetailDbContext context) : ILoyaltyRewardRepository
{
    /// <inheritdoc />
    public Task<LoyaltyReward?> FindAsync(Guid companyId, string rewardId, CancellationToken cancellationToken = default)
        => context.LoyaltyRewards.FirstOrDefaultAsync(
            reward => reward.CompanyId == companyId && reward.RewardId == rewardId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<LoyaltyReward>> ListAsync(
        Guid companyId, string? tierId, CancellationToken cancellationToken = default)
        => await context.LoyaltyRewards.AsNoTracking()
            .Where(reward => reward.CompanyId == companyId
                && (tierId == null || reward.TierId == tierId))
            .OrderBy(reward => reward.CostInPoints)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(LoyaltyReward reward) => context.LoyaltyRewards.Add(reward);
}

/// <summary>EF Core implementation of the loyalty settings store (Stage 20).</summary>
public sealed class LoyaltySettingsRepository(VumaRetailDbContext context) : ILoyaltySettingsRepository
{
    /// <inheritdoc />
    public Task<LoyaltySettings?> FindAsync(Guid companyId, CancellationToken cancellationToken = default)
        => context.LoyaltySettings.FirstOrDefaultAsync(
            settings => settings.CompanyId == companyId, cancellationToken);

    /// <inheritdoc />
    public void Add(LoyaltySettings settings) => context.LoyaltySettings.Add(settings);
}
