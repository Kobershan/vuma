using VumaRetail.Domain.Registry;
using VumaRetail.Domain.Primitives;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace VumaRetail.Infrastructure.Registry;

/// <summary>Enforces company link checks at the point of use.</summary>
public sealed class CompanyLinkService : ICompanyLinkService
{
    private readonly VumaRegistryDbContext _registry;
    private readonly IClock _clock;

    public CompanyLinkService(VumaRegistryDbContext registry, IClock clock)
    {
        _registry = registry;
        _clock = clock;
    }

    public async Task RequireLink(Guid companyA, Guid companyB, CompanyLinkScope requiredScope, CancellationToken cancellationToken = default)
    {
        if (companyA == Guid.Empty) throw new ArgumentException("Company A is required.", nameof(companyA));
        if (companyB == Guid.Empty) throw new ArgumentException("Company B is required.", nameof(companyB));
        if (companyA == companyB) return;

        var link = await TryGetLinkAsync(companyA, companyB, cancellationToken);
        if (link is null)
            throw new CompanyLinkRequiredException(companyA, companyB, requiredScope);

        if (link.Status != CompanyLinkStatus.Active)
            throw new CompanyLinkRequiredException(companyA, companyB, requiredScope);

        if (!link.Scopes.HasFlag(requiredScope))
            throw new CompanyLinkRequiredException(companyA, companyB, requiredScope);
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
        var (smaller, larger) = companyA.CompareTo(companyB) < 0 ? (companyA, companyB) : (companyB, companyA);

        var existing = await _registry.CompanyLinks
            .FirstOrDefaultAsync(l => l.CompanyAId == smaller && l.CompanyBId == larger, cancellationToken);

        if (existing is not null && existing.Status != CompanyLinkStatus.Revoked)
            throw new InvalidOperationException("A link already exists between these companies.");

        var link = new CompanyLink
        {
            Id = UuidV7.NewGuid(),
            CompanyAId = smaller,
            CompanyBId = larger,
            Scopes = scopes,
            Status = CompanyLinkStatus.Proposed,
            EffectiveFrom = _clock.UtcNow,
        };

        _registry.CompanyLinks.Add(link);
        await _registry.CommitAsync(cancellationToken);
        return link;
    }

    public async Task AcceptAsync(Guid linkId, Guid acceptingCompanyId, CancellationToken cancellationToken = default)
    {
        var link = await _registry.CompanyLinks.FindAsync(new object[] { linkId }, cancellationToken)
            ?? throw new InvalidOperationException("Link not found.");

        if (link.Status != CompanyLinkStatus.Proposed)
            throw new InvalidOperationException("Link is not in Proposed state.");

        if (link.CompanyAId == acceptingCompanyId) link.AcceptedByA = true;
        else if (link.CompanyBId == acceptingCompanyId) link.AcceptedByB = true;
        else throw new InvalidOperationException("Company is not part of this link.");

        if (link.AcceptedByA && link.AcceptedByB)
        {
            link.Status = CompanyLinkStatus.Active;
            link.AcceptedAt = _clock.UtcNow;
        }

        await _registry.CommitAsync(cancellationToken);
    }

    public async Task SuspendAsync(Guid linkId, string reason, CancellationToken cancellationToken = default)
    {
        var link = await _registry.CompanyLinks.FindAsync(new object[] { linkId }, cancellationToken)
            ?? throw new InvalidOperationException("Link not found.");

        if (link.Status != CompanyLinkStatus.Active)
            throw new InvalidOperationException("Only active links can be suspended.");

        link.Status = CompanyLinkStatus.Suspended;
        link.SuspendedAt = _clock.UtcNow;
        link.SuspendedReason = reason;

        await _registry.CommitAsync(cancellationToken);
    }

    public async Task RevokeAsync(Guid linkId, string reason, CancellationToken cancellationToken = default)
    {
        var link = await _registry.CompanyLinks.FindAsync(new object[] { linkId }, cancellationToken)
            ?? throw new InvalidOperationException("Link not found.");

        if (link.Status == CompanyLinkStatus.Revoked)
            throw new InvalidOperationException("Link is already revoked.");

        link.Status = CompanyLinkStatus.Revoked;
        link.RevokedAt = _clock.UtcNow;
        link.RevokedReason = reason;

        await _registry.CommitAsync(cancellationToken);
    }
}

public class CompanyLinkRequiredException : InvalidOperationException
{
    public Guid CompanyA { get; }
    public Guid CompanyB { get; }
    public CompanyLinkScope RequiredScope { get; }

    public CompanyLinkRequiredException(Guid companyA, Guid companyB, CompanyLinkScope requiredScope)
        : base($"Companies {companyA} and {companyB} do not have an active link with scope {requiredScope}.")
    {
        CompanyA = companyA;
        CompanyB = companyB;
        RequiredScope = requiredScope;
    }
}
