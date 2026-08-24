using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Identity;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Pos.Permissions;
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
/// <param name="ReceiptPrintId">
/// The print row's identity, or <c>null</c> to mint one here. Without it, a replay of this command
/// after a dropped acknowledgement appends a second row marked <c>isReprint: true</c> — a fabricated
/// loss-prevention signal against a cashier who only printed once (§4.11).
/// </param>
/// <remarks>
/// §4.10, ADR-135: exempt from read-only (<see cref="ReadOnlyExemption.ReceiptReprint"/>) on the
/// argument that this command cannot originate trade — it appends one row to an append-only log for a
/// sale that already exists, and cannot create a sale, move money, move stock, raise a financial event
/// or consume a document number. Bounded in the handler: refused while the referenced sale is still
/// <see cref="SaleStatus.Open"/>, which is the in-flight case and belongs to the session-scoped
/// mechanism instead, not to this exemption.
/// </remarks>
[CommandSideEffect(SideEffect.Write, Exemption = ReadOnlyExemption.ReceiptReprint)]
public sealed record RecordReceiptPrintCommand(
    Guid SaleId, string? Reason = null, Guid? ReceiptPrintId = null) : ICommand<Guid>;

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
/// <param name="permissions">
/// The in-handler check §4.15 needs: whether *this* print is a reprint is only known after the log is
/// read, so an endpoint filter — which runs before the handler — cannot enforce it.
/// </param>
/// <param name="principal">Who printed it, and from which terminal.</param>
/// <param name="clock">The only source of time.</param>
/// <param name="entitlements">
/// §4.10: this command is exempt from the pipeline's read-only guard, so this is the one place left
/// that can still ask whether the tenant is read-only — the sanctioned use of
/// <see cref="IEnforcementStatusReader"/> outside the guard itself (ADR-054).
/// </param>
public sealed class RecordReceiptPrintCommandHandler(
    ISaleRepository sales,
    IReceiptPrintRepository prints,
    IPermissionChecker permissions,
    IPrincipalAccessor principal,
    IClock clock,
    IEnforcementStatusReader entitlements) : ICommandHandler<RecordReceiptPrintCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(
        RecordReceiptPrintCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Sale sale = await sales.FindAsync(command.SaleId, cancellationToken).ConfigureAwait(false)
            ?? throw new PosNotFoundException("sale", command.SaleId);

        if (command.ReceiptPrintId is { } replayedPrintId)
        {
            IReadOnlyList<ReceiptPrint> existing = await prints
                .ListForSaleAsync(sale.Id, cancellationToken)
                .ConfigureAwait(false);

            ReceiptPrint? already = existing.FirstOrDefault(print => print.Id == replayedPrintId);

            if (already is not null)
            {
                // The idempotent replay (§4.11) — a lost acknowledgement must not fabricate a second,
                // reprint-flagged row against a cashier who printed exactly once.
                return already.Id;
            }
        }

        int alreadyPrinted = await prints.CountForSaleAsync(sale.Id, cancellationToken).ConfigureAwait(false);

        Guid operatorUserId = PosActor.RequireUserId(principal);

        // §4.15: pos.receipt.print is granted to every cashier; pos.receipt.reprint is the high-risk
        // permission that gates the second and every later print of the same sale. The endpoint's
        // RequirePermission only ever saw pos.receipt.print, because "is this a reprint" is derived
        // from the log this handler is about to read — so the check belongs here, not at the route.
        if (alreadyPrinted > 0
            && !await permissions
                .HasPermissionAsync(operatorUserId, sale.StoreId, PosPermissions.ReceiptReprint, cancellationToken)
                .ConfigureAwait(false))
        {
            throw PosForbiddenException.ReceiptReprintNotPermitted();
        }

        // §4.10: the exemption above is unconditional at the guard, so the "cannot originate trade"
        // argument only holds if this handler enforces its own bound — a first print of a sale that is
        // still Open is the in-flight case, not the reprint case, and does not get the free pass.
        if (sale.Status is SaleStatus.Open)
        {
            EnforcementDecision decision = await entitlements.CurrentLevel(cancellationToken).ConfigureAwait(false);

            if (decision.IsReadOnly)
            {
                throw new LicenceReadOnlyException(decision);
            }
        }

        ReceiptPrint print = ReceiptPrint.Record(
            sale.TenantId,
            sale.StoreId,
            sale.Id,
            operatorUserId,
            principal.TerminalId ?? sale.TerminalId,
            isReprint: alreadyPrinted > 0,
            command.Reason,
            clock.UtcNow,
            command.ReceiptPrintId);

        prints.Add(print);

        return print.Id;
    }
}
