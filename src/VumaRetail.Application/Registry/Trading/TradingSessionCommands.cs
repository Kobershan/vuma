using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Abstractions.Sales;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry;
using VumaRetail.Domain.Registry.Trading;

#pragma warning disable CS1591
#pragma warning disable CA1062

namespace VumaRetail.Application.Registry.Trading;

// ---------------------------------------------------------------------------
// Open
// ---------------------------------------------------------------------------

/// <summary>Opens a trading session at a shared till. Replays by idempotency key.</summary>
[CommandSideEffect(SideEffect.Write)]
public sealed record OpenTradingSessionCommand(
    Guid PremisesId,
    Guid TerminalId,
    Guid CashierUserId,
    Guid SessionCompanyId,
    string Currency,
    string IdempotencyKey,
    Guid? CustomerGroupPartnerId = null) : ICommand<Guid>;

/// <summary>Validates <see cref="OpenTradingSessionCommand"/>.</summary>
public sealed class OpenTradingSessionCommandValidator : AbstractValidator<OpenTradingSessionCommand>
{
    public OpenTradingSessionCommandValidator()
    {
        RuleFor(c => c.PremisesId).NotEmpty();
        RuleFor(c => c.TerminalId).NotEmpty();
        RuleFor(c => c.CashierUserId).NotEmpty();
        RuleFor(c => c.SessionCompanyId).NotEmpty();
        RuleFor(c => c.Currency).NotEmpty().Length(3);
        RuleFor(c => c.IdempotencyKey).NotEmpty();
    }
}

/// <summary>Handler for <see cref="OpenTradingSessionCommand"/>.</summary>
public sealed class OpenTradingSessionCommandHandler(
    ITradingSessionRepository sessions,
    IDocumentNumberSequence numbers,
    ITenantContext tenant,
    IClock clock)
    : ICommandHandler<OpenTradingSessionCommand, Guid>
{
    public async Task<Guid> HandleAsync(OpenTradingSessionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // §4.11's shape: the till mints the key offline and replays it after a dropped
        // acknowledgement — the second open returns the first session, never a second one.
        TradingSession? existing = await sessions
            .FindByIdempotencyKeyAsync(command.IdempotencyKey.Trim(), cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return existing.Id;
        }

        string number = await numbers.NextAsync("TS", cancellationToken).ConfigureAwait(false);
        var session = TradingSession.Open(
            UuidV7.NewGuid(),
            tenant.TenantId,
            command.SessionCompanyId,
            number,
            command.PremisesId,
            command.TerminalId,
            command.CashierUserId,
            command.Currency,
            command.IdempotencyKey,
            clock.UtcNow,
            command.CustomerGroupPartnerId);
        sessions.Add(session);
        await sessions.CommitSessionAsync(cancellationToken).ConfigureAwait(false);
        return session.Id;
    }
}

// ---------------------------------------------------------------------------
// Add line
// ---------------------------------------------------------------------------

/// <summary>Adds a scanned line to its company's segment. Replays by line id.</summary>
[CommandSideEffect(SideEffect.Write)]
public sealed record AddBasketLineCommand(
    Guid SessionId,
    string Barcode,
    decimal QuantityValue,
    string QuantityUom,
    decimal UnitPriceAmount,
    string Currency,
    decimal DiscountAmount = 0m,
    string TaxCode = "STANDARD",
    Guid? LineId = null) : ICommand<Guid>;

/// <summary>Validates <see cref="AddBasketLineCommand"/>.</summary>
public sealed class AddBasketLineCommandValidator : AbstractValidator<AddBasketLineCommand>
{
    public AddBasketLineCommandValidator()
    {
        RuleFor(c => c.SessionId).NotEmpty();
        RuleFor(c => c.Barcode).NotEmpty();
        RuleFor(c => c.QuantityValue).GreaterThan(0m);
        RuleFor(c => c.QuantityUom).NotEmpty();
        RuleFor(c => c.UnitPriceAmount).GreaterThanOrEqualTo(0m);
        RuleFor(c => c.Currency).NotEmpty().Length(3);
        RuleFor(c => c.DiscountAmount).GreaterThanOrEqualTo(0m);
        RuleFor(c => c.TaxCode).NotEmpty();
    }
}

/// <summary>Handler for <see cref="AddBasketLineCommand"/>.</summary>
/// <remarks>
/// The scan-time guard (TRADING_GROUP.md §2): the company comes from the routing index
/// (ADR-100), and a sister company's line requires an active <c>SharedTill</c> link —
/// refused here, at scan time, never at payment. Same-company lines skip the link read:
/// a company is trivially linked to itself, and the till must keep trading when the
/// registry link table is unreachable for its own goods (R1 outranks group convenience).
/// </remarks>
public sealed class AddBasketLineCommandHandler(
    ITradingSessionRepository sessions,
    IBarcodeResolver barcodes,
    ICompanyLinkService links,
    ITaxCalculator tax,
    IPackSizeResolver packs,
    ITenantContext tenant,
    IClock clock)
    : ICommandHandler<AddBasketLineCommand, Guid>
{
    public async Task<Guid> HandleAsync(AddBasketLineCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        TradingSession session = await sessions.FindAsync(command.SessionId, cancellationToken).ConfigureAwait(false)
            ?? throw new TradingSessionException(
                "TRADING_SESSION_NOT_FOUND", $"No trading session {command.SessionId}.");

        if (session.TenantId != tenant.TenantId)
        {
            throw new TradingSessionException(
                "TRADING_SESSION_WRONG_TENANT", $"Trading session {command.SessionId} belongs to another tenant.");
        }

        // Idempotent replay: the till reuses the line id after a dropped acknowledgement.
        Guid lineId = command.LineId ?? UuidV7.NewGuid();
        TradingSessionLine? replayed = session.Segments
            .SelectMany(segment => segment.Lines)
            .FirstOrDefault(line => line.Id == lineId);
        if (replayed is not null)
        {
            return replayed.Id;
        }

        BarcodeResolution resolution = await barcodes
            .ResolveAsync(command.Barcode.Trim(), cancellationToken)
            .ConfigureAwait(false);
        if (resolution.IsNone)
        {
            throw TradingSessionException.UnroutableBarcode(command.Barcode.Trim());
        }

        // ADR-100 collision order is the resolver's: first candidate wins, and the choice
        // is on the line (CompanyId) where the completion legs and the cashier can see it.
        BarcodeCandidate candidate = resolution.Candidates[0];

        if (candidate.CompanyId != session.SessionCompanyId)
        {
            await links.RequireLink(
                    session.SessionCompanyId, candidate.CompanyId,
                    CompanyLinkScope.SharedTill, cancellationToken)
                .ConfigureAwait(false);
        }

        string currency = command.Currency.Trim().ToUpperInvariant();
        var unitPrice = new Money(command.UnitPriceAmount, currency);
        var discount = new Money(command.DiscountAmount, currency);
        Money extended = unitPrice * command.QuantityValue - discount;

        TaxCalculation calculation = await tax.CalculateAsync(
                command.TaxCode.Trim(), extended, DateOnly.FromDateTime(clock.UtcNow.UtcDateTime), cancellationToken)
            .ConfigureAwait(false);

        PackSizeSnapshot pack = await packs.ResolveAsync(
                candidate.ItemId == Guid.Empty ? null : candidate.ItemId,
                candidate.VariantId,
                command.QuantityUom.Trim(),
                command.QuantityValue,
                cancellationToken)
            .ConfigureAwait(false);

        TradingSessionLine line = session.AddLine(
            lineId,
            candidate.CompanyId,
            command.Barcode.Trim(),
            candidate.ItemId == Guid.Empty ? null : candidate.ItemId,
            candidate.VariantId,
            candidate.Description,
            command.QuantityValue,
            command.QuantityUom.Trim(),
            unitPrice,
            discount,
            calculation.TaxCode,
            calculation.TaxAmount,
            calculation.NetAmount,
            pack.Description,
            priceListId: null,
            clock.UtcNow);

        await sessions.CommitSessionAsync(cancellationToken).ConfigureAwait(false);
        return line.Id;
    }
}

// ---------------------------------------------------------------------------
// Void line / void session
// ---------------------------------------------------------------------------

/// <summary>Takes one scanned line back off the session.</summary>
[CommandSideEffect(SideEffect.Write)]
public sealed record VoidBasketLineCommand(Guid SessionId, Guid LineId) : ICommand;

/// <summary>Validates <see cref="VoidBasketLineCommand"/>.</summary>
public sealed class VoidBasketLineCommandValidator : AbstractValidator<VoidBasketLineCommand>
{
    public VoidBasketLineCommandValidator()
    {
        RuleFor(c => c.SessionId).NotEmpty();
        RuleFor(c => c.LineId).NotEmpty();
    }
}

/// <summary>Handler for <see cref="VoidBasketLineCommand"/>.</summary>
public sealed class VoidBasketLineCommandHandler(
    ITradingSessionRepository sessions,
    ITenantContext tenant,
    IClock clock)
    : ICommandHandler<VoidBasketLineCommand, Unit>
{
    public async Task<Unit> HandleAsync(VoidBasketLineCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        TradingSession session = await sessions.FindAsync(command.SessionId, cancellationToken).ConfigureAwait(false)
            ?? throw new TradingSessionException(
                "TRADING_SESSION_NOT_FOUND", $"No trading session {command.SessionId}.");

        if (session.TenantId != tenant.TenantId)
        {
            throw new TradingSessionException(
                "TRADING_SESSION_WRONG_TENANT", $"Trading session {command.SessionId} belongs to another tenant.");
        }

        session.VoidLine(command.LineId, clock.UtcNow);
        await sessions.CommitSessionAsync(cancellationToken).ConfigureAwait(false);
        return Unit.Value;
    }
}

/// <summary>Abandons a session with a reason. Releases holds, posts nothing.</summary>
[CommandSideEffect(SideEffect.Write)]
public sealed record VoidTradingSessionCommand(Guid SessionId, string Reason) : ICommand;

/// <summary>Validates <see cref="VoidTradingSessionCommand"/>.</summary>
public sealed class VoidTradingSessionCommandValidator : AbstractValidator<VoidTradingSessionCommand>
{
    public VoidTradingSessionCommandValidator()
    {
        RuleFor(c => c.SessionId).NotEmpty();
        RuleFor(c => c.Reason).NotEmpty();
    }
}

/// <summary>Handler for <see cref="VoidTradingSessionCommand"/>.</summary>
/// <remarks>
/// A session voided before completion holds no reservations — holds are taken inside the
/// completion legs (TASK-09B-002), never at scan time — so there is nothing to release here.
/// Post-failure voids likewise find no open holds: compensation released them. The void
/// therefore only ever transitions state; the release-on-void invariant is proven by 002's
/// void-releases test against a session whose legs never ran.
/// </remarks>
public sealed class VoidTradingSessionCommandHandler(
    ITradingSessionRepository sessions,
    ITenantContext tenant,
    IClock clock)
    : ICommandHandler<VoidTradingSessionCommand, Unit>
{
    public async Task<Unit> HandleAsync(VoidTradingSessionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        TradingSession session = await sessions.FindAsync(command.SessionId, cancellationToken).ConfigureAwait(false)
            ?? throw new TradingSessionException(
                "TRADING_SESSION_NOT_FOUND", $"No trading session {command.SessionId}.");

        if (session.TenantId != tenant.TenantId)
        {
            throw new TradingSessionException(
                "TRADING_SESSION_WRONG_TENANT", $"Trading session {command.SessionId} belongs to another tenant.");
        }

        session.Void(command.Reason, clock.UtcNow);
        await sessions.CommitSessionAsync(cancellationToken).ConfigureAwait(false);
        return Unit.Value;
    }
}

// ---------------------------------------------------------------------------
// Tender
// ---------------------------------------------------------------------------

/// <summary>Captures the one tender against the session. Replays by tender id.</summary>
[CommandSideEffect(SideEffect.Write)]
public sealed record CaptureTenderCommand(
    Guid SessionId,
    string TenderType,
    decimal Amount,
    string Currency,
    string? Reference = null,
    Guid? TenderId = null) : ICommand;

/// <summary>Validates <see cref="CaptureTenderCommand"/>.</summary>
public sealed class CaptureTenderCommandValidator : AbstractValidator<CaptureTenderCommand>
{
    public CaptureTenderCommandValidator()
    {
        RuleFor(c => c.SessionId).NotEmpty();
        RuleFor(c => c.TenderType).NotEmpty();
        RuleFor(c => c.Amount).GreaterThan(0m);
        RuleFor(c => c.Currency).NotEmpty().Length(3);
    }
}

/// <summary>Handler for <see cref="CaptureTenderCommand"/>.</summary>
public sealed class CaptureTenderCommandHandler(
    ITradingSessionRepository sessions,
    ITenantContext tenant,
    IClock clock)
    : ICommandHandler<CaptureTenderCommand, Unit>
{
    public async Task<Unit> HandleAsync(CaptureTenderCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        TradingSession session = await sessions.FindAsync(command.SessionId, cancellationToken).ConfigureAwait(false)
            ?? throw new TradingSessionException(
                "TRADING_SESSION_NOT_FOUND", $"No trading session {command.SessionId}.");

        if (session.TenantId != tenant.TenantId)
        {
            throw new TradingSessionException(
                "TRADING_SESSION_WRONG_TENANT", $"Trading session {command.SessionId} belongs to another tenant.");
        }

        // Tender replay: capturing over an identical tender is the dropped-acknowledgement
        // shape, not a second payment. A different amount on a tendered session is refused
        // by the aggregate (illegal transition), which is the correct answer — money taken
        // twice must be a loud error, never a quiet second tender.
        if (session.TenderAmount is not null
            && session.TenderType == command.TenderType.Trim()
            && session.TenderAmount.Value.Amount == command.Amount)
        {
            return Unit.Value;
        }

        session.CaptureTender(
            command.TenderType,
            new Money(command.Amount, command.Currency),
            command.Reference,
            clock.UtcNow);
        await sessions.CommitSessionAsync(cancellationToken).ConfigureAwait(false);
        return Unit.Value;
    }
}

/// <summary>Replaces the proportional allocation with the cashier's exact split.</summary>
[CommandSideEffect(SideEffect.Write)]
public sealed record OverrideTenderAllocationCommand(
    Guid SessionId,
    IReadOnlyList<AllocationOverride> Allocations) : ICommand;

/// <summary>One override entry: the company and its exact share.</summary>
public sealed record AllocationOverride(Guid CompanyId, decimal Amount, string Currency);

/// <summary>Validates <see cref="OverrideTenderAllocationCommand"/>.</summary>
public sealed class OverrideTenderAllocationCommandValidator : AbstractValidator<OverrideTenderAllocationCommand>
{
    public OverrideTenderAllocationCommandValidator()
    {
        RuleFor(c => c.SessionId).NotEmpty();
        RuleFor(c => c.Allocations).NotEmpty();
    }
}

/// <summary>Handler for <see cref="OverrideTenderAllocationCommand"/>.</summary>
public sealed class OverrideTenderAllocationCommandHandler(
    ITradingSessionRepository sessions,
    ITenantContext tenant)
    : ICommandHandler<OverrideTenderAllocationCommand, Unit>
{
    public async Task<Unit> HandleAsync(OverrideTenderAllocationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        TradingSession session = await sessions.FindAsync(command.SessionId, cancellationToken).ConfigureAwait(false)
            ?? throw new TradingSessionException(
                "TRADING_SESSION_NOT_FOUND", $"No trading session {command.SessionId}.");

        if (session.TenantId != tenant.TenantId)
        {
            throw new TradingSessionException(
                "TRADING_SESSION_WRONG_TENANT", $"Trading session {command.SessionId} belongs to another tenant.");
        }

        session.OverrideAllocation([.. command.Allocations
            .Select(entry => (entry.CompanyId, new Money(entry.Amount, entry.Currency)))]);
        await sessions.CommitSessionAsync(cancellationToken).ConfigureAwait(false);
        return Unit.Value;
    }
}

// ---------------------------------------------------------------------------
// Complete (thin — the saga is TASK-09B-002's service)
// ---------------------------------------------------------------------------

/// <summary>Completes a tendered session: one leg per segment (TASK-09B-002).</summary>
[CommandSideEffect(SideEffect.Write)]
public sealed record CompleteTradingSessionCommand(Guid SessionId) : ICommand<IReadOnlyList<CompletedSegmentResult>>;

/// <summary>Validates <see cref="CompleteTradingSessionCommand"/>.</summary>
public sealed class CompleteTradingSessionCommandValidator : AbstractValidator<CompleteTradingSessionCommand>
{
    public CompleteTradingSessionCommandValidator()
    {
        RuleFor(c => c.SessionId).NotEmpty();
    }
}

/// <summary>One posted segment: the sale and the tax invoice behind it.</summary>
public sealed record CompletedSegmentResult(
    Guid CompanyId, Guid SaleId, Guid InvoiceId, string InvoiceNumber);

/// <summary>Handler for <see cref="CompleteTradingSessionCommand"/>.</summary>
public sealed class CompleteTradingSessionCommandHandler(
    IMixedBasketCompletionService completion)
    : ICommandHandler<CompleteTradingSessionCommand, IReadOnlyList<CompletedSegmentResult>>
{
    public async Task<IReadOnlyList<CompletedSegmentResult>> HandleAsync(
        CompleteTradingSessionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        IReadOnlyList<CompletedSegment> posted = await completion
            .CompleteAsync(command.SessionId, cancellationToken)
            .ConfigureAwait(false);

        return [.. posted.Select(segment =>
            new CompletedSegmentResult(
                segment.CompanyId, segment.SaleId, segment.InvoiceId, segment.InvoiceNumber))];
    }
}
