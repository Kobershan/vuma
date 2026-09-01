using System.Data;
using VumaRetail.Domain.Registry;
using VumaRetail.Domain.Primitives;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace VumaRetail.Infrastructure.Registry;

/// <summary>Manages credit groups with serializable hold tokens.</summary>
public sealed class GroupCreditService : IGroupCreditService
{
    private readonly VumaRegistryDbContext _registry;
    private readonly IClock _clock;

    public GroupCreditService(VumaRegistryDbContext registry, IClock clock)
    {
        _registry = registry;
        _clock = clock;
    }

    public async Task<CreditPosition> GetPositionAsync(Guid tenantId, Guid creditGroupId, CancellationToken cancellationToken = default)
    {
        var group = await _registry.CreditGroups
            .AsNoTracking()
            .Include(g => g.Members)
            .FirstOrDefaultAsync(g => g.Id == creditGroupId && g.TenantId == tenantId, cancellationToken)
            ?? throw new InvalidOperationException("Credit group not found.");

        var confirmed = await _registry.CreditExposureEntries
            .Where(e => e.TenantId == tenantId && e.CreditGroupId == creditGroupId)
            .SumAsync(e => e.Amount, cancellationToken);

        var held = await _registry.CreditHolds
            .Where(h => h.TenantId == tenantId && h.CreditGroupId == creditGroupId
                && h.State == CreditHoldState.Held && h.ExpiresAt > _clock.UtcNow)
            .SumAsync(h => h.Amount, cancellationToken);

        return new CreditPosition(
            creditGroupId,
            group.Limit,
            group.Currency,
            confirmed,
            held,
            group.Limit - confirmed - held);
    }

    public async Task<HoldResult> TryHoldAsync(Guid tenantId, Guid creditGroupId, Guid companyId, decimal amount, string currency, string documentReference, TimeSpan expiry, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _registry.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var position = await GetPositionAsync(tenantId, creditGroupId, cancellationToken);
            if (position.Available < amount)
            {
                await transaction.RollbackAsync(cancellationToken);
                return HoldResult.Failed(Guid.NewGuid());
            }

            var member = await _registry.CreditGroupMembers
                .FirstOrDefaultAsync(m => m.CreditGroupId == creditGroupId && m.CompanyId == companyId, cancellationToken);

            if (member?.SubLimit is not null && member.SubLimit < amount)
            {
                await transaction.RollbackAsync(cancellationToken);
                return HoldResult.Failed(Guid.NewGuid());
            }

            var hold = CreditHold.Create(tenantId, creditGroupId, companyId, amount, currency, documentReference, _clock.UtcNow.Add(expiry));
            _registry.CreditHolds.Add(hold);
            await _registry.CommitAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            var newPosition = await GetPositionAsync(tenantId, creditGroupId, cancellationToken);
            return new HoldResult(hold.Id, true, newPosition.Available);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task ConfirmHoldAsync(Guid holdId, CancellationToken cancellationToken = default)
    {
        var hold = await _registry.CreditHolds.FindAsync(new object[] { holdId }, cancellationToken)
            ?? throw new InvalidOperationException("Hold not found.");

        if (hold.State != CreditHoldState.Held)
            throw new InvalidOperationException("Hold is not in Held state.");

        hold.Confirm(_clock.UtcNow);
        _registry.CreditExposureEntries.Add(new CreditExposureEntry
        {
            Id = UuidV7.NewGuid(),
            TenantId = hold.TenantId,
            CreditGroupId = hold.CreditGroupId,
            CompanyId = hold.CompanyId,
            Amount = hold.Amount,
            Currency = hold.Currency,
            DocumentReference = hold.DocumentReference,
            ConfirmedAt = _clock.UtcNow
        });
        await _registry.CommitAsync(cancellationToken);
    }

    public async Task ReleaseHoldAsync(Guid holdId, CancellationToken cancellationToken = default)
    {
        var hold = await _registry.CreditHolds.FindAsync(new object[] { holdId }, cancellationToken)
            ?? throw new InvalidOperationException("Hold not found.");

        hold.Release(_clock.UtcNow);
        await _registry.CommitAsync(cancellationToken);
    }

    public async Task<int> ExpireHoldsAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var expired = await _registry.CreditHolds
            .Where(h => h.TenantId == tenantId && h.State == CreditHoldState.Held && h.ExpiresAt <= _clock.UtcNow)
            .ToListAsync(cancellationToken);

        foreach (var hold in expired)
        {
            hold.Expire(_clock.UtcNow);
        }

        await _registry.CommitAsync(cancellationToken);
        return expired.Count;
    }
}
