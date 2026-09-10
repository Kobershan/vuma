#pragma warning disable CS1591
using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.CustomerAccounts;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Abstractions.Workflow;
using VumaRetail.Domain.CustomerAccounts;
using VumaRetail.Domain.Finance;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.CustomerAccounts.Commands.Accounts;

[CommandSideEffect(SideEffect.Write)]
public sealed record OpenCustomerAccountCommand(
    Guid PartnerId,
    decimal CreditLimitAmount,
    string CreditLimitCurrency,
    int TermsDays,
    Guid? CompanyId = null) : ICommand<Guid>;

public sealed class OpenCustomerAccountCommandValidator : AbstractValidator<OpenCustomerAccountCommand>
{
    public OpenCustomerAccountCommandValidator()
    {
        RuleFor(c => c.PartnerId).NotEmpty();
        RuleFor(c => c.CreditLimitAmount).GreaterThan(0m);
        RuleFor(c => c.CreditLimitCurrency).NotEmpty().Length(3);
        RuleFor(c => c.TermsDays).GreaterThan(0);
    }
}

public sealed class OpenCustomerAccountCommandHandler(
    ICustomerAccountRepository accounts,
    IDocumentNumberSequence numbers,
    ITenantContext tenant,
    ICompanyContext company)
    : ICommandHandler<OpenCustomerAccountCommand, Guid>
{
    public async Task<Guid> HandleAsync(OpenCustomerAccountCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        string number = await numbers.NextAsync("ACT", cancellationToken).ConfigureAwait(false);
        var account = CustomerAccount.Open(
            tenant.TenantId, tenant.StoreId, number, command.PartnerId,
            new Money(command.CreditLimitAmount, command.CreditLimitCurrency),
            command.TermsDays, command.CompanyId ?? company.RequireCompany());
        accounts.Add(account);
        return account.Id;
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record SetCreditLimitCommand(Guid AccountId, decimal Amount, string Currency) : ICommand;

public sealed class SetCreditLimitCommandValidator : AbstractValidator<SetCreditLimitCommand>
{
    public SetCreditLimitCommandValidator()
    {
        RuleFor(c => c.AccountId).NotEmpty();
        RuleFor(c => c.Amount).GreaterThan(0m);
        RuleFor(c => c.Currency).NotEmpty().Length(3);
    }
}

public sealed class SetCreditLimitCommandHandler(
    ICustomerAccountRepository accounts,
    IApprovalService approvals)
    : ICommandHandler<SetCreditLimitCommand, Unit>
{
    public async Task<Unit> HandleAsync(SetCreditLimitCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var account = await accounts.FindAsync(command.AccountId, cancellationToken).ConfigureAwait(false)
            ?? throw new CustomerAccountExceptions("ACCOUNT_NOT_FOUND", $"No account with id {command.AccountId}.");
        var outcome = await approvals.EvaluateAsync(
            new ApprovalContext("customer-accounts", "CustomerAccount", "SetLimit", command.AccountId,
                new Money(command.Amount, command.Currency)),
            cancellationToken).ConfigureAwait(false);
        if (!outcome.MayProceed)
        {
            throw CustomerAccountExceptions.ApprovalRequired();
        }

        account.SetLimit(new Money(command.Amount, command.Currency));
        return Unit.Value;
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record PlaceAccountHoldCommand(Guid AccountId, string Reason) : ICommand;

public sealed class PlaceAccountHoldCommandValidator : AbstractValidator<PlaceAccountHoldCommand>
{
    public PlaceAccountHoldCommandValidator()
    {
        RuleFor(c => c.AccountId).NotEmpty();
        RuleFor(c => c.Reason).NotEmpty().MaximumLength(256);
    }
}

public sealed class PlaceAccountHoldCommandHandler(
    ICustomerAccountRepository accounts,
    IApprovalService approvals)
    : ICommandHandler<PlaceAccountHoldCommand, Unit>
{
    public async Task<Unit> HandleAsync(PlaceAccountHoldCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var account = await accounts.FindAsync(command.AccountId, cancellationToken).ConfigureAwait(false)
            ?? throw new CustomerAccountExceptions("ACCOUNT_NOT_FOUND", $"No account with id {command.AccountId}.");
        var outcome = await approvals.EvaluateAsync(
            new ApprovalContext("customer-accounts", "CustomerAccount", "Hold", command.AccountId,
                Reason: command.Reason),
            cancellationToken).ConfigureAwait(false);
        if (!outcome.MayProceed)
        {
            throw CustomerAccountExceptions.ApprovalRequired();
        }

        account.Hold(command.Reason);
        return Unit.Value;
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record ReleaseAccountHoldCommand(Guid AccountId) : ICommand;

public sealed class ReleaseAccountHoldCommandValidator : AbstractValidator<ReleaseAccountHoldCommand>
{
    public ReleaseAccountHoldCommandValidator() => RuleFor(c => c.AccountId).NotEmpty();
}

public sealed class ReleaseAccountHoldCommandHandler(ICustomerAccountRepository accounts)
    : ICommandHandler<ReleaseAccountHoldCommand, Unit>
{
    public async Task<Unit> HandleAsync(ReleaseAccountHoldCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var account = await accounts.FindAsync(command.AccountId, cancellationToken).ConfigureAwait(false)
            ?? throw new CustomerAccountExceptions("ACCOUNT_NOT_FOUND", $"No account with id {command.AccountId}.");
        account.Release();
        return Unit.Value;
    }
}

public sealed record PaymentAllocationInput(Guid ArInvoiceId, decimal Amount);

[CommandSideEffect(SideEffect.Write)]
public sealed record RecordAccountPaymentCommand(
    Guid AccountId,
    decimal Amount,
    string Currency,
    string Channel,
    string ReceiptReference,
    IReadOnlyList<PaymentAllocationInput> Allocations) : ICommand<Guid>;

public sealed class RecordAccountPaymentCommandValidator : AbstractValidator<RecordAccountPaymentCommand>
{
    public RecordAccountPaymentCommandValidator()
    {
        RuleFor(c => c.AccountId).NotEmpty();
        RuleFor(c => c.Amount).GreaterThan(0m);
        RuleFor(c => c.Currency).NotEmpty().Length(3);
        RuleFor(c => c.Channel).NotEmpty().MaximumLength(64);
        RuleFor(c => c.ReceiptReference).NotEmpty().MaximumLength(64);
        RuleFor(c => c.Allocations).NotEmpty();
    }
}

public sealed class RecordAccountPaymentCommandHandler(
    ICustomerAccountRepository accounts,
    IArInvoiceRepository arInvoices,
    IArReceiptRepository receipts,
    IFinancialEventPoster events,
    IDocumentNumberSequence numbers,
    ITenantContext tenant,
    IClock clock)
    : ICommandHandler<RecordAccountPaymentCommand, Guid>
{
    public async Task<Guid> HandleAsync(RecordAccountPaymentCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var account = await accounts.FindAsync(command.AccountId, cancellationToken).ConfigureAwait(false)
            ?? throw new CustomerAccountExceptions("ACCOUNT_NOT_FOUND", $"No account with id {command.AccountId}.");
        var money = new Money(command.Amount, command.Currency);
        DateTimeOffset now = clock.UtcNow;

        var allocations = new List<(Guid? ArInvoiceId, Money Amount)>();
        foreach (var allocation in command.Allocations)
        {
            var invoice = await arInvoices.FindByIdAsync(allocation.ArInvoiceId, cancellationToken).ConfigureAwait(false)
                ?? throw new CustomerAccountExceptions("ACCOUNT_INVOICE_NOT_FOUND", $"No AR invoice with id {allocation.ArInvoiceId}.");
            var slice = new Money(allocation.Amount, command.Currency);
            invoice.Allocate(slice);
            allocations.Add((allocation.ArInvoiceId, slice));
        }

        string receiptNumber = await numbers.NextAsync("ARREC", cancellationToken).ConfigureAwait(false);
        Guid journalId = await events.PostAsync(
            new Events.AccountPaymentReceivedEvent(
                tenant.TenantId, tenant.StoreId, now, receiptNumber,
                new Dictionary<string, Money> { ["Principal"] = money }),
            cancellationToken).ConfigureAwait(false);
        var receipt = ArReceipt.Record(
            tenant.TenantId, tenant.StoreId, new PartnerId(account.PartnerId),
            receiptNumber, now, money, journalId, allocations);
        receipts.Add(receipt);
        return receipt.Id;
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record AuthoriseHolderCommand(
    Guid AccountId,
    Guid UserId,
    string DisplayName,
    decimal ChargeLimitAmount,
    string ChargeLimitCurrency) : ICommand<Guid>;

public sealed class AuthoriseHolderCommandValidator : AbstractValidator<AuthoriseHolderCommand>
{
    public AuthoriseHolderCommandValidator()
    {
        RuleFor(c => c.AccountId).NotEmpty();
        RuleFor(c => c.UserId).NotEmpty();
        RuleFor(c => c.DisplayName).NotEmpty().MaximumLength(128);
        RuleFor(c => c.ChargeLimitAmount).GreaterThan(0m);
        RuleFor(c => c.ChargeLimitCurrency).NotEmpty().Length(3);
    }
}

public sealed class AuthoriseHolderCommandHandler(
    ICustomerAccountRepository accounts,
    IAccountHolderRepository holders,
    ITenantContext tenant)
    : ICommandHandler<AuthoriseHolderCommand, Guid>
{
    public async Task<Guid> HandleAsync(AuthoriseHolderCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        _ = await accounts.FindAsync(command.AccountId, cancellationToken).ConfigureAwait(false)
            ?? throw new CustomerAccountExceptions("ACCOUNT_NOT_FOUND", $"No account with id {command.AccountId}.");
        var holder = AccountHolder.Authorise(
            tenant.TenantId, tenant.StoreId, command.AccountId, command.UserId,
            command.DisplayName, new Money(command.ChargeLimitAmount, command.ChargeLimitCurrency));
        holders.Add(holder);
        return holder.Id;
    }
}
