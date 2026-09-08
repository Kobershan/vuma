#pragma warning disable CS1591
using VumaRetail.Domain.CustomerAccounts;

namespace VumaRetail.Application.Abstractions.CustomerAccounts;

public interface ICustomerAccountRepository
{
    Task<CustomerAccount?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CustomerAccount?> FindByNumberAsync(string number, CancellationToken cancellationToken = default);
    Task<CustomerAccount?> FindByPartnerAsync(Guid partnerId, CancellationToken cancellationToken = default);
    void Add(CustomerAccount account);
}

public interface ILayByAgreementRepository
{
    Task<LayByAgreement?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    Task<LayByAgreement?> FindByNumberAsync(string number, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LayByAgreement>> ListActiveAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LayByAgreement>> ListExpiringAsync(DateTimeOffset before, CancellationToken cancellationToken = default);
    void Add(LayByAgreement agreement);
}

public interface ICustomerFinanceTermsRepository
{
    Task<CustomerFinanceTerms?> FindAsync(CancellationToken cancellationToken = default);
    void Add(CustomerFinanceTerms terms);
}

public interface IAccountHolderRepository
{
    Task<IReadOnlyList<AccountHolder>> ListForAccountAsync(Guid accountId, CancellationToken cancellationToken = default);
    void Add(AccountHolder holder);
}
