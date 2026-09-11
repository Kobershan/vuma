using System.Data;
using VumaRetail.Domain.Registry;
using VumaRetail.Domain.Primitives;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace VumaRetail.Infrastructure.Registry;

/// <summary>Manages credit groups with serializable hold tokens.</summary>
public sealed class GroupCreditService : IGroupCreditService
{
    private readonly VumaRegistryDbContext _registry;
    private readonly IClock _clock;
    private readonly ICompanyLinkService _companyLinks;

    public GroupCreditService(VumaRegistryDbContext registry, IClock clock, ICompanyLinkService companyLinks)
    {
        _registry = registry;
        _clock = clock;
        _companyLinks = companyLinks;
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
        if (tenantId == Guid.Empty) throw new ArgumentException("A tenant is required.", nameof(tenantId));
        if (creditGroupId == Guid.Empty) throw new ArgumentException("A credit group is required.", nameof(creditGroupId));
        if (companyId == Guid.Empty) throw new ArgumentException("A company is required.", nameof(companyId));
        if (amount <= 0m) throw new ArgumentOutOfRangeException(nameof(amount), "A hold amount must be positive.");
        if (string.IsNullOrWhiteSpace(currency)) throw new ArgumentException("A currency is required.", nameof(currency));
        if (string.IsNullOrWhiteSpace(documentReference)) throw new ArgumentException("A document reference is required.", nameof(documentReference));
        if (expiry <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(expiry), "A hold expiry must be positive.");

        // Check the live trading-group link before entering the retrying serializable operation.
        // This is deliberately at the public cross-company entry point so a suspended link cannot
        // be hidden behind a cached/configuration-time membership decision.
        Guid[] linkedCompanies = await _registry.CreditGroupMembers
            .Where(member => member.CreditGroupId == creditGroupId && member.CompanyId != companyId)
            .Select(member => member.CompanyId)
            .ToArrayAsync(cancellationToken);
        foreach (Guid other in linkedCompanies)
            await _companyLinks.RequireLink(companyId, other, CompanyLinkScope.SharedCredit, cancellationToken);

        // PostgreSQL detects a genuine serialisation race with SQLSTATE 40001. Re-read inside a
        // new serialisable transaction so the losing till returns an ordinary credit refusal
        // instead of a transient database error; a stable document reference makes this replay-safe.
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return await TryHoldOnceAsync(tenantId, creditGroupId, companyId, amount, currency.Trim().ToUpperInvariant(), documentReference.Trim(), expiry, cancellationToken);
            }
            catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.SerializationFailure && attempt < 4)
            {
                _registry.ChangeTracker.Clear();
            }
        }
    }

    private async Task<HoldResult> TryHoldOnceAsync(
        Guid tenantId, Guid creditGroupId, Guid companyId, decimal amount, string currency,
        string documentReference, TimeSpan expiry, CancellationToken cancellationToken)
    {
        // Disposing an EF transaction rolls back an uncommitted transaction.  Do not explicitly
        // roll it back from a catch block: PostgreSQL has already completed the transaction when
        // it reports a serialisation failure, and a second rollback masks that retriable error.
        await using var transaction = await _registry.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        CreditGroup group = await _registry.CreditGroups
                .Include(candidate => candidate.Members)
                .SingleOrDefaultAsync(candidate => candidate.Id == creditGroupId && candidate.TenantId == tenantId, cancellationToken)
                ?? throw new InvalidOperationException("Credit group not found.");
        if (!string.Equals(group.Currency, currency, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Hold currency must match the credit group currency.");

        CreditHold? existing = await _registry.CreditHolds
            .SingleOrDefaultAsync(hold => hold.TenantId == tenantId && hold.CreditGroupId == creditGroupId
                && hold.CompanyId == companyId && hold.DocumentReference == documentReference, cancellationToken);
        if (existing is not null)
        {
            CreditPosition replayPosition = await GetPositionAsync(tenantId, creditGroupId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new HoldResult(existing.Id, existing.State is CreditHoldState.Held or CreditHoldState.Confirmed, replayPosition.Available);
        }

        CreditGroupMember? member = group.Members.SingleOrDefault(candidate => candidate.CompanyId == companyId);
        if (member is null) throw new InvalidOperationException("The company is not a member of the credit group.");

        CreditPosition position = await GetPositionAsync(tenantId, creditGroupId, cancellationToken);
        if (position.Available < amount)
        {
            await transaction.RollbackAsync(cancellationToken);
            return HoldResult.Failed(Guid.NewGuid());
        }

        decimal companyConfirmed = await _registry.CreditExposureEntries
            .Where(entry => entry.TenantId == tenantId && entry.CreditGroupId == creditGroupId && entry.CompanyId == companyId)
            .SumAsync(entry => (decimal?)entry.Amount, cancellationToken) ?? 0m;
        decimal companyHeld = await _registry.CreditHolds
            .Where(hold => hold.TenantId == tenantId && hold.CreditGroupId == creditGroupId && hold.CompanyId == companyId
                && hold.State == CreditHoldState.Held && hold.ExpiresAt > _clock.UtcNow)
            .SumAsync(hold => (decimal?)hold.Amount, cancellationToken) ?? 0m;
        if (member.SubLimit is { } subLimit && companyConfirmed + companyHeld + amount > subLimit)
        {
            await transaction.RollbackAsync(cancellationToken);
            return HoldResult.Failed(Guid.NewGuid());
        }

        CreditHold hold = CreditHold.Create(tenantId, creditGroupId, companyId, amount, currency, documentReference, _clock.UtcNow.Add(expiry));
        _registry.CreditHolds.Add(hold);
        await _registry.CommitAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new HoldResult(hold.Id, true, position.Available - amount);
    }

    public async Task ConfirmHoldAsync(Guid holdId, CancellationToken cancellationToken = default)
    {
        var hold = await _registry.CreditHolds.FindAsync(new object[] { holdId }, cancellationToken)
            ?? throw new InvalidOperationException("Hold not found.");

        if (hold.State == CreditHoldState.Confirmed) return;
        if (hold.State != CreditHoldState.Held) throw new InvalidOperationException("Hold is not in Held state.");

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

        if (hold.State is CreditHoldState.Released or CreditHoldState.Expired) return;
        if (hold.State != CreditHoldState.Held) throw new InvalidOperationException("Only an unconfirmed hold can be released.");
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

        if (expired.Count > 0) await _registry.CommitAsync(cancellationToken);
        return expired.Count;
    }

    public async Task<IReadOnlyList<OutstandingCreditHold>> GetOutstandingHoldsAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _clock.UtcNow;
        return await _registry.CreditHolds.AsNoTracking()
            .Where(hold => hold.TenantId == tenantId && hold.State == CreditHoldState.Held && hold.ExpiresAt > now)
            .OrderBy(hold => hold.ExpiresAt)
            .Select(hold => new OutstandingCreditHold(
                hold.Id, hold.CreditGroupId, hold.CompanyId, hold.Amount, hold.Currency,
                hold.DocumentReference, hold.ExpiresAt, hold.ExpiresAt - now))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Every group the company belongs to (Stage 14b: the approval saga holds here).</summary>
    public async Task<IReadOnlyList<CreditGroupSummary>> ListGroupsForCompanyAsync(Guid tenantId, Guid companyId, CancellationToken cancellationToken = default)
    {
        List<CreditGroup> groups = await _registry.CreditGroups
            .AsNoTracking()
            .Include(group => group.Members)
            .Where(group => group.TenantId == tenantId
                && group.Members.Any(member => member.CompanyId == companyId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. groups.Select(group => new CreditGroupSummary(
            group.Id, group.Name, group.Direction, group.Limit, group.Currency,
            group.Members.Count))];
    }
}
