using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Crm;
using VumaRetail.Domain.Crm;
using VumaRetail.Domain.Loyalty;

namespace VumaRetail.Application.Loyalty.Commands;

/// <summary>What an earn produced.</summary>
/// <param name="TransactionId">The Vuma transaction.</param>
/// <param name="Points">Points requested.</param>
/// <param name="NewBalance">Balance after applying (cache after confirm).</param>
/// <param name="TierId">Tier after applying, if reported.</param>
/// <param name="Queued">True when Orbit was unreachable and the earn waits for retry.</param>
public sealed record EarnOutcome(Guid TransactionId, decimal Points, decimal NewBalance, string? TierId, bool Queued);

/// <summary>Earns points for a member (e.g. from a sale).</summary>
/// <param name="CompanyId">The owning company. Loyalty is per company.</param>
/// <param name="CustomerId">The member.</param>
/// <param name="PurchaseAmount">The purchase amount in the settings currency.</param>
/// <param name="Currency">ISO 4217 code. Must match the company settings.</param>
/// <param name="Reference">What caused it (e.g. sale id).</param>
/// <param name="IdempotencyKey">Client-supplied UUID v7. The at-most-once key.</param>
/// <param name="IsMarketingBonus">True for campaign bonuses — gated on marketing consent.</param>
/// <param name="StoreId">The owning store, if any.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record EarnPointsCommand(
    Guid CompanyId,
    Guid CustomerId,
    decimal PurchaseAmount,
    string Currency,
    string? Reference,
    Guid IdempotencyKey,
    bool IsMarketingBonus = false,
    Guid? StoreId = null) : ICommand<EarnOutcome>;

/// <summary>Rejects a malformed earn.</summary>
public sealed class EarnPointsCommandValidator : AbstractValidator<EarnPointsCommand>
{
    /// <summary>Builds the rules.</summary>
    public EarnPointsCommandValidator()
    {
        RuleFor(command => command.CompanyId).NotEmpty();
        RuleFor(command => command.CustomerId).NotEmpty();
        RuleFor(command => command.PurchaseAmount).GreaterThan(0m);
        RuleFor(command => command.Currency).NotEmpty().Length(3);
        RuleFor(command => command.IdempotencyKey).NotEmpty();
        RuleFor(command => command.Reference).MaximumLength(200);
    }
}

/// <summary>
/// Earns points: idempotent intent first, Orbit second, queue-and-retry when Orbit is down.
/// The till never blocks on the loyalty engine (R1).
/// </summary>
/// <param name="settings">Settings persistence.</param>
/// <param name="members">Member persistence.</param>
/// <param name="transactions">Transaction persistence.</param>
/// <param name="tiers">Tier persistence, for the earn multiplier.</param>
/// <param name="orbit">The loyalty engine boundary.</param>
/// <param name="consents">The Stage 19 consent contract.</param>
/// <param name="tenant">The ambient tenant.</param>
/// <param name="clock">The only source of time.</param>
public sealed class EarnPointsCommandHandler(
    ILoyaltySettingsRepository settings,
    ILoyaltyMemberRepository members,
    ILoyaltyTransactionRepository transactions,
    ILoyaltyTierRepository tiers,
    IOrbitClient orbit,
    IConsentService consents,
    ITenantContext tenant,
    IClock clock) : ICommandHandler<EarnPointsCommand, EarnOutcome>
{
    /// <inheritdoc />
    public async Task<EarnOutcome> HandleAsync(EarnPointsCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        LoyaltySettings company = await settings
            .FindAsync(command.CompanyId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new LoyaltyDisabledException();

        if (!company.IsEnabled)
        {
            throw new LoyaltyDisabledException();
        }

        LoyaltyMember member = await members
            .FindByCustomerAsync(command.CustomerId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new LoyaltyMemberNotFoundException();

        // Loyalty is per company: a member of company A is invisible to company B. Answered as
        // not-found (never "wrong company") so one company cannot probe another's membership.
        if (member.CompanyId != command.CompanyId)
        {
            throw new LoyaltyMemberNotFoundException();
        }

        DateTimeOffset now = clock.UtcNow;

        if (command.IsMarketingBonus
            && !await consents
                .IsValidAsync(command.CustomerId, ConsentType.MarketingEmail, now, cancellationToken)
                .ConfigureAwait(false))
        {
            throw new ConsentNotGivenException(ConsentType.MarketingEmail);
        }

        decimal multiplier = 1m;
        if (member.TierId is not null)
        {
            LoyaltyTier? tier = await tiers
                .FindAsync(command.CompanyId, member.TierId, cancellationToken)
                .ConfigureAwait(false);
            multiplier = tier?.Multiplier ?? 1m;
        }

        decimal points = LoyaltyCalculator.ComputeEarn(command.PurchaseAmount, company.EarnRate, multiplier);

        LoyaltyTransaction? replay = await transactions
            .FindByKeyAsync(command.CompanyId, command.IdempotencyKey, cancellationToken)
            .ConfigureAwait(false);

        if (replay is not null)
        {
            return ToReplay(replay, command, points);
        }

        var transaction = new LoyaltyTransaction(
            tenant.TenantId,
            command.CompanyId,
            command.CustomerId,
            TransactionType.Earn,
            points,
            command.Currency,
            command.IdempotencyKey,
            command.Reference,
            now,
            command.StoreId ?? member.StoreId);

        transactions.Add(transaction);

        // Inside the pipeline transaction by design (see remarks on the retry command): the
        // Pending row and the confirm/queue decision commit atomically, so a crash between the
        // Orbit call and the commit replays the whole handler under the same idempotency key
        // rather than leaving a confirmed Orbit credit with no local record.
        try
        {
            OrbitEarnResult applied = await orbit
                .EarnAsync(
                    new OrbitEarnRequest(
                        member.OrbitMemberId, points, command.Currency, command.Reference,
                        command.IdempotencyKey),
                    cancellationToken)
                .ConfigureAwait(false);

            transaction.MarkConfirmed(applied.OrbitTransactionId, applied.NewBalance);
            member.RecordSync(applied.NewBalance, applied.TierId, now);
            return new EarnOutcome(transaction.Id, points, applied.NewBalance, applied.TierId, false);
        }
        catch (OrbitUnavailableException)
        {
            transaction.MarkQueuedForRetry();
            return new EarnOutcome(transaction.Id, points, member.BalanceCache, member.TierId, true);
        }
    }

    internal static EarnOutcome ToReplay(LoyaltyTransaction replay, EarnPointsCommand command, decimal points)
    {
        if (replay.Status is TransactionStatus.Pending or TransactionStatus.QueuedForRetry)
        {
            throw new LoyaltyRequestInProgressException();
        }

        if (replay.TransactionType != TransactionType.Earn
            || replay.CustomerId != command.CustomerId
            || replay.Amount != points)
        {
            throw new DuplicateLoyaltyTransactionException();
        }

        return new EarnOutcome(
            replay.Id, replay.Amount, replay.ResultingBalance ?? 0m, null, false);
    }
}

/// <summary>What a redemption produced.</summary>
/// <param name="TransactionId">The Vuma transaction.</param>
/// <param name="Points">Points debited.</param>
/// <param name="NewBalance">Balance after applying.</param>
/// <param name="Queued">True when Orbit was unreachable and the burn waits for retry.</param>
public sealed record RedeemOutcome(Guid TransactionId, decimal Points, decimal NewBalance, bool Queued);

/// <summary>Redeems points for a member (e.g. for a discount).</summary>
/// <param name="CompanyId">The owning company.</param>
/// <param name="CustomerId">The member.</param>
/// <param name="Points">Points to redeem. Must be positive.</param>
/// <param name="Reference">What caused it.</param>
/// <param name="IdempotencyKey">Client-supplied UUID v7. The at-most-once key.</param>
/// <param name="StoreId">The owning store, if any.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record RedeemPointsCommand(
    Guid CompanyId,
    Guid CustomerId,
    decimal Points,
    string? Reference,
    Guid IdempotencyKey,
    Guid? StoreId = null) : ICommand<RedeemOutcome>;

/// <summary>Rejects a malformed redemption.</summary>
public sealed class RedeemPointsCommandValidator : AbstractValidator<RedeemPointsCommand>
{
    /// <summary>Builds the rules.</summary>
    public RedeemPointsCommandValidator()
    {
        RuleFor(command => command.CompanyId).NotEmpty();
        RuleFor(command => command.CustomerId).NotEmpty();
        RuleFor(command => command.Points).GreaterThan(0m);
        RuleFor(command => command.IdempotencyKey).NotEmpty();
        RuleFor(command => command.Reference).MaximumLength(200);
    }
}

/// <summary>
/// Redeems points: provisional cache pre-check, authoritative Orbit debit, queue-and-retry on
/// outage. The cache never gates alone — competing burns serialise on Orbit's ledger.
/// </summary>
/// <param name="settings">Settings persistence.</param>
/// <param name="members">Member persistence.</param>
/// <param name="transactions">Transaction persistence.</param>
/// <param name="orbit">The loyalty engine boundary.</param>
/// <param name="tenant">The ambient tenant.</param>
/// <param name="clock">The only source of time.</param>
public sealed class RedeemPointsCommandHandler(
    ILoyaltySettingsRepository settings,
    ILoyaltyMemberRepository members,
    ILoyaltyTransactionRepository transactions,
    IOrbitClient orbit,
    ITenantContext tenant,
    IClock clock) : ICommandHandler<RedeemPointsCommand, RedeemOutcome>
{
    /// <inheritdoc />
    public async Task<RedeemOutcome> HandleAsync(RedeemPointsCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        LoyaltySettings company = await settings
            .FindAsync(command.CompanyId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new LoyaltyDisabledException();

        if (!company.IsEnabled)
        {
            throw new LoyaltyDisabledException();
        }

        LoyaltyMember member = await members
            .FindByCustomerAsync(command.CustomerId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new LoyaltyMemberNotFoundException();

        if (member.CompanyId != command.CompanyId)
        {
            throw new LoyaltyMemberNotFoundException();
        }

        // Provisional: refuses the obviously impossible without a network round trip. Orbit's
        // live debit remains authoritative and may still refuse.
        LoyaltyCalculator.ApplyBurn(member.BalanceCache, command.Points);

        LoyaltyTransaction? replay = await transactions
            .FindByKeyAsync(command.CompanyId, command.IdempotencyKey, cancellationToken)
            .ConfigureAwait(false);

        if (replay is not null)
        {
            return ToReplay(replay, command);
        }

        DateTimeOffset now = clock.UtcNow;
        var transaction = new LoyaltyTransaction(
            tenant.TenantId,
            command.CompanyId,
            command.CustomerId,
            TransactionType.Burn,
            command.Points,
            company.Currency,
            command.IdempotencyKey,
            command.Reference,
            now,
            command.StoreId ?? member.StoreId);

        transactions.Add(transaction);

        try
        {
            OrbitRedeemResult applied = await orbit
                .RedeemAsync(
                    new OrbitRedeemRequest(
                        member.OrbitMemberId, command.Points, command.Reference,
                        command.IdempotencyKey),
                    cancellationToken)
                .ConfigureAwait(false);

            if (!applied.Applied)
            {
                transaction.MarkFailed();
                throw new InsufficientPointsException();
            }

            transaction.MarkConfirmed(applied.OrbitTransactionId, applied.NewBalance);
            member.RecordSync(applied.NewBalance, applied.TierId, now);
            return new RedeemOutcome(transaction.Id, command.Points, applied.NewBalance, false);
        }
        catch (OrbitUnavailableException)
        {
            // The cache is NOT deducted: the provisional check was provisional, and deducting
            // now would let a failed retry spend the same points twice.
            transaction.MarkQueuedForRetry();
            return new RedeemOutcome(transaction.Id, command.Points, member.BalanceCache, true);
        }
    }

    internal static RedeemOutcome ToReplay(LoyaltyTransaction replay, RedeemPointsCommand command)
    {
        if (replay.Status is TransactionStatus.Pending or TransactionStatus.QueuedForRetry)
        {
            throw new LoyaltyRequestInProgressException();
        }

        if (replay.TransactionType != TransactionType.Burn
            || replay.CustomerId != command.CustomerId
            || replay.Amount != command.Points)
        {
            throw new DuplicateLoyaltyTransactionException();
        }

        return new RedeemOutcome(replay.Id, replay.Amount, replay.ResultingBalance ?? 0m, false);
    }
}

/// <summary>Where one retry left a queued transaction.</summary>
public enum RetryDisposition
{
    /// <summary>Orbit confirmed it.</summary>
    Confirmed = 0,

    /// <summary>Orbit still unreachable. Still queued.</summary>
    StillQueued = 1,

    /// <summary>Orbit refused it (e.g. insufficient ledger). Terminal.</summary>
    Refused = 2,

    /// <summary>Retries exhausted after 24 hours. Terminal; needs reconciliation.</summary>
    Expired = 3,
}

/// <summary>Retries one queued transaction. Dispatched per transaction by the retry worker.</summary>
/// <param name="TransactionId">The queued transaction.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record RetryLoyaltyTransactionCommand(Guid TransactionId) : ICommand<RetryDisposition>;

/// <summary>Rejects a malformed retry.</summary>
public sealed class RetryLoyaltyTransactionCommandValidator : AbstractValidator<RetryLoyaltyTransactionCommand>
{
    /// <summary>Builds the rules.</summary>
    public RetryLoyaltyTransactionCommandValidator() => RuleFor(command => command.TransactionId).NotEmpty();
}

/// <summary>Retries one queued earn/burn under its original idempotency key.</summary>
/// <param name="transactions">Transaction persistence.</param>
/// <param name="members">Member persistence.</param>
/// <param name="orbit">The loyalty engine boundary.</param>
/// <param name="clock">The only source of time.</param>
public sealed class RetryLoyaltyTransactionCommandHandler(
    ILoyaltyTransactionRepository transactions,
    ILoyaltyMemberRepository members,
    IOrbitClient orbit,
    IClock clock) : ICommandHandler<RetryLoyaltyTransactionCommand, RetryDisposition>
{
    /// <summary>How long a queued transaction retries before it is declared failed.</summary>
    public static readonly TimeSpan RetryHorizon = TimeSpan.FromHours(24);

    /// <inheritdoc />
    public async Task<RetryDisposition> HandleAsync(
        RetryLoyaltyTransactionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        LoyaltyTransaction transaction = await transactions
            .FindAsync(command.TransactionId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new LoyaltyTransactionNotFoundException();

        if (transaction.Status is not TransactionStatus.QueuedForRetry)
        {
            return RetryDisposition.Confirmed;
        }

        DateTimeOffset now = clock.UtcNow;
        if (transaction.OccurredAt + RetryHorizon <= now)
        {
            transaction.MarkFailed();
            return RetryDisposition.Expired;
        }

        LoyaltyMember? member = await members
            .FindByCustomerAsync(transaction.CustomerId, cancellationToken)
            .ConfigureAwait(false);

        if (member is null)
        {
            transaction.MarkFailed();
            return RetryDisposition.Refused;
        }

        try
        {
            if (transaction.TransactionType == TransactionType.Earn)
            {
                OrbitEarnResult applied = await orbit
                    .EarnAsync(
                        new OrbitEarnRequest(
                            member.OrbitMemberId, transaction.Amount, transaction.Currency,
                            transaction.Reference, transaction.IdempotencyKey),
                        cancellationToken)
                    .ConfigureAwait(false);

                transaction.MarkConfirmed(applied.OrbitTransactionId, applied.NewBalance);
                member.RecordSync(applied.NewBalance, applied.TierId, now);
                return RetryDisposition.Confirmed;
            }

            OrbitRedeemResult redeemed = await orbit
                .RedeemAsync(
                    new OrbitRedeemRequest(
                        member.OrbitMemberId, transaction.Amount, transaction.Reference,
                        transaction.IdempotencyKey),
                    cancellationToken)
                .ConfigureAwait(false);

            if (!redeemed.Applied)
            {
                transaction.MarkFailed();
                return RetryDisposition.Refused;
            }

            transaction.MarkConfirmed(redeemed.OrbitTransactionId, redeemed.NewBalance);
            member.RecordSync(redeemed.NewBalance, redeemed.TierId, now);
            return RetryDisposition.Confirmed;
        }
        catch (OrbitUnavailableException)
        {
            return RetryDisposition.StillQueued;
        }
    }
}
