using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Crm;
using VumaRetail.Application.Loyalty;
using VumaRetail.Application.Loyalty.Commands;
using VumaRetail.Domain.Loyalty;
using VumaRetail.Infrastructure.Loyalty;

namespace VumaRetail.UnitTests.Loyalty;

/// <summary>
/// Drives the real loyalty command handlers against an in-memory transaction log and the real
/// in-memory Orbit ledger. The log is real (idempotency truly enforced); only the surrounding
/// repositories are stubbed.
/// </summary>
public sealed class LoyaltyDriver
{
    private readonly List<LoyaltyTransaction> _log = [];
    private readonly object _gate = new();
    private DateTimeOffset _now;

    public LoyaltyDriver()
    {
        TenantId = Guid.NewGuid();
        CompanyId = Guid.NewGuid();
        CustomerId = Guid.NewGuid();
        _now = new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

        Orbit = new InMemoryOrbitClient();
        string orbitId = Orbit.EnsureMemberAsync(CustomerId).GetAwaiter().GetResult();

        Settings = new LoyaltySettings(TenantId, CompanyId, "ZAR");
        Settings.Enable(1m, 365);

        Member = new LoyaltyMember(TenantId, CompanyId, CustomerId, orbitId, _now);

        var settingsRepo = Substitute.For<ILoyaltySettingsRepository>();
        settingsRepo.FindAsync(CompanyId, Arg.Any<CancellationToken>()).Returns(Settings);

        var memberRepo = Substitute.For<ILoyaltyMemberRepository>();
        memberRepo.FindByCustomerAsync(CustomerId, Arg.Any<CancellationToken>()).Returns(Member);
        memberRepo.FindByCustomerAsync(Arg.Is<Guid>(id => id != CustomerId), Arg.Any<CancellationToken>())
            .Returns((LoyaltyMember?)null);
        memberRepo.FindByOrbitIdAsync(orbitId, Arg.Any<CancellationToken>()).Returns(Member);

        var tierRepo = Substitute.For<ILoyaltyTierRepository>();
        tierRepo.FindAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((LoyaltyTier?)null);

        var consents = Substitute.For<IConsentService>();
        consents.IsValidAsync(Arg.Any<Guid>(), Arg.Any<Domain.Crm.ConsentType>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(_ => _now);

        SettingsRepo = settingsRepo;
        MemberRepo = memberRepo;
        TierRepo = tierRepo;
        Consents = consents;
        Transactions = new TxLog(_log, _gate);

        Earns = new EarnPointsCommandHandler(
            settingsRepo, memberRepo, Transactions, tierRepo, Orbit, consents, tenant, clock);
        Redeems = new RedeemPointsCommandHandler(
            settingsRepo, memberRepo, Transactions, Orbit, tenant, clock);
        Retries = new RetryLoyaltyTransactionCommandHandler(Transactions, memberRepo, Orbit, clock);
    }

    public Guid TenantId { get; }

    public Guid CompanyId { get; }

    public Guid CustomerId { get; }

    public InMemoryOrbitClient Orbit { get; }

    public LoyaltySettings Settings { get; }

    public LoyaltyMember Member { get; }

    public ILoyaltySettingsRepository SettingsRepo { get; }

    public ILoyaltyMemberRepository MemberRepo { get; }

    public ILoyaltyTierRepository TierRepo { get; }

    public IConsentService Consents { get; }

    public ILoyaltyTransactionRepository Transactions { get; }

    public EarnPointsCommandHandler Earns { get; }

    public RedeemPointsCommandHandler Redeems { get; }

    public RetryLoyaltyTransactionCommandHandler Retries { get; }

    /// <summary>Moves the driver's clock. The handlers read time only through it.</summary>
    /// <param name="delta">How far forward.</param>
    public void Advance(TimeSpan delta) => _now += delta;

    /// <summary>Seeds a transaction straight into the log (e.g. a stuck Pending row).</summary>
    /// <param name="transaction">The row.</param>
    public void Seed(LoyaltyTransaction transaction)
    {
        lock (_gate)
        {
            _log.Add(transaction);
        }
    }

    /// <summary>How many rows the log holds.</summary>
    public int Logged
    {
        get
        {
            lock (_gate)
            {
                return _log.Count;
            }
        }
    }

    private sealed class TxLog(List<LoyaltyTransaction> log, object gate) : ILoyaltyTransactionRepository
    {
        public Task<LoyaltyTransaction?> FindAsync(Guid transactionId, CancellationToken cancellationToken = default)
        {
            lock (gate)
            {
                return Task.FromResult(log.FirstOrDefault(row => row.Id == transactionId));
            }
        }

        public Task<LoyaltyTransaction?> FindByKeyAsync(Guid companyId, Guid idempotencyKey, CancellationToken cancellationToken = default)
        {
            lock (gate)
            {
                return Task.FromResult(log.FirstOrDefault(row =>
                    row.CompanyId == companyId && row.IdempotencyKey == idempotencyKey));
            }
        }

        public Task<IReadOnlyList<LoyaltyTransaction>> ListForMemberAsync(Guid customerId, int limit, CancellationToken cancellationToken = default)
        {
            lock (gate)
            {
                return Task.FromResult<IReadOnlyList<LoyaltyTransaction>>(
                    [.. log.Where(row => row.CustomerId == customerId).Take(limit)]);
            }
        }

        public Task<IReadOnlyList<LoyaltyTransaction>> ListQueuedAsync(DateTimeOffset notBefore, int limit, CancellationToken cancellationToken = default)
        {
            lock (gate)
            {
                return Task.FromResult<IReadOnlyList<LoyaltyTransaction>>(
                    [.. log.Where(row => row.Status == TransactionStatus.QueuedForRetry).Take(limit)]);
            }
        }

        public Task<IReadOnlyList<LoyaltyTransaction>> ListConfirmedAsync(Guid customerId, CancellationToken cancellationToken = default)
        {
            lock (gate)
            {
                return Task.FromResult<IReadOnlyList<LoyaltyTransaction>>(
                    [.. log.Where(row => row.CustomerId == customerId && row.Status == TransactionStatus.Confirmed)]);
            }
        }

        public void Add(LoyaltyTransaction transaction)
        {
            lock (gate)
            {
                log.Add(transaction);
            }
        }
    }
}
