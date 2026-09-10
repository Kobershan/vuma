using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Crm;

namespace VumaRetail.Application.Crm.Commands;

/// <summary>Records affirmative consent for one purpose (POPIA).</summary>
/// <param name="CompanyId">The owning company.</param>
/// <param name="CustomerId">The customer.</param>
/// <param name="Type">The processing purpose.</param>
/// <param name="Source">What affirmative action captured it.</param>
/// <param name="ExpiresAt">Expiry, if the consent is time-boxed.</param>
/// <param name="StoreId">The owning store, if any.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record GiveConsentCommand(
    Guid CompanyId,
    Guid CustomerId,
    ConsentType Type,
    string Source,
    DateTimeOffset? ExpiresAt = null,
    Guid? StoreId = null) : ICommand;

/// <summary>Rejects a malformed consent capture.</summary>
public sealed class GiveConsentCommandValidator : AbstractValidator<GiveConsentCommand>
{
    /// <summary>Builds the rules.</summary>
    public GiveConsentCommandValidator()
    {
        RuleFor(command => command.CompanyId).NotEmpty();
        RuleFor(command => command.CustomerId).NotEmpty();
        RuleFor(command => command.Source).NotEmpty().MaximumLength(300);
    }
}

/// <summary>Captures consent: creates the row or re-gives on an existing one.</summary>
/// <param name="consents">Consent persistence.</param>
/// <param name="tenant">The ambient tenant.</param>
/// <param name="principal">Who captured it.</param>
/// <param name="clock">The only source of time.</param>
public sealed class GiveConsentCommandHandler(
    IConsentRepository consents,
    ITenantContext tenant,
    IPrincipalAccessor principal,
    IClock clock) : ICommandHandler<GiveConsentCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(GiveConsentCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Consent? existing = await consents
            .FindAsync(command.CustomerId, command.Type, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            var consent = new Consent(
                tenant.TenantId,
                command.CompanyId,
                command.CustomerId,
                command.Type,
                command.ExpiresAt,
                command.StoreId);
            consent.Give(clock.UtcNow, command.Source, principal.Principal);
            consents.Add(consent);
            return Unit.Value;
        }

        existing.Give(clock.UtcNow, command.Source, principal.Principal);
        return Unit.Value;
    }
}

/// <summary>Withdraws consent. Immediate — no grace period for marketing.</summary>
/// <param name="CompanyId">The owning company (used when no consent row exists yet).</param>
/// <param name="CustomerId">The customer.</param>
/// <param name="Type">The processing purpose.</param>
/// <param name="Reason">Why, if recorded.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record WithdrawConsentCommand(
    Guid CompanyId,
    Guid CustomerId,
    ConsentType Type,
    string? Reason = null)
    : ICommand;

/// <summary>Rejects a malformed withdrawal.</summary>
public sealed class WithdrawConsentCommandValidator : AbstractValidator<WithdrawConsentCommand>
{
    /// <summary>Builds the rules.</summary>
    public WithdrawConsentCommandValidator()
    {
        RuleFor(command => command.CompanyId).NotEmpty();
        RuleFor(command => command.CustomerId).NotEmpty();
        RuleFor(command => command.Reason).MaximumLength(1000);
    }
}

/// <summary>Withdraws consent. Withdrawing a never-given purpose still records the refusal.</summary>
/// <param name="consents">Consent persistence.</param>
/// <param name="tenant">The ambient tenant.</param>
/// <param name="principal">Who recorded it.</param>
/// <param name="clock">The only source of time.</param>
public sealed class WithdrawConsentCommandHandler(
    IConsentRepository consents,
    ITenantContext tenant,
    IPrincipalAccessor principal,
    IClock clock) : ICommandHandler<WithdrawConsentCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(WithdrawConsentCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Consent? existing = await consents
            .FindAsync(command.CustomerId, command.Type, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            var refused = new Consent(
                tenant.TenantId,
                command.CompanyId,
                command.CustomerId,
                command.Type);
            refused.Withdraw(clock.UtcNow, principal.Principal, command.Reason);
            consents.Add(refused);
            return Unit.Value;
        }

        existing.Withdraw(clock.UtcNow, principal.Principal, command.Reason);
        return Unit.Value;
    }
}
