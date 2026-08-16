using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Abstractions.Sales;
using VumaRetail.Application.Pos;
using VumaRetail.Domain.Pos;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Sales;

namespace VumaRetail.Application.Sales.Commands;

/// <summary>Raises a return against a completed sale.</summary>
/// <param name="SaleId">The sale the goods came off.</param>
/// <param name="Reason">Why they came back.</param>
/// <param name="RefundTenderType">How the refund is being given.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record CreateSalesReturnCommand(
    Guid SaleId,
    string Reason,
    TenderType RefundTenderType) : ICommand<Guid>;

/// <summary>Rejects a malformed create-return command before it reaches the handler.</summary>
public sealed class CreateSalesReturnCommandValidator : AbstractValidator<CreateSalesReturnCommand>
{
    /// <summary>Builds the rules.</summary>
    public CreateSalesReturnCommandValidator()
    {
        RuleFor(command => command.SaleId).NotEmpty();
        RuleFor(command => command.Reason).NotEmpty().MaximumLength(500);
        RuleFor(command => command.RefundTenderType).IsInEnum();
    }
}

/// <summary>Raises the return, drawing its number from ADR-065's <c>RTN</c> series.</summary>
/// <param name="returns">Return lookup and insertion.</param>
/// <param name="sales">The original sale — read, never written.</param>
/// <param name="numbers">ADR-065's gap-free document number sequence.</param>
/// <param name="principal">Who is authorising.</param>
/// <param name="clock">The only source of time.</param>
public sealed class CreateSalesReturnCommandHandler(
    ISalesReturnRepository returns,
    ISaleRepository sales,
    IDocumentNumberSequence numbers,
    IPrincipalAccessor principal,
    IClock clock) : ICommandHandler<CreateSalesReturnCommand, Guid>
{
    /// <summary>The document number series a return number is drawn from.</summary>
    public const string ReturnNumberSeries = "RTN";

    /// <inheritdoc />
    public async Task<Guid> HandleAsync(
        CreateSalesReturnCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Guid authorisedBy = SalesActor.RequireUserId(principal);

        Sale sale = await sales.FindAsync(command.SaleId, cancellationToken).ConfigureAwait(false)
            ?? throw new SalesNotFoundException("sale", command.SaleId);

        string returnNumber = await numbers
            .NextAsync(ReturnNumberSeries, cancellationToken)
            .ConfigureAwait(false);

        // Raise refuses anything but a completed sale (business rule 7). It reads the sale and writes
        // nothing to it: §7 rule 7 means the correction is this new document, and Stage 09's
        // ck_sale_lines_quantity_positive means a negative line was never an option anyway.
        SalesReturn created = SalesReturn.Raise(
            sale,
            returnNumber,
            command.Reason,
            command.RefundTenderType,
            authorisedBy,
            clock.UtcNow);

        returns.Add(created);

        return created.Id;
    }
}

/// <summary>Puts one of the original sale's lines onto a draft return.</summary>
/// <param name="SalesReturnId">The return.</param>
/// <param name="SaleLineId">The original line the goods came off.</param>
/// <param name="Quantity">How much is coming back.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record AddSalesReturnLineCommand(
    Guid SalesReturnId,
    Guid SaleLineId,
    Quantity Quantity) : ICommand<Guid>;

/// <summary>Rejects a malformed add-line command before it reaches the handler.</summary>
public sealed class AddSalesReturnLineCommandValidator : AbstractValidator<AddSalesReturnLineCommand>
{
    /// <summary>Builds the rules.</summary>
    public AddSalesReturnLineCommandValidator()
    {
        RuleFor(command => command.SalesReturnId).NotEmpty();
        RuleFor(command => command.SaleLineId).NotEmpty();
        RuleFor(command => command.Quantity.Value).GreaterThan(0m);
    }
}

/// <summary>
/// Adds the line, refunding what was actually charged and refusing anything beyond what was sold.
/// </summary>
/// <remarks>
/// The cumulative check (business rule 5) is the reason this handler exists rather than the aggregate
/// doing it alone: how much of a sale line has already come back is spread across every earlier return
/// document, which the aggregate cannot see. The sum is read here, inside the command's transaction,
/// and handed to <see cref="SalesReturn.AddLine"/>, which refuses and also snapshots it onto the row so
/// the database can re-assert the same arithmetic.
/// </remarks>
/// <param name="returns">Return lookup, and the cumulative returned quantity.</param>
/// <param name="sales">The original sale — read, never written.</param>
public sealed class AddSalesReturnLineCommandHandler(ISalesReturnRepository returns, ISaleRepository sales)
    : ICommandHandler<AddSalesReturnLineCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(
        AddSalesReturnLineCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        SalesReturn salesReturn = await returns
            .FindAsync(command.SalesReturnId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new SalesNotFoundException("sales return", command.SalesReturnId);

        salesReturn.EnsureDraft();

        Sale sale = await sales.FindAsync(salesReturn.SaleId, cancellationToken).ConfigureAwait(false)
            ?? throw new SalesNotFoundException("sale", salesReturn.SaleId);

        SaleLine soldLine = sale.RequireLine(command.SaleLineId);

        if (soldLine.IsVoided)
        {
            // A voided line was taken off the sale before the customer paid, so nothing on it was ever
            // sold and nothing on it can come back.
            throw SalesRuleException.ReturnExceedsQuantitySold(0m, 0m, command.Quantity.Value);
        }

        decimal alreadyReturned = await returns
            .SumReturnedQuantityAsync(soldLine.Id, salesReturn.Id, cancellationToken)
            .ConfigureAwait(false);

        SalesReturnLine created = salesReturn.AddLine(soldLine, command.Quantity, alreadyReturned);

        return created.Id;
    }
}

/// <summary>What a completed return hands back to the counter.</summary>
/// <param name="SalesReturnId">The return.</param>
/// <param name="ReturnNumber">The credit slip number.</param>
/// <param name="Net">The refund excluding tax.</param>
/// <param name="Tax">The tax coming back.</param>
/// <param name="Gross">What to hand the customer.</param>
/// <param name="RefundTenderType">How the refund is given.</param>
/// <param name="StockReturnsRefused">
/// How many lines completed without putting stock back (ADR-073). Zero on a normal return; anything
/// else is a reconciliation the store owes itself.
/// </param>
public sealed record SalesReturnCompletionResult(
    Guid SalesReturnId,
    string ReturnNumber,
    Money Net,
    Money Tax,
    Money Gross,
    TenderType RefundTenderType,
    int StockReturnsRefused);

/// <summary>Closes the return: freezes it, puts stock back and raises the financial event.</summary>
/// <param name="SalesReturnId">The return.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record CompleteSalesReturnCommand(Guid SalesReturnId) : ICommand<SalesReturnCompletionResult>;

/// <summary>Rejects a malformed complete command before it reaches the handler.</summary>
public sealed class CompleteSalesReturnCommandValidator : AbstractValidator<CompleteSalesReturnCommand>
{
    /// <summary>Builds the rules.</summary>
    public CompleteSalesReturnCommandValidator() => RuleFor(command => command.SalesReturnId).NotEmpty();
}

/// <summary>Delegates to <see cref="ISalesReturnCompletionService"/> and reports what happened.</summary>
/// <param name="returns">Return lookup.</param>
/// <param name="completion">The three steps completion always takes, in one place.</param>
public sealed class CompleteSalesReturnCommandHandler(
    ISalesReturnRepository returns, ISalesReturnCompletionService completion)
    : ICommandHandler<CompleteSalesReturnCommand, SalesReturnCompletionResult>
{
    /// <inheritdoc />
    public async Task<SalesReturnCompletionResult> HandleAsync(
        CompleteSalesReturnCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        SalesReturn salesReturn = await returns
            .FindAsync(command.SalesReturnId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new SalesNotFoundException("sales return", command.SalesReturnId);

        await completion.CompleteAsync(salesReturn, cancellationToken).ConfigureAwait(false);

        return new SalesReturnCompletionResult(
            salesReturn.Id,
            salesReturn.ReturnNumber,
            salesReturn.Net,
            salesReturn.Tax,
            salesReturn.Gross,
            salesReturn.RefundTenderType,
            salesReturn.Lines.Count(line => line.StockReturn is StockReturnStatus.Refused));
    }
}

/// <summary>Abandons a draft return. Nothing was refunded and nothing moved.</summary>
/// <param name="SalesReturnId">The return.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record CancelSalesReturnCommand(Guid SalesReturnId) : ICommand;

/// <summary>Rejects a malformed cancel command before it reaches the handler.</summary>
public sealed class CancelSalesReturnCommandValidator : AbstractValidator<CancelSalesReturnCommand>
{
    /// <summary>Builds the rules.</summary>
    public CancelSalesReturnCommandValidator() => RuleFor(command => command.SalesReturnId).NotEmpty();
}

/// <summary>Cancels the return.</summary>
/// <param name="returns">Return lookup.</param>
/// <param name="clock">The only source of time.</param>
public sealed class CancelSalesReturnCommandHandler(ISalesReturnRepository returns, IClock clock)
    : ICommandHandler<CancelSalesReturnCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(
        CancelSalesReturnCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        SalesReturn salesReturn = await returns
            .FindAsync(command.SalesReturnId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new SalesNotFoundException("sales return", command.SalesReturnId);

        salesReturn.Cancel(clock.UtcNow);

        return Unit.Value;
    }
}
