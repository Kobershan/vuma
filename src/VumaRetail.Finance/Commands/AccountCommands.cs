using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Domain.Finance;

namespace VumaRetail.Finance.Commands;

/// <summary>Opens a new chart-of-accounts entry.</summary>
/// <param name="Code">The account code, unique per tenant.</param>
/// <param name="Name">The account's name.</param>
/// <param name="Type">Which of the five classes it belongs to.</param>
/// <param name="Currency">The ISO 4217 currency it is denominated in.</param>
/// <param name="ControlAccountType">What sub-ledger, if any, this reconciles to.</param>
/// <param name="ParentAccountId">The parent account, for a hierarchy.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record CreateAccountCommand(
    string Code,
    string Name,
    AccountType Type,
    string Currency,
    ControlAccountType ControlAccountType = ControlAccountType.None,
    Guid? ParentAccountId = null) : ICommand<Guid>;

/// <summary>Validates <see cref="CreateAccountCommand"/>.</summary>
public sealed class CreateAccountCommandValidator : AbstractValidator<CreateAccountCommand>
{
    /// <summary>Builds the rules.</summary>
    public CreateAccountCommandValidator()
    {
        RuleFor(command => command.Code).NotEmpty().MaximumLength(32);
        RuleFor(command => command.Name).NotEmpty().MaximumLength(128);
        RuleFor(command => command.Currency).NotEmpty().Length(3);
        RuleFor(command => command.Type).IsInEnum();
        RuleFor(command => command.ControlAccountType).IsInEnum();
    }
}

/// <summary>Handles <see cref="CreateAccountCommand"/>.</summary>
/// <param name="accounts">The chart of accounts.</param>
/// <param name="tenant">The ambient tenant.</param>
public sealed class CreateAccountCommandHandler(IAccountRepository accounts, ITenantContext tenant)
    : ICommandHandler<CreateAccountCommand, Guid>
{
    /// <inheritdoc />
    /// <exception cref="AccountCodeAlreadyInUseException">The code is already in use.</exception>
    public async Task<Guid> HandleAsync(CreateAccountCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await accounts.FindByCodeAsync(command.Code, cancellationToken).ConfigureAwait(false) is not null)
        {
            throw new AccountCodeAlreadyInUseException(command.Code);
        }

        Account account = Account.Open(
            tenant.TenantId,
            command.Code,
            command.Name,
            command.Type,
            command.Currency,
            command.ControlAccountType,
            command.ParentAccountId);

        accounts.Add(account);

        return account.Id;
    }
}
