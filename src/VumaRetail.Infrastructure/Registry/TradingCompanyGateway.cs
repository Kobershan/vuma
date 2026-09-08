using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Registry.Trading;
using VumaRetail.Infrastructure.Persistence;

namespace VumaRetail.Infrastructure.Registry;

/// <summary>Opens child scopes bound to one company for trading-session reservation legs (Stage 09b).</summary>
public sealed class TradingCompanyGateway(IServiceScopeFactory scopes) : ITradingCompanyGateway
{
    /// <inheritdoc />
    public async Task<T> RunReservationAsync<T>(
        Guid tenantId,
        Guid companyId,
        Func<IServiceProvider, Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A reservation leg needs its tenant.", nameof(tenantId));
        }

        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("A reservation leg needs its company.", nameof(companyId));
        }

        using IServiceScope scope = scopes.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().SetCompany(companyId);

        // Deliberately no company context opened here: IReservationService opens its own
        // through the company factory, bound by this scope's company. Opening one here as
        // well would trip the factory's one-context-per-scope guard.
        return await action(scope.ServiceProvider).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RunReservationAsync(
        Guid tenantId,
        Guid companyId,
        Func<IServiceProvider, Task> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A reservation leg needs its tenant.", nameof(tenantId));
        }

        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("A reservation leg needs its company.", nameof(companyId));
        }

        using IServiceScope scope = scopes.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().SetCompany(companyId);

        await action(scope.ServiceProvider).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Guid> ResolveDefaultLocationAsync(
        Guid tenantId,
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A location read needs its tenant.", nameof(tenantId));
        }

        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("A location read needs its company.", nameof(companyId));
        }

        using IServiceScope scope = scopes.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().SetCompany(companyId);

        // The file's single company-context acquisition (MultiCompanyGuardTests counts textual
        // .CreateAsync occurrences per file): every company-database read in this gateway goes
        // through this one site, in its own scope, never two at once.
        ICompanyDbContextFactory companies = scope.ServiceProvider.GetRequiredService<ICompanyDbContextFactory>();
        await using VumaRetailDbContext companyDb = await companies
            .CreateAsync(CompanyAccessMode.Read, cancellationToken)
            .ConfigureAwait(false);

        StockLocation? first = await companyDb.StockLocations
            .OrderBy(location => location.Code)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return first?.Id
            ?? throw new TradingSessionException(
                "TRADING_NO_LOCATION", $"Company {companyId} has no stock location to sell from.");
    }
}
