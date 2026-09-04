using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Abstractions.Licensing;
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
    IPrincipalAccessor? principal = null,
    ICompanyFanOut? fanOut = null,
    IUnitOfWork? unitOfWork = null) : IPremisesService
{
    private readonly VumaRegistryDbContext _registry = registry;
    private readonly IClock _clock = clock;
    private readonly ITenantContext _tenantContext = tenantContext;
    private readonly IPrincipalAccessor? _principal = principal;
    private readonly ICompanyFanOut? _fanOut = fanOut;
    private readonly IUnitOfWork? _unitOfWork = unitOfWork;

    public async Task<Premises> CreateAsync(Guid tenantId, string code, string name, string address, string geoLocation, string tradingHours, CancellationToken cancellationToken = default)
    {
        var premises = Premises.Create(tenantId, code, name, address, geoLocation, tradingHours);
        _registry.Premises.Add(premises);
        await _registry.CommitAsync(cancellationToken);
        return premises;
    }

    public async Task<PremisesOccupancy> AddOccupancyAsync(Guid premisesId, Guid companyId, Guid storeId, CancellationToken cancellationToken = default)
    {
        var occupancy = PremisesOccupancy.Create(_tenantContext.TenantId, premisesId, companyId, storeId, _clock.UtcNow);
        _registry.PremisesOccupancies.Add(occupancy);
        await _registry.CommitAsync(cancellationToken);
        return occupancy;
    }

    public async Task PublishBinLayoutAsync(Guid premisesId, CancellationToken cancellationToken = default)
    {
        var layouts = await _registry.PremisesBinLayouts
            .Where(b => b.PremisesId == premisesId)
            .ToListAsync(cancellationToken);

        if (_fanOut is null || _unitOfWork is null)
            return;

        foreach (var layout in layouts)
        {
            await _fanOut.ReadAsync(
                [layout.PremisesId],
                async (companyId, ct) =>
                {
                    return true;
                },
                cancellationToken);
        }
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
        var access = RegistryUserCompanyAccess.Create(_tenantContext.TenantId, registryUserId, companyId, roles, grantedBy, _clock.UtcNow);
        _registry.RegistryUserCompanyAccesses.Add(access);
        await _registry.CommitAsync(cancellationToken);
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
        => await _registry.RegistryUsers
            .CountAsync(u => u.OperatorId != Guid.Empty, cancellationToken);

    public async Task<int> TillsPerCompanyAsync(Guid companyId, CancellationToken cancellationToken = default)
        => await _registry.Terminals
            .CountAsync(t => t.IsActive, cancellationToken);

    public async Task<int> ActiveLinksAsync(CancellationToken cancellationToken = default)
        => await _registry.CompanyLinks
            .CountAsync(l => l.Status == CompanyLinkStatus.Active, cancellationToken);
}
