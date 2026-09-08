using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Abstractions.CustomerAccounts;
using VumaRetail.Domain.CustomerAccounts;

namespace VumaRetail.Infrastructure.Persistence.Repositories;

/// <summary>EF Core implementation of <see cref="ICustomerAccountRepository"/>.</summary>
/// <param name="context">The database context.</param>
public sealed class CustomerAccountRepository(VumaRetailDbContext context) : ICustomerAccountRepository
{
    /// <inheritdoc />
    public Task<CustomerAccount?> FindAsync(Guid id, CancellationToken cancellationToken = default)
        => context.CustomerAccounts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<CustomerAccount?> FindByNumberAsync(string number, CancellationToken cancellationToken = default)
        => context.CustomerAccounts.FirstOrDefaultAsync(a => a.AccountNumber == number, cancellationToken);

    /// <inheritdoc />
    public Task<CustomerAccount?> FindByPartnerAsync(Guid partnerId, CancellationToken cancellationToken = default)
        => context.CustomerAccounts.FirstOrDefaultAsync(a => a.PartnerId == partnerId, cancellationToken);

    /// <inheritdoc />
    public void Add(CustomerAccount account) => context.CustomerAccounts.Add(account);
}

/// <summary>EF Core implementation of <see cref="IAccountHolderRepository"/>.</summary>
/// <param name="context">The database context.</param>
public sealed class AccountHolderRepository(VumaRetailDbContext context) : IAccountHolderRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<AccountHolder>> ListForAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        return await context.AccountHolders
            .AsNoTracking()
            .Where(h => h.AccountId == accountId)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public void Add(AccountHolder holder) => context.AccountHolders.Add(holder);
}

/// <summary>EF Core implementation of <see cref="ICustomerFinanceTermsRepository"/>.</summary>
/// <param name="context">The database context.</param>
public sealed class CustomerFinanceTermsRepository(VumaRetailDbContext context) : ICustomerFinanceTermsRepository
{
    /// <inheritdoc />
    public Task<CustomerFinanceTerms?> FindAsync(CancellationToken cancellationToken = default)
        => context.CustomerFinanceTerms.FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public void Add(CustomerFinanceTerms terms) => context.CustomerFinanceTerms.Add(terms);
}

/// <summary>EF Core implementation of <see cref="ILayByAgreementRepository"/>.</summary>
/// <param name="context">The database context.</param>
/// <remarks>
/// Lines and instalments load with the agreement, always: totals and sequences recompute from
/// them, so an agreement loaded without them would silently misbehave.
/// </remarks>
public sealed class LayByAgreementRepository(VumaRetailDbContext context) : ILayByAgreementRepository
{
    /// <inheritdoc />
    public Task<LayByAgreement?> FindAsync(Guid id, CancellationToken cancellationToken = default)
        => context.LayByAgreements
            .Include(a => a.Lines)
            .Include(a => a.Instalments)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<LayByAgreement?> FindByNumberAsync(string number, CancellationToken cancellationToken = default)
        => context.LayByAgreements
            .Include(a => a.Lines)
            .Include(a => a.Instalments)
            .FirstOrDefaultAsync(a => a.AgreementNumber == number, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<LayByAgreement>> ListActiveAsync(CancellationToken cancellationToken = default)
    {
        return await context.LayByAgreements
            .AsNoTracking()
            .Where(a => a.Status == LayByStatus.Active)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LayByAgreement>> ListExpiringAsync(DateTimeOffset before, CancellationToken cancellationToken = default)
    {
        return await context.LayByAgreements
            .AsNoTracking()
            .Where(a => a.Status == LayByStatus.Active && a.ExpiryDate <= before)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public void Add(LayByAgreement agreement) => context.LayByAgreements.Add(agreement);
}
