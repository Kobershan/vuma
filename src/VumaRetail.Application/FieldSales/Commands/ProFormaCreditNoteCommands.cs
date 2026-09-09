using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.FieldSales;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Abstractions.Workflow;
using VumaRetail.Domain.FieldSales;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Workflow;

#pragma warning disable CS1591
#pragma warning disable CA1062

namespace VumaRetail.Application.FieldSales.Commands;

/// <summary>One proposed credit line against one original invoice line.</summary>
public sealed record ProFormaCreditLineInput(
    Guid OriginalInvoiceLineId,
    Guid? ItemId,
    Guid? ItemVariantId,
    decimal QuantityValue,
    string QuantityUom,
    decimal UnitPriceAmount,
    decimal TaxAmount,
    decimal NetAmount,
    string Currency);

/// <summary>Captures a pro forma credit note. Replays by idempotency key.</summary>
[CommandSideEffect(SideEffect.Write)]
public sealed record CaptureProFormaCreditNoteCommand(
    Guid RepId,
    Guid CompanyId,
    Guid OriginalInvoiceId,
    string OriginalInvoiceNumber,
    string ReasonCode,
    string Reason,
    string Currency,
    string IdempotencyKey,
    IReadOnlyList<ProFormaCreditLineInput> Lines) : ICommand<Guid>;

/// <summary>Validates <see cref="CaptureProFormaCreditNoteCommand"/>.</summary>
public sealed class CaptureProFormaCreditNoteCommandValidator : AbstractValidator<CaptureProFormaCreditNoteCommand>
{
    public CaptureProFormaCreditNoteCommandValidator()
    {
        RuleFor(c => c.RepId).NotEmpty();
        RuleFor(c => c.CompanyId).NotEmpty();
        RuleFor(c => c.OriginalInvoiceId).NotEmpty();
        RuleFor(c => c.OriginalInvoiceNumber).NotEmpty();
        RuleFor(c => c.ReasonCode).NotEmpty();
        RuleFor(c => c.Reason).NotEmpty();
        RuleFor(c => c.Currency).NotEmpty().Length(3);
        RuleFor(c => c.IdempotencyKey).NotEmpty();
        RuleFor(c => c.Lines).NotEmpty();
    }
}

/// <summary>Handler for <see cref="CaptureProFormaCreditNoteCommand"/>.</summary>
[CommandSideEffect(SideEffect.Write)]
public sealed class CaptureProFormaCreditNoteCommandHandler(
    IProFormaCreditNoteRepository credits,
    IRepRepository reps,
    IDocumentNumberSequence numbers,
    ITenantContext tenant,
    IClock clock)
    : ICommandHandler<CaptureProFormaCreditNoteCommand, Guid>
{
    public async Task<Guid> HandleAsync(CaptureProFormaCreditNoteCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        ProFormaCreditNote? existing = await credits
            .FindByIdempotencyKeyAsync(command.IdempotencyKey.Trim(), cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return existing.Id;
        }

        Rep rep = await reps.FindAsync(command.RepId, cancellationToken).ConfigureAwait(false)
            ?? throw FieldSalesException.Forbidden("capture for an unknown rep");

        if (!rep.MaySellFor(command.CompanyId))
        {
            throw FieldSalesException.Forbidden($"credit for company {command.CompanyId}");
        }

        if (rep.TenantId != tenant.TenantId)
        {
            throw FieldSalesException.Forbidden("capture across tenants");
        }

        string currency = command.Currency.Trim().ToUpperInvariant();
        string number = await numbers.NextAsync("PFC", cancellationToken).ConfigureAwait(false);
        ProFormaCreditNote note = ProFormaCreditNote.Capture(
            tenant.TenantId, tenant.StoreId, number, rep.Id, command.CompanyId,
            command.OriginalInvoiceId, command.OriginalInvoiceNumber,
            command.ReasonCode, command.Reason, currency,
            command.IdempotencyKey, clock.UtcNow);

        foreach (ProFormaCreditLineInput input in command.Lines)
        {
            string lineCurrency = input.Currency.Trim().ToUpperInvariant();
            if (!string.Equals(lineCurrency, currency, StringComparison.Ordinal))
            {
                throw new FieldSalesException(
                    "PROFORMA_CURRENCY_MISMATCH",
                    $"Line currency {lineCurrency} does not match document currency {currency}.");
            }

            note.AddLine(
                input.OriginalInvoiceLineId, input.ItemId, input.ItemVariantId,
                input.QuantityValue, input.QuantityUom.Trim(),
                new Money(input.UnitPriceAmount, currency),
                new Money(input.TaxAmount, currency),
                new Money(input.NetAmount, currency));
        }

        credits.Add(note);
        return note.Id;
    }
}

/// <summary>Submits a credit proposal for approval.</summary>
[CommandSideEffect(SideEffect.Write)]
public sealed record SubmitProFormaCreditNoteCommand(Guid CreditNoteId) : ICommand;

/// <summary>Validates <see cref="SubmitProFormaCreditNoteCommand"/>.</summary>
public sealed class SubmitProFormaCreditNoteCommandValidator : AbstractValidator<SubmitProFormaCreditNoteCommand>
{
    public SubmitProFormaCreditNoteCommandValidator()
    {
        RuleFor(c => c.CreditNoteId).NotEmpty();
    }
}

/// <summary>Handler for <see cref="SubmitProFormaCreditNoteCommand"/>.</summary>
[CommandSideEffect(SideEffect.Write)]
public sealed class SubmitProFormaCreditNoteCommandHandler(
    IProFormaCreditNoteRepository credits,
    IApprovalService approvals,
    ITenantContext tenant,
    IClock clock)
    : ICommandHandler<SubmitProFormaCreditNoteCommand, Unit>
{
    public async Task<Unit> HandleAsync(SubmitProFormaCreditNoteCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        ProFormaCreditNote note = await credits.FindAsync(command.CreditNoteId, cancellationToken).ConfigureAwait(false)
            ?? throw new FieldSalesException("PROFORMA_NOT_FOUND", $"No credit proposal {command.CreditNoteId}.");

        if (note.TenantId != tenant.TenantId)
        {
            throw FieldSalesException.Forbidden("submit across tenants");
        }

        if (note.IsExpired(clock.UtcNow))
        {
            note.Expire();
            throw FieldSalesException.Expired(note.CreditNoteNumber);
        }

        note.Submit(clock.UtcNow);

        ApprovalOutcome outcome = await approvals.EvaluateAsync(
                new ApprovalContext("field-sales", "ProFormaCreditNote", "Approve", note.Id, note.Gross),
                cancellationToken)
            .ConfigureAwait(false);

        if (outcome.RequestId is { } requestId)
        {
            note.RecordApproval(requestId);
        }

        return Unit.Value;
    }
}

/// <summary>Approves a credit proposal: decides in Stage 05, then applies the sales return.</summary>
[CommandSideEffect(SideEffect.Write)]
public sealed record ApproveProFormaCreditNoteCommand(Guid CreditNoteId, string Comment) : ICommand<Guid>;

/// <summary>Validates <see cref="ApproveProFormaCreditNoteCommand"/>.</summary>
public sealed class ApproveProFormaCreditNoteCommandValidator : AbstractValidator<ApproveProFormaCreditNoteCommand>
{
    public ApproveProFormaCreditNoteCommandValidator()
    {
        RuleFor(c => c.CreditNoteId).NotEmpty();
    }
}

/// <summary>Handler for <see cref="ApproveProFormaCreditNoteCommand"/>.</summary>
[CommandSideEffect(SideEffect.Write)]
public sealed class ApproveProFormaCreditNoteCommandHandler(
    IProFormaCreditNoteRepository credits,
    IApprovalService approvals,
    IFieldSalesApprovalService conversion,
    IPrincipalAccessor principal,
    ITenantContext tenant,
    IClock clock)
    : ICommandHandler<ApproveProFormaCreditNoteCommand, Guid>
{
    public async Task<Guid> HandleAsync(ApproveProFormaCreditNoteCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        ProFormaCreditNote note = await credits.FindAsync(command.CreditNoteId, cancellationToken).ConfigureAwait(false)
            ?? throw new FieldSalesException("PROFORMA_NOT_FOUND", $"No credit proposal {command.CreditNoteId}.");

        if (note.TenantId != tenant.TenantId)
        {
            throw FieldSalesException.Forbidden("approve across tenants");
        }

        if (note.IsExpired(clock.UtcNow))
        {
            note.Expire();
            throw FieldSalesException.Expired(note.CreditNoteNumber);
        }

        if (note.Status is ProFormaStatus.Converted && note.ResultingReturnId is { } applied)
        {
            return applied;
        }

        if (note.Status is not ProFormaStatus.Submitted)
        {
            throw FieldSalesException.IllegalTransition(note.Status, "be approved");
        }

        if (note.ApprovalRequestId is { } requestId)
        {
            ApprovalDecisionResult decision = await approvals.DecideAsync(
                    requestId, ApprovalDecisionOutcome.Approved, command.Comment, cancellationToken)
                .ConfigureAwait(false);

            if (decision.Status is not ApprovalRequestStatus.Approved)
            {
                throw new FieldSalesException(
                    "PROFORMA_NOT_APPROVED",
                    $"Approval request {requestId} decided {decision.Status}; the return is not applied.");
            }
        }

        return await conversion.ApproveCreditNoteAsync(
                note.Id, principal.Principal, cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>Rejects a credit proposal with a reason.</summary>
[CommandSideEffect(SideEffect.Write)]
public sealed record RejectProFormaCreditNoteCommand(Guid CreditNoteId, string Reason) : ICommand;

/// <summary>Validates <see cref="RejectProFormaCreditNoteCommand"/>.</summary>
public sealed class RejectProFormaCreditNoteCommandValidator : AbstractValidator<RejectProFormaCreditNoteCommand>
{
    public RejectProFormaCreditNoteCommandValidator()
    {
        RuleFor(c => c.CreditNoteId).NotEmpty();
        RuleFor(c => c.Reason).NotEmpty();
    }
}

/// <summary>Handler for <see cref="RejectProFormaCreditNoteCommand"/>.</summary>
[CommandSideEffect(SideEffect.Write)]
public sealed class RejectProFormaCreditNoteCommandHandler(
    IProFormaCreditNoteRepository credits,
    IApprovalService approvals,
    ITenantContext tenant)
    : ICommandHandler<RejectProFormaCreditNoteCommand, Unit>
{
    public async Task<Unit> HandleAsync(RejectProFormaCreditNoteCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        ProFormaCreditNote note = await credits.FindAsync(command.CreditNoteId, cancellationToken).ConfigureAwait(false)
            ?? throw new FieldSalesException("PROFORMA_NOT_FOUND", $"No credit proposal {command.CreditNoteId}.");

        if (note.TenantId != tenant.TenantId)
        {
            throw FieldSalesException.Forbidden("reject across tenants");
        }

        if (note.Status is not ProFormaStatus.Submitted)
        {
            throw FieldSalesException.IllegalTransition(note.Status, "be rejected");
        }

        if (note.ApprovalRequestId is { } requestId)
        {
            await approvals.DecideAsync(
                    requestId, ApprovalDecisionOutcome.Rejected, command.Reason, cancellationToken)
                .ConfigureAwait(false);
        }

        note.Reject(command.Reason);
        return Unit.Value;
    }
}
