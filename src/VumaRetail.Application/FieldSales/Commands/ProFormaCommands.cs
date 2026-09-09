using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.FieldSales;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Abstractions.Sales;
using VumaRetail.Application.Abstractions.Workflow;
using VumaRetail.Domain.FieldSales;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Workflow;

#pragma warning disable CS1591
#pragma warning disable CA1062

namespace VumaRetail.Application.FieldSales.Commands;

// ---------------------------------------------------------------------------
// Capture
// ---------------------------------------------------------------------------

/// <summary>One captured line: caller prices, snapshots frozen at capture.</summary>
public sealed record ProFormaLineInput(
    Guid? ItemId,
    Guid? ItemVariantId,
    decimal QuantityValue,
    string QuantityUom,
    decimal UnitPriceAmount,
    decimal DiscountAmount,
    string TaxCode,
    string Currency);

/// <summary>Captures a pro forma order. Replays by idempotency key.</summary>
public sealed record CaptureProFormaCommand(
    Guid RepId,
    Guid CompanyId,
    Guid PartnerId,
    string Currency,
    string IdempotencyKey,
    IReadOnlyList<ProFormaLineInput> Lines,
    string? DeliveryLine1 = null,
    string? DeliveryLine2 = null,
    string? DeliveryCity = null,
    string? DeliveryRegion = null,
    string? DeliveryPostalCode = null,
    string? DeliveryCountryCode = null) : ICommand<Guid>;

/// <summary>Validates <see cref="CaptureProFormaCommand"/>.</summary>
public sealed class CaptureProFormaCommandValidator : AbstractValidator<CaptureProFormaCommand>
{
    public CaptureProFormaCommandValidator()
    {
        RuleFor(c => c.RepId).NotEmpty();
        RuleFor(c => c.CompanyId).NotEmpty();
        RuleFor(c => c.PartnerId).NotEmpty();
        RuleFor(c => c.Currency).NotEmpty().Length(3);
        RuleFor(c => c.IdempotencyKey).NotEmpty();
        RuleFor(c => c.Lines).NotEmpty();
    }
}

/// <summary>Handler for <see cref="CaptureProFormaCommand"/>.</summary>
/// <remarks>
/// A proposal commits nothing (ADR-107): this handler writes the pro forma and its snapshots
/// only. No finance poster, no reservation service, no stock poster is even referenced —
/// field-sales-guard proves the absence by dependency, not by reading the happy path.
/// </remarks>
[CommandSideEffect(SideEffect.Write)]
public sealed class CaptureProFormaCommandHandler(
    IProFormaOrderRepository proFormas,
    IRepRepository reps,
    IDocumentNumberSequence numbers,
    ITenantContext tenant,
    ITaxCalculator tax,
    IPackSizeResolver packs,
    IAvailabilityProbe availability,
    IClock clock)
    : ICommandHandler<CaptureProFormaCommand, Guid>
{
    public async Task<Guid> HandleAsync(CaptureProFormaCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        ProFormaOrder? existing = await proFormas
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
            throw FieldSalesException.Forbidden($"sell for company {command.CompanyId}");
        }

        if (!rep.MayQuote(command.PartnerId))
        {
            throw FieldSalesException.Forbidden($"quote customer {command.PartnerId}");
        }

        if (rep.TenantId != tenant.TenantId)
        {
            throw FieldSalesException.Forbidden("capture across tenants");
        }

        string currency = command.Currency.Trim().ToUpperInvariant();
        string number = await numbers.NextAsync("PF", cancellationToken).ConfigureAwait(false);
        Address? deliveryAddress = command.DeliveryLine1 is null || command.DeliveryCity is null || command.DeliveryCountryCode is null
            ? null
            : Address.Create(
                command.DeliveryLine1, command.DeliveryCity, command.DeliveryCountryCode,
                command.DeliveryLine2, command.DeliveryRegion, command.DeliveryPostalCode);
        ProFormaOrder order = ProFormaOrder.Capture(
            tenant.TenantId, tenant.StoreId, number, rep.Id, command.CompanyId,
            command.PartnerId, currency, command.IdempotencyKey, clock.UtcNow,
            deliveryAddress: deliveryAddress);

        foreach (ProFormaLineInput input in command.Lines)
        {
            string lineCurrency = input.Currency.Trim().ToUpperInvariant();
            if (!string.Equals(lineCurrency, currency, StringComparison.Ordinal))
            {
                throw new FieldSalesException(
                    "PROFORMA_CURRENCY_MISMATCH",
                    $"Line currency {lineCurrency} does not match document currency {currency}.");
            }

            var unitPrice = new Money(input.UnitPriceAmount, currency);
            var discount = new Money(input.DiscountAmount, currency);
            Money extended = unitPrice * input.QuantityValue - discount;

            TaxCalculation calculation = await tax.CalculateAsync(
                    input.TaxCode.Trim(), extended,
                    DateOnly.FromDateTime(clock.UtcNow.UtcDateTime), cancellationToken)
                .ConfigureAwait(false);

            PackSizeSnapshot pack = await packs.ResolveAsync(
                    input.ItemId, input.ItemVariantId, input.QuantityUom.Trim(),
                    input.QuantityValue, cancellationToken)
                .ConfigureAwait(false);

            AvailabilityProbeResult seen = await availability.ProbeAsync(
                    command.CompanyId, input.ItemId, input.ItemVariantId, cancellationToken)
                .ConfigureAwait(false);

            order.AddLine(
                input.ItemId, input.ItemVariantId, input.QuantityValue, input.QuantityUom.Trim(),
                unitPrice, discount, calculation.TaxCode, calculation.TaxAmount, calculation.NetAmount,
                pack.Description, priceListId: null, promotionsSummary: string.Empty,
                new Money(seen.Available, currency), seen.AsAt);
        }

        proFormas.Add(order);
        return order.Id;
    }
}

// ---------------------------------------------------------------------------
// Submit / amend / withdraw / expire
// ---------------------------------------------------------------------------

/// <summary>Submits a draft for approval. Raises the Stage 05 request.</summary>
public sealed record SubmitProFormaCommand(Guid ProFormaId) : ICommand;

/// <summary>Validates <see cref="SubmitProFormaCommand"/>.</summary>
public sealed class SubmitProFormaCommandValidator : AbstractValidator<SubmitProFormaCommand>
{
    public SubmitProFormaCommandValidator()
    {
        RuleFor(c => c.ProFormaId).NotEmpty();
    }
}

/// <summary>Handler for <see cref="SubmitProFormaCommand"/>.</summary>
[CommandSideEffect(SideEffect.Write)]
public sealed class SubmitProFormaCommandHandler(
    IProFormaOrderRepository proFormas,
    IApprovalService approvals,
    ITenantContext tenant,
    IClock clock)
    : ICommandHandler<SubmitProFormaCommand, Unit>
{
    public async Task<Unit> HandleAsync(SubmitProFormaCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        ProFormaOrder order = await proFormas.FindAsync(command.ProFormaId, cancellationToken).ConfigureAwait(false)
            ?? throw new FieldSalesException("PROFORMA_NOT_FOUND", $"No pro forma {command.ProFormaId}.");

        if (order.TenantId != tenant.TenantId)
        {
            throw FieldSalesException.Forbidden("submit across tenants");
        }

        if (order.IsExpired(clock.UtcNow))
        {
            order.Expire();
            throw FieldSalesException.Expired(order.ProFormaNumber);
        }

        order.Submit(clock.UtcNow);

        // The engine decides: no policy (or below threshold) proceeds immediately — but a
        // pro forma still converts only through ApproveProFormaCommand, never here. Under the
        // default field-sales policy every submit pends, and the request id is the receipt.
        ApprovalOutcome outcome = await approvals.EvaluateAsync(
                new ApprovalContext("field-sales", "ProFormaOrder", "Approve", order.Id, order.Gross),
                cancellationToken)
            .ConfigureAwait(false);

        if (outcome.RequestId is { } requestId)
        {
            order.RecordApproval(requestId);
        }

        return Unit.Value;
    }
}

/// <summary>Returns a submitted pro forma to draft for amendment, with the reason.</summary>
public sealed record AmendProFormaCommand(Guid ProFormaId, string Reason) : ICommand;

/// <summary>Validates <see cref="AmendProFormaCommand"/>.</summary>
public sealed class AmendProFormaCommandValidator : AbstractValidator<AmendProFormaCommand>
{
    public AmendProFormaCommandValidator()
    {
        RuleFor(c => c.ProFormaId).NotEmpty();
        RuleFor(c => c.Reason).NotEmpty();
    }
}

/// <summary>Handler for <see cref="AmendProFormaCommand"/>.</summary>
[CommandSideEffect(SideEffect.Write)]
public sealed class AmendProFormaCommandHandler(
    IProFormaOrderRepository proFormas,
    ITenantContext tenant,
    IClock clock)
    : ICommandHandler<AmendProFormaCommand, Unit>
{
    public async Task<Unit> HandleAsync(AmendProFormaCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        ProFormaOrder order = await proFormas.FindAsync(command.ProFormaId, cancellationToken).ConfigureAwait(false)
            ?? throw new FieldSalesException("PROFORMA_NOT_FOUND", $"No pro forma {command.ProFormaId}.");

        if (order.TenantId != tenant.TenantId)
        {
            throw FieldSalesException.Forbidden("amend across tenants");
        }

        order.ReturnForAmendment(command.Reason, clock.UtcNow);
        return Unit.Value;
    }
}

/// <summary>Withdraws a pro forma before a decision.</summary>
public sealed record WithdrawProFormaCommand(Guid ProFormaId, string Reason) : ICommand;

/// <summary>Validates <see cref="WithdrawProFormaCommand"/>.</summary>
public sealed class WithdrawProFormaCommandValidator : AbstractValidator<WithdrawProFormaCommand>
{
    public WithdrawProFormaCommandValidator()
    {
        RuleFor(c => c.ProFormaId).NotEmpty();
        RuleFor(c => c.Reason).NotEmpty();
    }
}

/// <summary>Handler for <see cref="WithdrawProFormaCommand"/>.</summary>
[CommandSideEffect(SideEffect.Write)]
public sealed class WithdrawProFormaCommandHandler(
    IProFormaOrderRepository proFormas,
    ITenantContext tenant)
    : ICommandHandler<WithdrawProFormaCommand, Unit>
{
    public async Task<Unit> HandleAsync(WithdrawProFormaCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        ProFormaOrder order = await proFormas.FindAsync(command.ProFormaId, cancellationToken).ConfigureAwait(false)
            ?? throw new FieldSalesException("PROFORMA_NOT_FOUND", $"No pro forma {command.ProFormaId}.");

        if (order.TenantId != tenant.TenantId)
        {
            throw FieldSalesException.Forbidden("withdraw across tenants");
        }

        order.Withdraw(command.Reason);
        return Unit.Value;
    }
}

/// <summary>Expires every lapsed undecided pro forma. The hosted service's command.</summary>
public sealed record ExpireProFormasCommand : ICommand<int>;

/// <summary>Handler for <see cref="ExpireProFormasCommand"/>.</summary>
/// <remarks>Lists per rep would page forever; expiry scans convertible documents directly.</remarks>
[CommandSideEffect(SideEffect.Write)]
public sealed class ExpireProFormasCommandHandler(
    IProFormaOrderRepository proFormas,
    IProFormaCreditNoteRepository credits,
    IRepRepository reps,
    IClock clock)
    : ICommandHandler<ExpireProFormasCommand, int>
{
    public async Task<int> HandleAsync(ExpireProFormasCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        int expired = 0;
        foreach (Rep rep in await reps.ListAllAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (ProFormaOrder order in await proFormas.ListConvertibleAsync(rep.Id, cancellationToken).ConfigureAwait(false))
            {
                if (order.IsExpired(clock.UtcNow))
                {
                    order.Expire();
                    expired++;
                }
            }

            foreach (ProFormaCreditNote note in await credits.ListConvertibleAsync(rep.Id, cancellationToken).ConfigureAwait(false))
            {
                if (note.IsExpired(clock.UtcNow))
                {
                    note.Expire();
                    expired++;
                }
            }
        }

        return expired;
    }
}

// ---------------------------------------------------------------------------
// Approve / reject (management; the saga runs in TASK-14B-002's service)
// ---------------------------------------------------------------------------

/// <summary>Approves a submitted pro forma: decides in Stage 05, then converts via the saga.</summary>
public sealed record ApproveProFormaCommand(Guid ProFormaId, string Comment) : ICommand<Guid>;

/// <summary>Validates <see cref="ApproveProFormaCommand"/>.</summary>
public sealed class ApproveProFormaCommandValidator : AbstractValidator<ApproveProFormaCommand>
{
    public ApproveProFormaCommandValidator()
    {
        RuleFor(c => c.ProFormaId).NotEmpty();
    }
}

/// <summary>Handler for <see cref="ApproveProFormaCommand"/>.</summary>
[CommandSideEffect(SideEffect.Write)]
public sealed class ApproveProFormaCommandHandler(
    IProFormaOrderRepository proFormas,
    IApprovalService approvals,
    IFieldSalesApprovalService conversion,
    IPrincipalAccessor principal,
    ITenantContext tenant)
    : ICommandHandler<ApproveProFormaCommand, Guid>
{
    public async Task<Guid> HandleAsync(ApproveProFormaCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        ProFormaOrder order = await proFormas.FindAsync(command.ProFormaId, cancellationToken).ConfigureAwait(false)
            ?? throw new FieldSalesException("PROFORMA_NOT_FOUND", $"No pro forma {command.ProFormaId}.");

        if (order.TenantId != tenant.TenantId)
        {
            throw FieldSalesException.Forbidden("approve across tenants");
        }

        // No expiry refusal here: the saga re-prices against today's list as its first step
        // (business rule 4 — approval IS the re-price), and the delta is reported, not hidden.
        // Replay short-circuit: a crash between conversion and acknowledgement replays the
        // same approval. Deciding twice is refused by the engine (one decider, one decision),
        // so a converted document returns its order without touching the request again.
        if (order.Status is ProFormaStatus.Converted && order.ConvertedOrderId is { } converted)
        {
            return converted;
        }

        if (order.Status is not ProFormaStatus.Submitted)
        {
            throw FieldSalesException.IllegalTransition(order.Status, "be approved");
        }

        // The engine decides — this module implements no approval logic of its own (rule 13).
        // Under the default policy every submit pended at Submit time; the stored request id is
        // decided here. Without a policy (or below threshold) the engine auto-approves and the
        // saga still runs exactly once, through the same call below.
        if (order.ApprovalRequestId is { } requestId)
        {
            ApprovalDecisionResult decision = await approvals.DecideAsync(
                    requestId, ApprovalDecisionOutcome.Approved, command.Comment, cancellationToken)
                .ConfigureAwait(false);

            if (decision.Status is not ApprovalRequestStatus.Approved)
            {
                throw new FieldSalesException(
                    "PROFORMA_NOT_APPROVED",
                    $"Approval request {requestId} decided {decision.Status}; the saga does not run.");
            }
        }

        ApprovedProForma approved = await conversion.ApproveOrderAsync(
                order.Id, principal.Principal, cancellationToken)
            .ConfigureAwait(false);

        return approved.OrderId;
    }
}

/// <summary>Rejects a submitted pro forma with a reason.</summary>
public sealed record RejectProFormaCommand(Guid ProFormaId, string Reason) : ICommand;

/// <summary>Validates <see cref="RejectProFormaCommand"/>.</summary>
public sealed class RejectProFormaCommandValidator : AbstractValidator<RejectProFormaCommand>
{
    public RejectProFormaCommandValidator()
    {
        RuleFor(c => c.ProFormaId).NotEmpty();
        RuleFor(c => c.Reason).NotEmpty();
    }
}

/// <summary>Handler for <see cref="RejectProFormaCommand"/>.</summary>
[CommandSideEffect(SideEffect.Write)]
public sealed class RejectProFormaCommandHandler(
    IProFormaOrderRepository proFormas,
    IApprovalService approvals,
    ITenantContext tenant,
    IClock clock)
    : ICommandHandler<RejectProFormaCommand, Unit>
{
    public async Task<Unit> HandleAsync(RejectProFormaCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        ProFormaOrder order = await proFormas.FindAsync(command.ProFormaId, cancellationToken).ConfigureAwait(false)
            ?? throw new FieldSalesException("PROFORMA_NOT_FOUND", $"No pro forma {command.ProFormaId}.");

        if (order.TenantId != tenant.TenantId)
        {
            throw FieldSalesException.Forbidden("reject across tenants");
        }

        if (order.Status is not ProFormaStatus.Submitted)
        {
            throw FieldSalesException.IllegalTransition(order.Status, "be rejected");
        }

        if (order.ApprovalRequestId is { } requestId)
        {
            await approvals.DecideAsync(
                    requestId, ApprovalDecisionOutcome.Rejected, command.Reason, cancellationToken)
                .ConfigureAwait(false);
        }

        order.Reject(command.Reason, clock.UtcNow);
        return Unit.Value;
    }
}
