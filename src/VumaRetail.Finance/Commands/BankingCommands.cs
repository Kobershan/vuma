using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Domain.Finance;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Finance.Commands;

/// <summary>Opens a bank account record, paired with its GL control account.</summary>
/// <param name="GlAccountId">The GL account this reconciles to.</param>
/// <param name="Name">A label for the account.</param>
/// <param name="AccountNumber">The account number.</param>
/// <param name="Currency">The ISO 4217 currency it is held in.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record CreateBankAccountCommand(
    Guid GlAccountId, string Name, string AccountNumber, string Currency) : ICommand<Guid>;

/// <summary>Validates <see cref="CreateBankAccountCommand"/>.</summary>
public sealed class CreateBankAccountCommandValidator : AbstractValidator<CreateBankAccountCommand>
{
    /// <summary>Builds the rules.</summary>
    public CreateBankAccountCommandValidator()
    {
        RuleFor(command => command.GlAccountId).NotEmpty();
        RuleFor(command => command.Name).NotEmpty().MaximumLength(128);
        RuleFor(command => command.AccountNumber).NotEmpty().MaximumLength(64);
        RuleFor(command => command.Currency).NotEmpty().Length(3);
    }
}

/// <summary>Handles <see cref="CreateBankAccountCommand"/>.</summary>
/// <param name="bankAccounts">Bank accounts.</param>
/// <param name="glAccounts">The chart of accounts, to validate the GL account exists.</param>
/// <param name="tenant">The ambient tenant.</param>
public sealed class CreateBankAccountCommandHandler(
    IBankAccountRepository bankAccounts, IAccountRepository glAccounts, ITenantContext tenant)
    : ICommandHandler<CreateBankAccountCommand, Guid>
{
    /// <inheritdoc />
    /// <exception cref="AccountNotFoundException">The GL account does not exist.</exception>
    public async Task<Guid> HandleAsync(CreateBankAccountCommand command, CancellationToken cancellationToken = default)
    {
        if (await glAccounts.FindByIdAsync(command.GlAccountId, cancellationToken).ConfigureAwait(false) is null)
        {
            throw new AccountNotFoundException(command.GlAccountId);
        }

        BankAccount account = BankAccount.Open(
            tenant.TenantId, command.GlAccountId, command.Name, command.AccountNumber, command.Currency);

        bankAccounts.Add(account);

        return account.Id;
    }
}

/// <summary>One statement line to import.</summary>
/// <param name="TransactionDate">The date the bank posted the transaction.</param>
/// <param name="Description">The bank's own description.</param>
/// <param name="Amount">The signed amount: positive for money in, negative for money out.</param>
/// <param name="ExternalReference">The bank's own reference, for de-duplication.</param>
public sealed record BankStatementLineInput(
    DateOnly TransactionDate, string Description, decimal Amount, string ExternalReference);

/// <summary>
/// Imports a batch of bank statement lines.
/// </summary>
/// <param name="BankAccountId">The bank account the lines belong to.</param>
/// <param name="Lines">The lines to import.</param>
/// <remarks>
/// A batch command, not a file parser — turning a bank's export format into these lines is Stage
/// 11's Excel/CSV/PDF ingest pipeline job, feeding into this command once a column mapping exists.
/// A line whose <see cref="BankStatementLineInput.ExternalReference"/> has already been imported for
/// this account is skipped rather than rejected, so importing the same file twice is harmless.
/// </remarks>
[CommandSideEffect(SideEffect.Write)]
public sealed record ImportBankStatementLinesCommand(
    Guid BankAccountId, IReadOnlyList<BankStatementLineInput> Lines) : ICommand<int>;

/// <summary>Validates <see cref="ImportBankStatementLinesCommand"/>.</summary>
public sealed class ImportBankStatementLinesCommandValidator : AbstractValidator<ImportBankStatementLinesCommand>
{
    /// <summary>Builds the rules.</summary>
    public ImportBankStatementLinesCommandValidator()
    {
        RuleFor(command => command.BankAccountId).NotEmpty();
        RuleFor(command => command.Lines).NotEmpty();
        RuleForEach(command => command.Lines).ChildRules(line =>
            line.RuleFor(l => l.ExternalReference).NotEmpty());
    }
}

/// <summary>Handles <see cref="ImportBankStatementLinesCommand"/>.</summary>
/// <param name="bankAccounts">Bank accounts.</param>
/// <param name="lines">Imported statement lines.</param>
/// <param name="tenant">The ambient tenant.</param>
public sealed class ImportBankStatementLinesCommandHandler(
    IBankAccountRepository bankAccounts, IBankStatementLineRepository lines, ITenantContext tenant)
    : ICommandHandler<ImportBankStatementLinesCommand, int>
{
    /// <inheritdoc />
    /// <returns>How many lines were actually imported, excluding duplicates already on file.</returns>
    /// <exception cref="FinanceDocumentNotFoundException">The bank account does not exist.</exception>
    public async Task<int> HandleAsync(ImportBankStatementLinesCommand command, CancellationToken cancellationToken = default)
    {
        BankAccount account = await bankAccounts.FindByIdAsync(command.BankAccountId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new FinanceDocumentNotFoundException(nameof(BankAccount), command.BankAccountId);

        int imported = 0;

        foreach (BankStatementLineInput input in command.Lines)
        {
            if (await lines.ExistsAsync(command.BankAccountId, input.ExternalReference, cancellationToken)
                .ConfigureAwait(false))
            {
                continue;
            }

            lines.Add(BankStatementLine.Import(
                tenant.TenantId,
                command.BankAccountId,
                input.TransactionDate,
                input.Description,
                new Money(input.Amount, account.Currency),
                input.ExternalReference));

            imported++;
        }

        return imported;
    }
}

/// <summary>Matches an imported statement line against a GL journal line — the reconciliation act.</summary>
/// <param name="BankStatementLineId">The statement line.</param>
/// <param name="JournalLineId">The journal line it corresponds to.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record MatchBankStatementLineCommand(Guid BankStatementLineId, Guid JournalLineId) : ICommand;

/// <summary>Handles <see cref="MatchBankStatementLineCommand"/>.</summary>
/// <param name="lines">Imported statement lines.</param>
/// <param name="principal">Who is acting.</param>
/// <param name="clock">The only source of time.</param>
public sealed class MatchBankStatementLineCommandHandler(
    IBankStatementLineRepository lines, IPrincipalAccessor principal, IClock clock)
    : ICommandHandler<MatchBankStatementLineCommand, Unit>
{
    /// <inheritdoc />
    /// <exception cref="FinanceDocumentNotFoundException">The statement line does not exist.</exception>
    public async Task<Unit> HandleAsync(MatchBankStatementLineCommand command, CancellationToken cancellationToken = default)
    {
        BankStatementLine line = await lines.FindByIdAsync(command.BankStatementLineId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new FinanceDocumentNotFoundException(nameof(BankStatementLine), command.BankStatementLineId);

        line.Match(command.JournalLineId, principal.Principal, clock.UtcNow);

        return Unit.Value;
    }
}

/// <summary>Undoes a match, for example after it is found to be wrong.</summary>
/// <param name="BankStatementLineId">The statement line.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record UnmatchBankStatementLineCommand(Guid BankStatementLineId) : ICommand;

/// <summary>Handles <see cref="UnmatchBankStatementLineCommand"/>.</summary>
/// <param name="lines">Imported statement lines.</param>
public sealed class UnmatchBankStatementLineCommandHandler(IBankStatementLineRepository lines)
    : ICommandHandler<UnmatchBankStatementLineCommand, Unit>
{
    /// <inheritdoc />
    /// <exception cref="FinanceDocumentNotFoundException">The statement line does not exist.</exception>
    public async Task<Unit> HandleAsync(UnmatchBankStatementLineCommand command, CancellationToken cancellationToken = default)
    {
        BankStatementLine line = await lines.FindByIdAsync(command.BankStatementLineId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new FinanceDocumentNotFoundException(nameof(BankStatementLine), command.BankStatementLineId);

        line.Unmatch();

        return Unit.Value;
    }
}
