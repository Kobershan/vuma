using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Abstractions.CustomerAccounts;
using VumaRetail.Domain.CustomerAccounts;

namespace VumaRetail.Infrastructure.Persistence.Repositories;

/// <summary>EF Core implementation of <see cref="IStokvelGroupRepository"/>.</summary>
/// <remarks>Groups, members and baskets travel together: every handler loads the group before it
/// touches a child, so one repository owns all three rather than three repositories racing.</remarks>
public sealed class StokvelGroupRepository(VumaRetailDbContext context) : IStokvelGroupRepository
{
    /// <inheritdoc />
    public Task<StokvelGroup?> FindAsync(Guid id, CancellationToken cancellationToken = default)
        => context.StokvelGroups.FirstOrDefaultAsync(g => g.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<StokvelGroup?> FindByNumberAsync(string number, CancellationToken cancellationToken = default)
        => context.StokvelGroups.FirstOrDefaultAsync(g => g.GroupNumber == number, cancellationToken);

    /// <inheritdoc />
    public Task<StokvelMember?> FindMemberAsync(Guid memberId, CancellationToken cancellationToken = default)
        => context.StokvelMembers.FirstOrDefaultAsync(m => m.Id == memberId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<StokvelMember>> ListMembersAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        return await context.StokvelMembers
            .Where(m => m.GroupId == groupId)
            .OrderBy(m => m.JoinedAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<HamperBasket?> FindBasketAsync(Guid basketId, CancellationToken cancellationToken = default)
        => context.HamperBaskets
            .Include(b => b.Lines)
            .FirstOrDefaultAsync(b => b.Id == basketId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<HamperBasket>> ListBasketsAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        return await context.HamperBaskets
            .Include(b => b.Lines)
            .Where(b => b.GroupId == groupId)
            .OrderBy(b => b.Name)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public void AddGroup(StokvelGroup group) => context.StokvelGroups.Add(group);

    /// <inheritdoc />
    public void AddMember(StokvelMember member) => context.StokvelMembers.Add(member);

    /// <inheritdoc />
    public void AddBasket(HamperBasket basket) => context.HamperBaskets.Add(basket);
}

/// <summary>EF Core implementation of <see cref="IStokvelContributionRepository"/>.</summary>
/// <remarks>Append-only by construction: <c>Add</c> and list methods exist, and no <c>Update</c>
/// does. A correction is a new row, never an edit.</remarks>
public sealed class StokvelContributionRepository(VumaRetailDbContext context) : IStokvelContributionRepository
{
    /// <inheritdoc />
    public Task<StokvelContribution?> FindAsync(Guid id, CancellationToken cancellationToken = default)
        => context.StokvelContributions.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<StokvelContribution?> FindByReceiptAsync(Guid memberId, string receiptReference, CancellationToken cancellationToken = default)
        => context.StokvelContributions.FirstOrDefaultAsync(
            c => c.MemberId == memberId && c.ReceiptReference == receiptReference, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<StokvelContribution>> ListForMemberAsync(Guid memberId, CancellationToken cancellationToken = default)
    {
        return await context.StokvelContributions
            .Where(c => c.MemberId == memberId)
            .OrderBy(c => c.PaidAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StokvelContribution>> ListForGroupAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        return await context.StokvelContributions
            .Where(c => c.GroupId == groupId)
            .OrderBy(c => c.PaidAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StokvelBenefitAllocation>> ListBenefitsForMemberAsync(Guid memberId, CancellationToken cancellationToken = default)
    {
        return await context.StokvelBenefitAllocations
            .Where(b => b.MemberId == memberId)
            .OrderBy(b => b.AllocatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StokvelBenefitAllocation>> ListBenefitsForGroupAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        return await context.StokvelBenefitAllocations
            .Where(b => b.GroupId == groupId)
            .OrderBy(b => b.AllocatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public void AddContribution(StokvelContribution contribution) => context.StokvelContributions.Add(contribution);

    /// <inheritdoc />
    public void AddBenefit(StokvelBenefitAllocation benefit) => context.StokvelBenefitAllocations.Add(benefit);
}

/// <summary>EF Core implementation of <see cref="IStokvelPayoutRepository"/>.</summary>
public sealed class StokvelPayoutRepository(VumaRetailDbContext context) : IStokvelPayoutRepository
{
    /// <inheritdoc />
    public Task<StokvelPayout?> FindAsync(Guid id, CancellationToken cancellationToken = default)
        => context.StokvelPayouts.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<StokvelPayout>> ListForMemberAsync(Guid memberId, CancellationToken cancellationToken = default)
    {
        return await context.StokvelPayouts
            .Where(p => p.MemberId == memberId)
            .OrderBy(p => p.RequestedAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StokvelPayout>> ListForGroupAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        return await context.StokvelPayouts
            .Where(p => p.GroupId == groupId)
            .OrderBy(p => p.RequestedAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public void Add(StokvelPayout payout) => context.StokvelPayouts.Add(payout);
}
