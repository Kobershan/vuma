using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Loyalty;

namespace VumaRetail.Application.Loyalty.Commands;

/// <summary>Enrolls a customer as a loyalty member.</summary>
/// <param name="CompanyId">The owning company.</param>
/// <param name="CustomerId">The Stage 06 identity.</param>
/// <param name="StoreId">The store of enrolment, if any.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record EnrollMemberCommand(Guid CompanyId, Guid CustomerId, Guid? StoreId = null)
    : ICommand<Guid>;

/// <summary>Rejects a malformed enrolment.</summary>
public sealed class EnrollMemberCommandValidator : AbstractValidator<EnrollMemberCommand>
{
    /// <summary>Builds the rules.</summary>
    public EnrollMemberCommandValidator()
    {
        RuleFor(command => command.CompanyId).NotEmpty();
        RuleFor(command => command.CustomerId).NotEmpty();
    }
}

/// <summary>Enrolls a member, creating the Orbit identity idempotently.</summary>
/// <param name="members">Member persistence.</param>
/// <param name="orbit">The loyalty engine boundary.</param>
/// <param name="tenant">The ambient tenant.</param>
/// <param name="clock">The only source of time.</param>
public sealed class EnrollMemberCommandHandler(
    ILoyaltyMemberRepository members,
    IOrbitClient orbit,
    ITenantContext tenant,
    IClock clock) : ICommandHandler<EnrollMemberCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(EnrollMemberCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        LoyaltyMember? existing = await members
            .FindByCustomerAsync(command.CustomerId, cancellationToken)
            .ConfigureAwait(false);

        // Per-company enrolment: the same customer may hold a membership in each company, but
        // never two in one.
        if (existing is not null && existing.CompanyId == command.CompanyId)
        {
            throw new LoyaltyMemberAlreadyEnrolledException();
        }

        string orbitMemberId;
        if (existing is not null)
        {
            orbitMemberId = existing.OrbitMemberId;
        }
        else
        {
            // Idempotent at Orbit: replays with the same customer return the same member id, so a
            // retried enrolment never creates a duplicate ledger identity.
            orbitMemberId = await orbit
                .EnsureMemberAsync(command.CustomerId, cancellationToken)
                .ConfigureAwait(false);
        }

        var member = new LoyaltyMember(
            tenant.TenantId,
            command.CompanyId,
            command.CustomerId,
            orbitMemberId,
            clock.UtcNow,
            command.StoreId);

        members.Add(member);
        return member.Id;
    }
}

/// <summary>Configures the loyalty programme for a company.</summary>
/// <param name="CompanyId">The company.</param>
/// <param name="Currency">ISO 4217 code earn amounts are quoted in.</param>
/// <param name="EarnRate">Base points per currency unit.</param>
/// <param name="PointExpiryDays">Days before points expire.</param>
/// <param name="Enabled">Whether members can earn and burn.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record ConfigureLoyaltyCommand(
    Guid CompanyId,
    string Currency,
    decimal EarnRate,
    int PointExpiryDays,
    bool Enabled) : ICommand;

/// <summary>Rejects malformed settings.</summary>
public sealed class ConfigureLoyaltyCommandValidator : AbstractValidator<ConfigureLoyaltyCommand>
{
    /// <summary>Builds the rules.</summary>
    public ConfigureLoyaltyCommandValidator()
    {
        RuleFor(command => command.CompanyId).NotEmpty();
        RuleFor(command => command.Currency).NotEmpty().Length(3);
        RuleFor(command => command.EarnRate).GreaterThan(0m);
        RuleFor(command => command.PointExpiryDays).GreaterThan(0);
    }
}

/// <summary>Creates or reconfigures a company's loyalty settings.</summary>
/// <param name="settings">Settings persistence.</param>
/// <param name="tenant">The ambient tenant.</param>
public sealed class ConfigureLoyaltyCommandHandler(
    ILoyaltySettingsRepository settings,
    ITenantContext tenant) : ICommandHandler<ConfigureLoyaltyCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(ConfigureLoyaltyCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        LoyaltySettings? existing = await settings
            .FindAsync(command.CompanyId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            var created = new LoyaltySettings(tenant.TenantId, command.CompanyId, command.Currency);
            if (command.Enabled)
            {
                created.Enable(command.EarnRate, command.PointExpiryDays);
            }

            settings.Add(created);
            return Unit.Value;
        }

        if (command.Enabled)
        {
            existing.Enable(command.EarnRate, command.PointExpiryDays);
        }
        else
        {
            existing.Disable();
        }

        return Unit.Value;
    }
}

/// <summary>Refreshes the cached tier catalogue from Orbit (webhook/sync path).</summary>
/// <param name="CompanyId">The company.</param>
/// <param name="Tiers">Orbit's current tier definitions.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record SyncTierCacheCommand(Guid CompanyId, IReadOnlyList<TierDefinition> Tiers)
    : ICommand<int>;

/// <summary>Rejects a malformed tier sync.</summary>
public sealed class SyncTierCacheCommandValidator : AbstractValidator<SyncTierCacheCommand>
{
    /// <summary>Builds the rules.</summary>
    public SyncTierCacheCommandValidator() => RuleFor(command => command.CompanyId).NotEmpty();
}

/// <summary>Upserts cached tiers.</summary>
/// <param name="writer">The shared cache write path.</param>
public sealed class SyncTierCacheCommandHandler(LoyaltyCacheWriter writer)
    : ICommandHandler<SyncTierCacheCommand, int>
{
    /// <inheritdoc />
    public Task<int> HandleAsync(SyncTierCacheCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return writer.UpsertTiersAsync(command.CompanyId, command.Tiers, cancellationToken);
    }
}

/// <summary>Refreshes the cached rewards catalogue from Orbit (webhook/sync path).</summary>
/// <param name="CompanyId">The company.</param>
/// <param name="Rewards">Orbit's current reward definitions.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record SyncRewardCacheCommand(Guid CompanyId, IReadOnlyList<RewardDefinition> Rewards)
    : ICommand<int>;

/// <summary>Rejects a malformed reward sync.</summary>
public sealed class SyncRewardCacheCommandValidator : AbstractValidator<SyncRewardCacheCommand>
{
    /// <summary>Builds the rules.</summary>
    public SyncRewardCacheCommandValidator() => RuleFor(command => command.CompanyId).NotEmpty();
}

/// <summary>Upserts cached rewards.</summary>
/// <param name="writer">The shared cache write path.</param>
public sealed class SyncRewardCacheCommandHandler(LoyaltyCacheWriter writer)
    : ICommandHandler<SyncRewardCacheCommand, int>
{
    /// <inheritdoc />
    public Task<int> HandleAsync(SyncRewardCacheCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return writer.UpsertRewardsAsync(command.CompanyId, command.Rewards, cancellationToken);
    }
}

/// <summary>What a catalogue sync pulled.</summary>
/// <param name="Tiers">Tier rows touched.</param>
/// <param name="Rewards">Reward rows touched.</param>
public sealed record CatalogueSyncOutcome(int Tiers, int Rewards);

/// <summary>Pulls Orbit's current tiers and rewards into the local cache.</summary>
/// <param name="CompanyId">The company.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record SyncCatalogueCommand(Guid CompanyId) : ICommand<CatalogueSyncOutcome>;

/// <summary>Rejects a malformed catalogue sync.</summary>
public sealed class SyncCatalogueCommandValidator : AbstractValidator<SyncCatalogueCommand>
{
    /// <summary>Builds the rules.</summary>
    public SyncCatalogueCommandValidator() => RuleFor(command => command.CompanyId).NotEmpty();
}

/// <summary>Pulls and caches the catalogue. Lets an Orbit outage propagate — the caller decides
/// whether a stale catalogue is acceptable (the till renders it regardless).</summary>
/// <param name="orbit">The loyalty engine boundary.</param>
/// <param name="writer">The shared cache write path.</param>
public sealed class SyncCatalogueCommandHandler(IOrbitClient orbit, LoyaltyCacheWriter writer)
    : ICommandHandler<SyncCatalogueCommand, CatalogueSyncOutcome>
{
    /// <inheritdoc />
    public async Task<CatalogueSyncOutcome> HandleAsync(
        SyncCatalogueCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        IReadOnlyList<TierDefinition> tiers = await orbit
            .ListTiersAsync(cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<RewardDefinition> rewards = await orbit
            .ListRewardsAsync(cancellationToken)
            .ConfigureAwait(false);

        int tierRows = await writer
            .UpsertTiersAsync(command.CompanyId, tiers, cancellationToken)
            .ConfigureAwait(false);

        int rewardRows = await writer
            .UpsertRewardsAsync(command.CompanyId, rewards, cancellationToken)
            .ConfigureAwait(false);

        return new CatalogueSyncOutcome(tierRows, rewardRows);
    }
}

/// <summary>Applies an Orbit-originated webhook: balance/tier change for a member.</summary>
/// <param name="CompanyId">The company.</param>
/// <param name="OrbitMemberId">Orbit's member id.</param>
/// <param name="Balance">Reported balance, if carried.</param>
/// <param name="TierId">Reported tier, if carried.</param>
/// <param name="EventType">Orbit's event name (tier.changed, balance.adjusted, ...).</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record ProcessLoyaltyWebhookCommand(
    Guid CompanyId,
    string OrbitMemberId,
    decimal? Balance,
    string? TierId,
    string EventType) : ICommand;

/// <summary>Rejects a malformed webhook.</summary>
public sealed class ProcessLoyaltyWebhookCommandValidator : AbstractValidator<ProcessLoyaltyWebhookCommand>
{
    /// <summary>Builds the rules.</summary>
    public ProcessLoyaltyWebhookCommandValidator()
    {
        RuleFor(command => command.CompanyId).NotEmpty();
        RuleFor(command => command.OrbitMemberId).NotEmpty().MaximumLength(200);
        RuleFor(command => command.EventType).NotEmpty().MaximumLength(100);
    }
}

/// <summary>
/// Applies the webhook to the local cache. Unknown members are ignored (enrolment owns
/// identity) — a webhook must never conjure a member. Vuma re-emits nothing itself: Stages 22
/// and 29 read the same rows through queries and the metering rollup.
/// </summary>
/// <param name="members">Member persistence.</param>
/// <param name="clock">The only source of time.</param>
public sealed class ProcessLoyaltyWebhookCommandHandler(
    ILoyaltyMemberRepository members,
    IClock clock) : ICommandHandler<ProcessLoyaltyWebhookCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(
        ProcessLoyaltyWebhookCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        LoyaltyMember? member = await members
            .FindByOrbitIdAsync(command.OrbitMemberId, cancellationToken)
            .ConfigureAwait(false);

        if (member is null)
        {
            return Unit.Value;
        }

        member.RecordSync(
            command.Balance ?? member.BalanceCache,
            command.TierId ?? member.TierId,
            clock.UtcNow);

        return Unit.Value;
    }
}
