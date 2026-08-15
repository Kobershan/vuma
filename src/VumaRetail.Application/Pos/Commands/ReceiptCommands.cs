using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Pos;

namespace VumaRetail.Application.Pos.Commands;

/// <summary>
/// Records that a sale's receipt came out of a printer.
/// </summary>
/// <remarks>
/// Whether this is a reprint is <b>not</b> a field on the command: it is derived from how many prints
/// the log already holds. A caller who could declare "this is the first print" could reprint a receipt
/// forever without any of them being marked, which would make the log worse than useless — it would
/// look complete.
/// </remarks>
/// <param name="SaleId">The sale whose receipt was printed.</param>
/// <param name="Reason">Why it was reprinted. Required once the receipt has been printed before.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record RecordReceiptPrintCommand(Guid SaleId, string? Reason = null) : ICommand<Guid>;

/// <summary>Rejects a malformed record-print command before it reaches the handler.</summary>
public sealed class RecordReceiptPrintCommandValidator : AbstractValidator<RecordReceiptPrintCommand>
{
    /// <summary>Builds the rules.</summary>
    public RecordReceiptPrintCommandValidator()
    {
        RuleFor(command => command.SaleId).NotEmpty();
        RuleFor(command => command.Reason).MaximumLength(500);
    }
}

/// <summary>Appends to the print log, deriving reprint status from the log itself.</summary>
/// <param name="sales">Sale lookup.</param>
/// <param name="prints">The append-only print log.</param>
/// <param name="principal">Who printed it, and from which terminal.</param>
/// <param name="clock">The only source of time.</param>
public sealed class RecordReceiptPrintCommandHandler(
    ISaleRepository sales,
    IReceiptPrintRepository prints,
    IPrincipalAccessor principal,
    IClock clock) : ICommandHandler<RecordReceiptPrintCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(
        RecordReceiptPrintCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Sale sale = await sales.FindAsync(command.SaleId, cancellationToken).ConfigureAwait(false)
            ?? throw new PosNotFoundException("sale", command.SaleId);

        int alreadyPrinted = await prints.CountForSaleAsync(sale.Id, cancellationToken).ConfigureAwait(false);

        ReceiptPrint print = ReceiptPrint.Record(
            sale.TenantId,
            sale.StoreId,
            sale.Id,
            PosActor.RequireUserId(principal),
            principal.TerminalId ?? sale.TerminalId,
            isReprint: alreadyPrinted > 0,
            command.Reason,
            clock.UtcNow);

        prints.Add(print);

        return print.Id;
    }
}
