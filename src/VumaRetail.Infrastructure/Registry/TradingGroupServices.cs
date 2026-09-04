using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Abstractions.Sync;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Identity;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry;
using VumaRetail.Infrastructure.Persistence;

namespace VumaRetail.Infrastructure.Registry;

public sealed class OperatorContext(ILogger<OperatorContext> logger, IPrincipalAccessor principalAccessor) : IOperatorContext
{
    private readonly ILogger<OperatorContext> _logger = logger;
    private readonly IPrincipalAccessor _principalAccessor = principalAccessor;

    public Guid? OperatorId { get; private set; }
    public string? OperatorName { get; private set; }
    public bool IsActive { get; private set; } = true;
    public string? LicenceFingerprint { get; private set; }
    public string Principal => _principalAccessor.Principal;

    public void SetOperator(Guid operatorId, string? operatorName, string? licenceFingerprint, bool isActive)
    {
        if (operatorId == Guid.Empty)
        {
            throw new ArgumentException("An operator identifier is required.", nameof(operatorId));
        }

        OperatorId = operatorId;
        OperatorName = operatorName;
        LicenceFingerprint = licenceFingerprint;
        IsActive = isActive;
    }

    public Guid RequireOperatorId()
    {
        if (OperatorId is not { } operatorId || operatorId == Guid.Empty)
        {
            throw new InvalidOperationException("No active operator context.");
        }

        if (!IsActive)
        {
            throw new InvalidOperationException("The acting operator is not active.");
        }

        return operatorId;
    }
}

public sealed class PremisesService(
    VumaRegistryDbContext registry,
    IClock clock,
    ITenantContext tenantContext,
    ICompanyLinkService companyLinkService,
    ISagaCoordinator sagaCoordinator,
    IOperatorContext operatorContext,
    IPrincipalAccessor? principal = null,
    IHybridClock? hybridClock = null) : IPremisesService
{
    private readonly VumaRegistryDbContext _registry = registry;
    private readonly IClock _clock = clock;
    private readonly ITenantContext _tenantContext = tenantContext;
    private readonly ICompanyLinkService _companyLinkService = companyLinkService;
    private readonly ISagaCoordinator _sagaCoordinator = sagaCoordinator;
    private readonly IOperatorContext _operatorContext = operatorContext;
    private readonly IPrincipalAccessor? _principal = principal;
    private readonly IHybridClock? _hybridClock = hybridClock;

    public async Task<Premises> CreateAsync(Guid tenantId, string code, string name, string address, string geoLocation, string tradingHours, CancellationToken cancellationToken = default)
    {
        var premises = Premises.Create(tenantId, code, name, address, geoLocation, tradingHours);
        _registry.Premises.Add(premises);
        await _registry.CommitAsync(cancellationToken);
        return premises;
    }

    public async Task<PremisesOccupancy> AddOccupancyAsync(Guid premisesId, Guid companyId, Guid storeId, CancellationToken cancellationToken = default)
    {
        Guid tenantId = _tenantContext.TenantId;

        _ = await _registry.Premises
            .FirstOrDefaultAsync(p => p.Id == premisesId && p.TenantId == tenantId, cancellationToken)
            .ConfigureAwait(false) ?? throw new RegistryNotFoundException("premises", premisesId);

        _ = await _registry.Companies
            .FirstOrDefaultAsync(c => c.Id == companyId && c.TenantId == tenantId, cancellationToken)
            .ConfigureAwait(false) ?? throw new RegistryNotFoundException("company", companyId);

        // Holding a sister company's stock at a premises requires SharedFloor (TRADING_GROUP
        // §2). The first occupant needs no link; every later one links to each current
        // occupant, checked here at the point of use — not at configuration time.
        List<Guid> current = await _registry.PremisesOccupancies
            .Where(o => o.PremisesId == premisesId && o.TenantId == tenantId && o.OccupiesTo == null && o.CompanyId != companyId)
            .Select(o => o.CompanyId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (Guid other in current)
        {
            await _companyLinkService.RequireLink(companyId, other, CompanyLinkScope.SharedFloor, cancellationToken).ConfigureAwait(false);
        }

        var occupancy = PremisesOccupancy.Create(tenantId, premisesId, companyId, storeId, _clock.UtcNow);
        _registry.PremisesOccupancies.Add(occupancy);
        await _registry.CommitAsync(cancellationToken).ConfigureAwait(false);
        return occupancy;
    }

    public async Task PublishBinLayoutAsync(Guid premisesId, CancellationToken cancellationToken = default)
    {
        Guid tenantId = _tenantContext.TenantId;

        _ = await _registry.Premises
            .FirstOrDefaultAsync(p => p.Id == premisesId && p.TenantId == tenantId, cancellationToken)
            .ConfigureAwait(false) ?? throw new RegistryNotFoundException("premises", premisesId);

        List<Guid> companies = await _registry.PremisesOccupancies
            .Where(o => o.PremisesId == premisesId && o.TenantId == tenantId && o.OccupiesTo == null)
            .Select(o => o.CompanyId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (companies.Count == 0)
        {
            return;
        }

        List<PremisesBinLayout> layouts = await _registry.PremisesBinLayouts
            .Where(b => b.PremisesId == premisesId && b.TenantId == tenantId)
            .OrderBy(b => b.ZoneCode)
            .ThenBy(b => b.BinCode)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (layouts.Count == 0)
        {
            return;
        }

        // The mirror is a saga: one leg per occupying company, idempotent on the layout
        // fingerprint, so republishing an unchanged layout is a no-op (ADR-124).
        string fingerprint = Fingerprint(layouts);
        string idempotencyKey = $"premises-bin-layout:{premisesId:N}:{fingerprint}";

        SagaIntent? existing = await _registry.SagaIntents
            .FirstOrDefaultAsync(i => i.TenantId == tenantId && i.IdempotencyKey == idempotencyKey, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return;
        }

        string payload = JsonSerializer.Serialize(new
        {
            premisesId,
            bins = layouts.Select(b => new { zone = b.ZoneCode, bin = b.BinCode, shared = b.IsShared }).ToArray(),
        });

        var intent = SagaIntent.Create(tenantId, "premises.bin-layout.publish", idempotencyKey, _clock.UtcNow, payload);

        foreach (Guid companyId in companies)
        {
            intent.AddLeg(companyId);
        }

        string stamp = _hybridClock?.Next().ToString() ?? HlcStamp.MinValue.ToString();
        intent.Authorize(
            _operatorContext.RequireOperatorId(),
            _principal?.Principal ?? "system:premises-mirror",
            stamp);

        await _sagaCoordinator.ExecuteAsync(intent, cancellationToken).ConfigureAwait(false);
    }

    private static string Fingerprint(IReadOnlyList<PremisesBinLayout> layouts)
    {
        StringBuilder content = new();

        foreach (PremisesBinLayout layout in layouts)
        {
            content.Append(layout.ZoneCode).Append('|').Append(layout.BinCode).Append('|').Append(layout.IsShared).Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content.ToString())))[..16];
    }

    public async Task<IReadOnlyList<PremisesOccupancy>> GetOccupanciesAsync(Guid premisesId, CancellationToken cancellationToken = default)
        => await _registry.PremisesOccupancies
            .Where(o => o.PremisesId == premisesId)
            .ToListAsync(cancellationToken);
}

public sealed class RegistryUserService(
    VumaRegistryDbContext registry,
    IClock clock,
    ITenantContext tenantContext,
    IPrincipalAccessor? principal = null) : IRegistryUserService
{
    private readonly VumaRegistryDbContext _registry = registry;
    private readonly IClock _clock = clock;
    private readonly ITenantContext _tenantContext = tenantContext;
    private readonly IPrincipalAccessor? _principal = principal;

    public async Task<RegistryUser> CreateAsync(Guid tenantId, string login, string displayName, Guid operatorId, string contactDetails, CancellationToken cancellationToken = default)
    {
        var user = RegistryUser.Create(tenantId, login, displayName, operatorId, contactDetails);
        _registry.RegistryUsers.Add(user);
        await _registry.CommitAsync(cancellationToken);
        return user;
    }

    public async Task<RegistryUserCompanyAccess> GrantAccessAsync(Guid registryUserId, Guid companyId, string roles, string grantedBy, CancellationToken cancellationToken = default)
    {
        Guid tenantId = _tenantContext.TenantId;

        RegistryUser user = await _registry.RegistryUsers
            .FirstOrDefaultAsync(u => u.Id == registryUserId && u.TenantId == tenantId, cancellationToken)
            .ConfigureAwait(false) ?? throw new RegistryNotFoundException("registry user", registryUserId);

        Company company = await _registry.Companies
            .FirstOrDefaultAsync(c => c.Id == companyId && c.TenantId == tenantId, cancellationToken)
            .ConfigureAwait(false) ?? throw new RegistryNotFoundException("company", companyId);

        // Business rule 7: a user may only be granted companies under the same Operator ID.
        if (user.OperatorId != company.OperatorId)
        {
            throw RegistryExceptions.CrossOperatorAccessDenied(registryUserId, user.OperatorId, company.OperatorId);
        }

        // A retried grant returns the existing row rather than a duplicate.
        RegistryUserCompanyAccess? existing = await _registry.RegistryUserCompanyAccesses
            .FirstOrDefaultAsync(a => a.RegistryUserId == registryUserId && a.CompanyId == companyId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return existing;
        }

        var access = RegistryUserCompanyAccess.Create(tenantId, registryUserId, companyId, roles, grantedBy, _clock.UtcNow);
        _registry.RegistryUserCompanyAccesses.Add(access);
        await _registry.CommitAsync(cancellationToken).ConfigureAwait(false);
        return access;
    }

    public async Task RevokeAccessAsync(Guid registryUserId, Guid companyId, CancellationToken cancellationToken = default)
    {
        var existing = await _registry.RegistryUserCompanyAccesses
            .Where(a => a.RegistryUserId == registryUserId && a.CompanyId == companyId)
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            _registry.RegistryUserCompanyAccesses.Remove(existing);
            await _registry.CommitAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<Guid>> ListUserCompaniesAsync(Guid registryUserId, CancellationToken cancellationToken = default)
        => await _registry.RegistryUserCompanyAccesses
            .Where(a => a.RegistryUserId == registryUserId)
            .Select(a => a.CompanyId)
            .ToListAsync(cancellationToken);
}

public sealed class TerminalService(
    VumaRegistryDbContext registry,
    ITenantContext tenantContext) : ITerminalService
{
    private readonly VumaRegistryDbContext _registry = registry;
    private readonly ITenantContext _tenantContext = tenantContext;

    public async Task<RegistryTerminal> RegisterAsync(Guid tenantId, Guid premisesId, string terminalId, string deviceCertThumbprint, CancellationToken cancellationToken = default)
    {
        RegistryTerminal? duplicate = await _registry.Terminals
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.TerminalId == terminalId, cancellationToken)
            .ConfigureAwait(false);

        if (duplicate is not null)
        {
            throw RegistryConflictException.TerminalAlreadyRegistered(terminalId);
        }

        var terminal = RegistryTerminal.Create(tenantId, premisesId, terminalId, deviceCertThumbprint);
        _registry.Terminals.Add(terminal);
        await _registry.CommitAsync(cancellationToken).ConfigureAwait(false);
        return terminal;
    }

    public async Task SetCompaniesAsync(Guid terminalId, IReadOnlyList<Guid> companyIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(companyIds);

        RegistryTerminal terminal = await _registry.Terminals
            .FirstOrDefaultAsync(t => t.Id == terminalId && t.TenantId == _tenantContext.TenantId, cancellationToken)
            .ConfigureAwait(false) ?? throw new RegistryNotFoundException("terminal", terminalId);

        List<Guid> distinct = companyIds.Where(c => c != Guid.Empty).Distinct().ToList();
        if (distinct.Count == 0)
        {
            throw new ArgumentException("At least one company is required.", nameof(companyIds));
        }

        List<Company> companies = await _registry.Companies
            .Where(c => c.TenantId == _tenantContext.TenantId && distinct.Contains(c.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Guid missing = distinct.FirstOrDefault(c => companies.All(x => x.Id != c));

        if (missing != Guid.Empty)
        {
            throw new RegistryNotFoundException("company", missing);
        }

        if (companies.Any(c => c.OperatorId == Guid.Empty))
        {
            throw RegistryRuleException.TillCompanyNeedsAnOperator();
        }

        if (companies.Select(c => c.OperatorId).Distinct().Count() != 1)
        {
            throw RegistryRuleException.TillMustStayUnderOneOperator();
        }

        terminal.SetCompanies(distinct);
        await _registry.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Resolves a login's registry company membership at sign-in time (ADR-127).
/// </summary>
/// <remarks>
/// Matches the identity login against the registry directory by sign-in name. A login with no
/// registry row keeps the pre-registry token exactly — enrichment adds claims, never removes them.
/// </remarks>
public sealed class RegistryTokenCompanyEnricher(IDbContextFactory<VumaRegistryDbContext> factory) : ITokenCompanyEnricher
{
    private readonly IDbContextFactory<VumaRegistryDbContext> _factory = factory;

    /// <inheritdoc />
    public async Task<TokenCompanyEnrichment> EnrichAsync(Guid tenantId, string userName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);

        await using VumaRegistryDbContext registry = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        string login = userName.Trim().ToLowerInvariant();

        RegistryUser? user = await registry.RegistryUsers
            .FirstOrDefaultAsync(u => u.TenantId == tenantId && u.Login.ToLower() == login, cancellationToken)
            .ConfigureAwait(false);

        if (user is not { IsEnabled: true })
        {
            return TokenCompanyEnrichment.Empty;
        }

        List<CompanyMembership> companies = await registry.RegistryUserCompanyAccesses
            .Where(a => a.TenantId == tenantId && a.RegistryUserId == user.Id)
            .Select(a => new CompanyMembership(a.CompanyId, a.Roles))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new TokenCompanyEnrichment(user.OperatorId, companies);
    }
}

public sealed class EntitlementCounters(
    VumaRegistryDbContext registry,
    ICompanyFanOut? fanOut = null) : IEntitlementCounters
{
    private readonly VumaRegistryDbContext _registry = registry;
    private readonly ICompanyFanOut? _fanOut = fanOut;

    public async Task<int> CompaniesActiveAsync(CancellationToken cancellationToken = default)
        => await _registry.Companies
            .CountAsync(c => c.IsActive, cancellationToken);

    public async Task<int> NamedUsersPerCompanyAsync(Guid companyId, CancellationToken cancellationToken = default)
        => await _registry.RegistryUserCompanyAccesses
            .Where(a => a.CompanyId == companyId
                && _registry.RegistryUsers.Any(u => u.Id == a.RegistryUserId && u.IsEnabled))
            .Select(a => a.RegistryUserId)
            .Distinct()
            .CountAsync(cancellationToken);

    public async Task<int> TillsPerCompanyAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        // Terminal company membership lives in a uuid[] column, which EF cannot filter
        // server-side through the value conversion — so filter the tenant's active tills
        // here. The table holds tills, not transactions; the set is tiny by construction.
        List<List<Guid>> memberships = await _registry.Terminals
            .Where(t => t.IsActive)
            .Select(t => t.CompanyIds)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return memberships.Count(companies => companies.Contains(companyId));
    }

    public async Task<int> ActiveLinksAsync(CancellationToken cancellationToken = default)
        => await _registry.CompanyLinks
            .CountAsync(l => l.Status == CompanyLinkStatus.Active, cancellationToken);
}
