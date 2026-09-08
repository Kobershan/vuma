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

/// <summary>Reads and writes stokvel groups, members and hamper baskets (Stage 10b, TASK-10B-002).</summary>
/// <remarks>Tracked entities, never <c>Update</c> — the pipeline's unit of work commits what the handler mutated.</remarks>
public interface IStokvelGroupRepository
{
    Task<StokvelGroup?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    Task<StokvelGroup?> FindByNumberAsync(string number, CancellationToken cancellationToken = default);
    Task<StokvelMember?> FindMemberAsync(Guid memberId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StokvelMember>> ListMembersAsync(Guid groupId, CancellationToken cancellationToken = default);
    Task<HamperBasket?> FindBasketAsync(Guid basketId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<HamperBasket>> ListBasketsAsync(Guid groupId, CancellationToken cancellationToken = default);
    void AddGroup(StokvelGroup group);
    void AddMember(StokvelMember member);
    void AddBasket(HamperBasket basket);
}

/// <summary>Reads and appends stokvel contributions and benefit allocations (append-only: no update path).</summary>
public interface IStokvelContributionRepository
{
    Task<StokvelContribution?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    Task<StokvelContribution?> FindByReceiptAsync(Guid memberId, string receiptReference, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StokvelContribution>> ListForMemberAsync(Guid memberId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StokvelContribution>> ListForGroupAsync(Guid groupId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StokvelBenefitAllocation>> ListBenefitsForMemberAsync(Guid memberId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StokvelBenefitAllocation>> ListBenefitsForGroupAsync(Guid groupId, CancellationToken cancellationToken = default);
    void AddContribution(StokvelContribution contribution);
    void AddBenefit(StokvelBenefitAllocation benefit);
}

/// <summary>Reads and writes stokvel payouts.</summary>
public interface IStokvelPayoutRepository
{
    Task<StokvelPayout?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StokvelPayout>> ListForMemberAsync(Guid memberId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StokvelPayout>> ListForGroupAsync(Guid groupId, CancellationToken cancellationToken = default);
    void Add(StokvelPayout payout);
}
