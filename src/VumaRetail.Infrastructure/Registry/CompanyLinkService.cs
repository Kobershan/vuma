#pragma warning disable CS1591
using System.Collections.Concurrent;
using System.Text.Json;
using VumaRetail.Domain.Registry;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace VumaRetail.Infrastructure.Registry;

/// <summary>Enforces company link checks at the point of use.</summary>
/// <remarks>
/// Reads go through a small in-memory snapshot cache keyed by ordered pair; every mutation
/// invalidates its key and writes a <c>company-link.changed</c> registry outbox row in the same
/// transaction, which is the durable cross-node invalidation (a SignalR transport can subscribe
/// to the same event later — no such transport exists in this product yet).
/// </remarks>
public sealed class CompanyLinkService : ICompanyLinkService
{
    private readonly VumaRegistryDbContext _registry;
    private readonly IClock _clock;
    private readonly ITenantContext _tenantContext;
    private readonly IOperatorContext _operatorContext;
    private readonly ConcurrentDictionary<(Guid TenantId, Guid CompanyAId, Guid CompanyBId), LinkSnapshot> _linkCache = new();

    public CompanyLinkService(VumaRegistryDbContext registry, IClock clock, ITenantContext tenantContext, IOperatorContext operatorContext)
    {
        _registry = registry;
        _clock = clock;
        _tenantContext = tenantContext;
        _operatorContext = operatorContext;
    }

    public async Task RequireLink(Guid companyA, Guid companyB, CompanyLinkScope requiredScope, CancellationToken cancellationToken = default)
    {
        if (companyA == Guid.Empty) throw new ArgumentException("Company A is required.", nameof(companyA));
        if (companyB == Guid.Empty) throw new ArgumentException("Company B is required.", nameof(companyB));
        if (companyA == companyB) return;

        var (smaller, larger) = Order(companyA, companyB);
        var key = (_tenantContext.TenantId, smaller, larger);

        if (!_linkCache.TryGetValue(key, out LinkSnapshot cached))
        {
            CompanyLink? link = await _registry.CompanyLinks
                .AsNoTracking()
                .FirstOrDefaultAsync(l => l.TenantId == key.TenantId && l.CompanyAId == smaller && l.CompanyBId == larger, cancellationToken)
                .ConfigureAwait(false);

            if (link is null)
            {
                throw new CompanyLinkRequiredException(companyA, companyB, requiredScope);
            }

            cached = new LinkSnapshot(link.Status, link.Scopes, link.EffectiveTo);
            _linkCache[key] = cached;
        }

        if (cached.Status != CompanyLinkStatus.Active
            || (cached.EffectiveTo is not null && cached.EffectiveTo <= _clock.UtcNow)
            || !cached.Scopes.HasFlag(requiredScope))
        {
            throw new CompanyLinkRequiredException(companyA, companyB, requiredScope);
        }
    }

    public async Task<CompanyLink?> TryGetLinkAsync(Guid companyA, Guid companyB, CancellationToken cancellationToken = default)
    {
        var (smaller, larger) = companyA.CompareTo(companyB) < 0 ? (companyA, companyB) : (companyB, companyA);
        
        return await _registry.CompanyLinks
            .AsNoTracking()
            .FirstOrDefaultAsync(l => 
                l.CompanyAId == smaller && l.CompanyBId == larger && 
                l.Status == CompanyLinkStatus.Active &&
                (l.EffectiveTo == null || l.EffectiveTo > _clock.UtcNow),
                cancellationToken);
    }

    public async Task<IReadOnlyList<CompanyLink>> GetLinksFor(Guid companyId, CancellationToken cancellationToken = default)
    {
        return await _registry.CompanyLinks
            .AsNoTracking()
            .Where(l => (l.CompanyAId == companyId || l.CompanyBId == companyId) && l.Status == CompanyLinkStatus.Active)
            .ToListAsync(cancellationToken);
    }

    public async Task<CompanyLink> ProposeAsync(Guid companyA, Guid companyB, CompanyLinkScope scopes, CancellationToken cancellationToken = default)
    {
        if (companyA == Guid.Empty) throw new ArgumentException("Company A is required.", nameof(companyA));
        if (companyB == Guid.Empty) throw new ArgumentException("Company B is required.", nameof(companyB));
        if (companyA == companyB) throw new ArgumentException("A company cannot link to itself.", nameof(companyB));

        Guid tenantId = _tenantContext.TenantId;
        Guid operatorId = _operatorContext.RequireOperatorId();

        // The operator-match invariant (ADR-121): both companies must exist in this tenant and
        // sit under the acting operator. A proposal naming unknown or foreign companies is
        // refused here, not at the first attempted operation.
        Company companyRowA = await _registry.Companies
            .FirstOrDefaultAsync(c => c.Id == companyA && c.TenantId == tenantId, cancellationToken)
            .ConfigureAwait(false) ?? throw new RegistryNotFoundException("company", companyA);
        Company companyRowB = await _registry.Companies
            .FirstOrDefaultAsync(c => c.Id == companyB && c.TenantId == tenantId, cancellationToken)
            .ConfigureAwait(false) ?? throw new RegistryNotFoundException("company", companyB);

        if (companyRowA.OperatorId == Guid.Empty || companyRowB.OperatorId == Guid.Empty)
        {
            throw RegistryRuleException.CompaniesNeedAnOperator();
        }

        if (companyRowA.OperatorId != companyRowB.OperatorId)
        {
            throw RegistryExceptions.DifferentOperatorsCannotLink(companyRowA.OperatorId, companyRowB.OperatorId);
        }

        if (companyRowA.OperatorId != operatorId)
        {
            throw RegistryExceptions.DifferentOperatorsCannotLink(operatorId, companyRowA.OperatorId);
        }

        var (smaller, larger) = Order(companyA, companyB);

        CompanyLink? existing = await _registry.CompanyLinks
            .FirstOrDefaultAsync(l => l.TenantId == tenantId && l.CompanyAId == smaller && l.CompanyBId == larger, cancellationToken)
            .ConfigureAwait(false);

        // Revocation is final: the row stands as history and the pair cannot be re-proposed.
        if (existing is not null)
        {
            throw RegistryConflictException.LinkAlreadyExists();
        }

        var link = CompanyLink.Create(
            tenantId: tenantId,
            operatorId: operatorId,
            companyAId: smaller,
            companyBId: larger,
            scopes: scopes,
            effectiveFrom: _clock.UtcNow,
            operatorName: _operatorContext.OperatorName);

        _registry.CompanyLinks.Add(link);
        PublishLinkChanged(link);
        await _registry.CommitAsync(cancellationToken).ConfigureAwait(false);
        Invalidate(smaller, larger);
        return link;
    }

    public async Task AcceptAsync(Guid linkId, Guid acceptingCompanyId, string acceptedBy, string licenceFingerprint, CancellationToken cancellationToken = default)
    {
        var link = await _registry.CompanyLinks.FindAsync(new object[] { linkId }, cancellationToken)
            ?? throw new RegistryNotFoundException("company link", linkId);

        link.Accept(acceptingCompanyId, acceptedBy, licenceFingerprint, _clock.UtcNow);

        PublishLinkChanged(link);
        await _registry.CommitAsync(cancellationToken).ConfigureAwait(false);
        Invalidate(link.CompanyAId, link.CompanyBId);
    }

    public async Task SuspendAsync(Guid linkId, string reason, CancellationToken cancellationToken = default)
    {
        var link = await _registry.CompanyLinks.FindAsync(new object[] { linkId }, cancellationToken)
            ?? throw new RegistryNotFoundException("company link", linkId);

        link.Suspend(reason, _clock.UtcNow);

        PublishLinkChanged(link);
        await _registry.CommitAsync(cancellationToken).ConfigureAwait(false);
        Invalidate(link.CompanyAId, link.CompanyBId);
    }

    public async Task ResumeAsync(Guid linkId, CancellationToken cancellationToken = default)
    {
        var link = await _registry.CompanyLinks.FindAsync(new object[] { linkId }, cancellationToken)
            ?? throw new RegistryNotFoundException("company link", linkId);

        link.Resume();

        PublishLinkChanged(link);
        await _registry.CommitAsync(cancellationToken).ConfigureAwait(false);
        Invalidate(link.CompanyAId, link.CompanyBId);
    }

    public async Task RevokeAsync(Guid linkId, string reason, CancellationToken cancellationToken = default)
    {
        var link = await _registry.CompanyLinks.FindAsync(new object[] { linkId }, cancellationToken)
            ?? throw new RegistryNotFoundException("company link", linkId);

        link.Revoke(reason, _clock.UtcNow);

        PublishLinkChanged(link);
        await _registry.CommitAsync(cancellationToken).ConfigureAwait(false);
        Invalidate(link.CompanyAId, link.CompanyBId);
    }

    private static (Guid Smaller, Guid Larger) Order(Guid companyA, Guid companyB)
        => companyA.CompareTo(companyB) < 0 ? (companyA, companyB) : (companyB, companyA);

    private void Invalidate(Guid companyAId, Guid companyBId)
    {
        var (smaller, larger) = Order(companyAId, companyBId);
        _linkCache.TryRemove((_tenantContext.TenantId, smaller, larger), out _);
    }

    private void PublishLinkChanged(CompanyLink link)
    {
        // Same transaction as the mutation: a committed status change always has its event, and
        // a rolled-back one never does. Consumers invalidate their own link caches from it.
        _registry.RegistryOutboxMessages.Add(new RegistryOutboxMessage(
            link.TenantId,
            "company-link.changed",
            JsonSerializer.Serialize(new { linkId = link.Id, status = link.Status.ToString() }),
            _clock.UtcNow,
            $"company-link:{link.Id:N}:{link.Status}"));
    }

    private sealed record LinkSnapshot(CompanyLinkStatus Status, CompanyLinkScope Scopes, DateTimeOffset? EffectiveTo);
}
