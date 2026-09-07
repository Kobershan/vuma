using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Abstractions.Sales;
using VumaRetail.Application.Pos;
using VumaRetail.Domain.Sales.Quotes;

namespace VumaRetail.Application.Sales.Commands.Quotes;

/// <summary>Opens a draft quote: a priced basket with an expiry, promising price but never stock.</summary>
/// <param name="CustomerId">The customer the price is promised to.</param>
/// <param name="Currency">The ISO 4217 currency.</param>
/// <param name="ValidUntil">The last day the promise holds.</param>
/// <param name="GroupId">The trading-group document reference, when the quote spans companies.</param>
/// <param name="CompanyId">The owning company, or <c>null</c> for the ambient acting company.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record CreateQuoteCommand(
    Guid CustomerId,
    string Currency,
    DateOnly ValidUntil,
    string? GroupId = null,
    Guid? CompanyId = null) : ICommand<Guid>;

/// <summary>Rejects a malformed quote command before it reaches the handler.</summary>
public sealed class CreateQuoteCommandValidator : AbstractValidator<CreateQuoteCommand>
{
    /// <summary>Builds the rules.</summary>
    public CreateQuoteCommandValidator()
    {
        RuleFor(command => command.CustomerId).NotEmpty();
        RuleFor(command => command.Currency).NotEmpty().Length(3);
        RuleFor(command => command.ValidUntil).NotEmpty();
        RuleFor(command => command.GroupId).MaximumLength(64);
    }
}

/// <summary>Opens the draft, drawing its number from ADR-065's <c>QTE</c> series.</summary>
/// <param name="quotes">Quote insertion.</param>
/// <param name="tenant">The ambient tenant and store.</param>
/// <param name="company">The ambient acting company, when the command names none.</param>
/// <param name="numbers">ADR-065's gap-free document number sequence.</param>
/// <param name="clock">The only source of time.</param>
public sealed class CreateQuoteCommandHandler(
    IQuoteRepository quotes,
    ITenantContext tenant,
    ICompanyContext company,
    IDocumentNumberSequence numbers,
    IClock clock) : ICommandHandler<CreateQuoteCommand, Guid>
{
    /// <summary>The document number series a quote number is drawn from.</summary>
    public const string QuoteNumberSeries = "QTE";

    /// <inheritdoc />
    public async Task<Guid> HandleAsync(CreateQuoteCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (tenant.TenantId == Guid.Empty)
        {
            throw new InvalidOperationException("A quote needs an authenticated tenant.");
        }

        DateTimeOffset validUntil = new(command.ValidUntil.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        if (validUntil < clock.UtcNow)
        {
            throw QuotesRuleException.QuoteExpired();
        }

        string quoteNumber = await numbers.NextAsync(QuoteNumberSeries, cancellationToken).ConfigureAwait(false);

        Quote quote = Quote.Create(
            tenant.TenantId,
            tenant.StoreId,
            quoteNumber,
            command.CustomerId,
            command.Currency,
            validUntil,
            command.GroupId,
            command.CompanyId ?? company.CompanyId);

        quotes.Add(quote);

        return quote.Id;
    }
}

/// <summary>Locks a draft's prices and hands it to the customer.</summary>
/// <param name="QuoteId">The quote.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record IssueQuoteCommand(Guid QuoteId) : ICommand;

/// <summary>Rejects a malformed issue command before it reaches the handler.</summary>
public sealed class IssueQuoteCommandValidator : AbstractValidator<IssueQuoteCommand>
{
    /// <summary>Builds the rules.</summary>
    public IssueQuoteCommandValidator()
    {
        RuleFor(command => command.QuoteId).NotEmpty();
    }
}

/// <summary>Issues the quote.</summary>
/// <param name="quotes">Quote lookup.</param>
/// <param name="clock">The only source of time.</param>
public sealed class IssueQuoteCommandHandler(
    IQuoteRepository quotes,
    IClock clock) : ICommandHandler<IssueQuoteCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(IssueQuoteCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Quote quote = await quotes.FindAsync(command.QuoteId, cancellationToken).ConfigureAwait(false)
            ?? throw new QuotesNotFoundException("quote", command.QuoteId);

        quote.Issue(clock.UtcNow);

        return Unit.Value;
    }
}

/// <summary>Records the customer's yes, inside the validity window.</summary>
/// <param name="QuoteId">The quote.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record AcceptQuoteCommand(Guid QuoteId) : ICommand;

/// <summary>Rejects a malformed accept command before it reaches the handler.</summary>
public sealed class AcceptQuoteCommandValidator : AbstractValidator<AcceptQuoteCommand>
{
    /// <summary>Builds the rules.</summary>
    public AcceptQuoteCommandValidator()
    {
        RuleFor(command => command.QuoteId).NotEmpty();
    }
}

/// <summary>Accepts the quote.</summary>
/// <param name="quotes">Quote lookup.</param>
/// <param name="clock">The only source of time.</param>
public sealed class AcceptQuoteCommandHandler(
    IQuoteRepository quotes,
    IClock clock) : ICommandHandler<AcceptQuoteCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(AcceptQuoteCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Quote quote = await quotes.FindAsync(command.QuoteId, cancellationToken).ConfigureAwait(false)
            ?? throw new QuotesNotFoundException("quote", command.QuoteId);

        quote.Accept(clock.UtcNow);

        return Unit.Value;
    }
}

/// <summary>Records the customer's no.</summary>
/// <param name="QuoteId">The quote.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record RejectQuoteCommand(Guid QuoteId) : ICommand;

/// <summary>Rejects a malformed reject command before it reaches the handler.</summary>
public sealed class RejectQuoteCommandValidator : AbstractValidator<RejectQuoteCommand>
{
    /// <summary>Builds the rules.</summary>
    public RejectQuoteCommandValidator()
    {
        RuleFor(command => command.QuoteId).NotEmpty();
    }
}

/// <summary>Rejects the quote.</summary>
/// <param name="quotes">Quote lookup.</param>
public sealed class RejectQuoteCommandHandler(
    IQuoteRepository quotes) : ICommandHandler<RejectQuoteCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(RejectQuoteCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Quote quote = await quotes.FindAsync(command.QuoteId, cancellationToken).ConfigureAwait(false)
            ?? throw new QuotesNotFoundException("quote", command.QuoteId);

        quote.Reject();

        return Unit.Value;
    }
}

/// <summary>Withdraws the promise before or at its lapse.</summary>
/// <param name="QuoteId">The quote.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record ExpireQuoteCommand(Guid QuoteId) : ICommand;

/// <summary>Rejects a malformed expire command before it reaches the handler.</summary>
public sealed class ExpireQuoteCommandValidator : AbstractValidator<ExpireQuoteCommand>
{
    /// <summary>Builds the rules.</summary>
    public ExpireQuoteCommandValidator()
    {
        RuleFor(command => command.QuoteId).NotEmpty();
    }
}

/// <summary>Expires the quote.</summary>
/// <param name="quotes">Quote lookup.</param>
public sealed class ExpireQuoteCommandHandler(
    IQuoteRepository quotes) : ICommandHandler<ExpireQuoteCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(ExpireQuoteCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Quote quote = await quotes.FindAsync(command.QuoteId, cancellationToken).ConfigureAwait(false)
            ?? throw new QuotesNotFoundException("quote", command.QuoteId);

        quote.Expire();

        return Unit.Value;
    }
}

/// <summary>
/// Snapshots one priced line onto a draft quote. The price comes from Stage 10's resolver at this
/// instant, the tax from Stage 07's engine, the pack size from the resolver beside them — all
/// frozen, never re-resolved (ADR-074, ADR-075, ADR-112).
/// </summary>
/// <param name="QuoteId">The draft quote.</param>
/// <param name="ItemId">The item, when it has no variants.</param>
/// <param name="ItemVariantId">The variant.</param>
/// <param name="Quantity">How much. Must be positive.</param>
/// <param name="Uom">The unit the quantity is counted in.</param>
/// <param name="PriceListId">The price list to resolve against, or <c>null</c> for the winning list.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record AddQuoteLineCommand(
    Guid QuoteId,
    Guid? ItemId,
    Guid? ItemVariantId,
    decimal Quantity,
    string Uom,
    Guid? PriceListId = null) : ICommand<Guid>;

/// <summary>Rejects a malformed add-line command before it reaches the handler.</summary>
public sealed class AddQuoteLineCommandValidator : AbstractValidator<AddQuoteLineCommand>
{
    /// <summary>Builds the rules.</summary>
    public AddQuoteLineCommandValidator()
    {
        RuleFor(command => command.QuoteId).NotEmpty();
        RuleFor(command => command.Quantity).GreaterThan(0m);
        RuleFor(command => command.Uom).NotEmpty().MaximumLength(16);
        RuleFor(command => command).Must(HaveExactlyOneItemOrVariant)
            .WithMessage("Exactly one of ItemId or ItemVariantId must be set.");
    }

    private static bool HaveExactlyOneItemOrVariant(AddQuoteLineCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return command.ItemId.HasValue != command.ItemVariantId.HasValue;
    }
}

/// <summary>Snapshots the resolved price, tax and pack size onto the draft.</summary>
/// <param name="quotes">Quote lookup.</param>
/// <param name="catalog">Resolves the item's tax class.</param>
/// <param name="prices">Stage 10's price resolver.</param>
/// <param name="tax">Stage 07's tax rules engine.</param>
/// <param name="packs">The pack size resolver (ADR-112).</param>
/// <param name="clock">The only source of time.</param>
public sealed class AddQuoteLineCommandHandler(
    IQuoteRepository quotes,
    ISellableItemResolver catalog,
    IPriceResolver prices,
    ITaxCalculator tax,
    IPackSizeResolver packs,
    IClock clock) : ICommandHandler<AddQuoteLineCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(AddQuoteLineCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Quote quote = await quotes.FindAsync(command.QuoteId, cancellationToken).ConfigureAwait(false)
            ?? throw new QuotesNotFoundException("quote", command.QuoteId);

        DateTimeOffset now = clock.UtcNow;
        DateOnly today = DateOnly.FromDateTime(now.UtcDateTime);
        TimeOnly nowTime = TimeOnly.FromDateTime(now.UtcDateTime);

        SellableItem item = await catalog.ResolveAsync(
            command.ItemId, command.ItemVariantId, cancellationToken).ConfigureAwait(false);

        PriceResolution resolution = await prices.ResolveAsync(
            new PriceResolutionRequest(
                command.ItemId,
                command.ItemVariantId,
                null,
                command.Quantity,
                quote.StoreId,
                today,
                nowTime,
                quote.Currency),
            cancellationToken).ConfigureAwait(false);

        // Tax is computed once, here, from the resolved net — and stored. Nothing downstream
        // recomputes it (ADR-075), which is why a later rate change cannot restate this quote.
        TaxCalculation calculation = await tax.CalculateAsync(
            item.TaxClassCode, resolution.NetPayable, today, cancellationToken).ConfigureAwait(false);

        PackSizeSnapshot pack = await packs.ResolveAsync(
            command.ItemId, command.ItemVariantId, command.Uom, command.Quantity, cancellationToken).ConfigureAwait(false);

        QuoteLine line = QuoteLine.Create(
            quote.TenantId,
            quote.StoreId,
            quote.Id,
            command.ItemId,
            command.ItemVariantId,
            command.Quantity,
            command.Uom,
            resolution.UnitPrice,
            resolution.DiscountAmount,
            calculation.TaxAmount,
            pack.Description,
            quote.Currency,
            command.PriceListId ?? resolution.PriceListId,
            string.Join(", ", resolution.Promotions.Select(promotion => promotion.Code)));

        quote.AddLine(line);

        return line.Id;
    }
}

/// <summary>
/// Converts an accepted quote into a Stage 14 order. Marks the quote converted so one acceptance
/// can never become two orders; Stage 14 builds the real document from this quote's snapshots.
/// </summary>
/// <param name="QuoteId">The accepted quote.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record ConvertQuoteToOrderCommand(Guid QuoteId) : ICommand<Guid>;

/// <summary>Rejects a malformed convert command before it reaches the handler.</summary>
public sealed class ConvertQuoteToOrderCommandValidator : AbstractValidator<ConvertQuoteToOrderCommand>
{
    /// <summary>Builds the rules.</summary>
    public ConvertQuoteToOrderCommandValidator()
    {
        RuleFor(command => command.QuoteId).NotEmpty();
    }
}

/// <summary>Marks the accepted quote converted.</summary>
/// <param name="quotes">Quote lookup.</param>
public sealed class ConvertQuoteToOrderCommandHandler(
    IQuoteRepository quotes) : ICommandHandler<ConvertQuoteToOrderCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(ConvertQuoteToOrderCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Quote quote = await quotes.FindAsync(command.QuoteId, cancellationToken).ConfigureAwait(false)
            ?? throw new QuotesNotFoundException("quote", command.QuoteId);

        quote.MarkConverted();

        return quote.Id;
    }
}

/// <summary>
/// Converts an accepted quote into a Stage 09 till sale. Marks the quote converted so one acceptance
/// can never become two sales; the till builds the real document from this quote's snapshots.
/// </summary>
/// <param name="QuoteId">The accepted quote.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record ConvertQuoteToSaleCommand(Guid QuoteId) : ICommand<Guid>;

/// <summary>Rejects a malformed convert command before it reaches the handler.</summary>
public sealed class ConvertQuoteToSaleCommandValidator : AbstractValidator<ConvertQuoteToSaleCommand>
{
    /// <summary>Builds the rules.</summary>
    public ConvertQuoteToSaleCommandValidator()
    {
        RuleFor(command => command.QuoteId).NotEmpty();
    }
}

/// <summary>Marks the accepted quote converted for the till path.</summary>
/// <param name="quotes">Quote lookup.</param>
public sealed class ConvertQuoteToSaleCommandHandler(
    IQuoteRepository quotes) : ICommandHandler<ConvertQuoteToSaleCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(ConvertQuoteToSaleCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Quote quote = await quotes.FindAsync(command.QuoteId, cancellationToken).ConfigureAwait(false)
            ?? throw new QuotesNotFoundException("quote", command.QuoteId);

        quote.MarkConverted();

        return quote.Id;
    }
}
