using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Abstractions.Sales;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Sales.Invoices;

namespace VumaRetail.Application.Sales.Commands.Invoices;

/// <summary>
/// Opens a draft invoice in one company's books from already-frozen lines. The lines arrive
/// snapshotted — price, tax, pack size — from the order, sale or quote being documented; this
/// command resolves nothing and re-derives nothing (ADR-074, ADR-075, ADR-112).
/// </summary>
/// <param name="CustomerId">The customer who owes.</param>
/// <param name="Currency">The ISO 4217 currency.</param>
/// <param name="SourceDocumentId">The order or sale being documented.</param>
/// <param name="SourceType">Which kind of document that is.</param>
/// <param name="Lines">The frozen lines. At least one.</param>
/// <param name="GroupDocumentRef">The split's shared reference, when this is one segment of N (ADR-102).</param>
/// <param name="CompanyId">The company, or <c>null</c> for the ambient acting company.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record CreateInvoiceCommand(
    Guid CustomerId,
    string Currency,
    Guid SourceDocumentId,
    InvoiceSourceType SourceType,
    IReadOnlyList<InvoiceLineInput> Lines,
    string? GroupDocumentRef = null,
    Guid? CompanyId = null) : ICommand<Guid>;

/// <summary>Rejects a malformed invoice command before it reaches the handler.</summary>
public sealed class CreateInvoiceCommandValidator : AbstractValidator<CreateInvoiceCommand>
{
    /// <summary>Builds the rules.</summary>
    public CreateInvoiceCommandValidator()
    {
        RuleFor(command => command.CustomerId).NotEmpty();
        RuleFor(command => command.Currency).NotEmpty().Length(3);
        RuleFor(command => command.SourceDocumentId).NotEmpty();
        RuleFor(command => command.SourceType).IsInEnum();
        RuleFor(command => command.Lines).NotEmpty();
        RuleFor(command => command.GroupDocumentRef).MaximumLength(64);
        RuleForEach(command => command.Lines).ChildRules(lines =>
        {
            lines.RuleFor(line => line.Quantity).GreaterThan(0m);
            lines.RuleFor(line => line.Uom).NotEmpty().MaximumLength(16);
            lines.RuleFor(line => line.UnitPrice).GreaterThanOrEqualTo(0m);
            lines.RuleFor(line => line.DiscountAmount).GreaterThanOrEqualTo(0m);
            lines.RuleFor(line => line.TaxAmount).GreaterThanOrEqualTo(0m);
            lines.RuleFor(line => line.PackSizeDescription).NotEmpty().MaximumLength(128);
            lines.RuleFor(line => line).Must(HaveExactlyOneItemOrVariant)
                .WithMessage("Exactly one of ItemId or ItemVariantId must be set.");
        });
    }

    private static bool HaveExactlyOneItemOrVariant(InvoiceLineInput line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return line.ItemId.HasValue != line.ItemVariantId.HasValue;
    }
}

/// <summary>Opens the draft in the acting company's database, drawing its number from <c>INV</c>.</summary>
/// <param name="invoices">Invoice insertion.</param>
/// <param name="tenant">The ambient tenant and store.</param>
/// <param name="company">The ambient acting company, when the command names none.</param>
/// <param name="numbers">ADR-065's gap-free document number sequence.</param>
public sealed class CreateInvoiceCommandHandler(
    IInvoiceRepository invoices,
    ITenantContext tenant,
    ICompanyContext company,
    IDocumentNumberSequence numbers) : ICommandHandler<CreateInvoiceCommand, Guid>
{
    /// <summary>The document number series an invoice number is drawn from.</summary>
    public const string InvoiceNumberSeries = "INV";

    /// <inheritdoc />
    public async Task<Guid> HandleAsync(CreateInvoiceCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (tenant.TenantId == Guid.Empty)
        {
            throw new InvalidOperationException("An invoice needs an authenticated tenant.");
        }

        Guid companyId = command.CompanyId ?? company.CompanyId ?? Guid.Empty;
        if (companyId == Guid.Empty)
        {
            throw new InvalidOperationException("An invoice needs its company.");
        }

        string invoiceNumber = await numbers.NextAsync(InvoiceNumberSeries, cancellationToken).ConfigureAwait(false);

        Invoice invoice = Invoice.Create(
            tenant.TenantId,
            tenant.StoreId,
            invoiceNumber,
            companyId,
            command.SourceDocumentId.ToString(),
            command.SourceType,
            command.CustomerId,
            command.Currency,
            command.GroupDocumentRef);

        foreach (InvoiceLineInput input in command.Lines)
        {
            invoice.AddLine(InvoiceLine.Create(
                tenant.TenantId,
                tenant.StoreId,
                invoice.Id,
                input.ItemId,
                input.ItemVariantId,
                input.Quantity,
                input.Uom,
                new Money(input.UnitPrice, command.Currency),
                new Money(input.DiscountAmount, command.Currency),
                new Money(input.TaxAmount, command.Currency),
                input.PackSizeDescription,
                command.Currency,
                input.PriceListId));
        }

        invoices.Add(invoice);

        return invoice.Id;
    }
}

/// <summary>Finalizes a draft: freezes it and posts it to the ledger. Irreversible.</summary>
/// <param name="InvoiceId">The draft invoice.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record FinalizeInvoiceCommand(Guid InvoiceId) : ICommand;

/// <summary>Rejects a malformed finalize command before it reaches the handler.</summary>
public sealed class FinalizeInvoiceCommandValidator : AbstractValidator<FinalizeInvoiceCommand>
{
    /// <summary>Builds the rules.</summary>
    public FinalizeInvoiceCommandValidator()
    {
        RuleFor(command => command.InvoiceId).NotEmpty();
    }
}

/// <summary>Posts the invoice and raises its financial event.</summary>
/// <param name="invoices">Invoice lookup.</param>
/// <param name="financialEvents">Where the posted invoice's financial event is raised.</param>
/// <param name="clock">The only source of time.</param>
public sealed class FinalizeInvoiceCommandHandler(
    IInvoiceRepository invoices,
    IInvoiceFinancialEventPublisher financialEvents,
    IClock clock) : ICommandHandler<FinalizeInvoiceCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(FinalizeInvoiceCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Invoice invoice = await invoices.FindAsync(command.InvoiceId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvoicesNotFoundException("invoice", command.InvoiceId);

        DateTimeOffset now = clock.UtcNow;
        invoice.Post(now);

        await financialEvents.PublishAsync(
            new InvoicePostedEvent(
                invoice.TenantId,
                invoice.StoreId,
                invoice.CompanyId ?? Guid.Empty,
                invoice.Id,
                invoice.InvoiceNumber,
                invoice.SourceDocumentType,
                invoice.Net,
                invoice.Tax,
                invoice.Gross,
                now),
            cancellationToken).ConfigureAwait(false);

        return Unit.Value;
    }
}

/// <summary>Abandons a draft invoice. Posted invoices are corrected via credit note, never here.</summary>
/// <param name="InvoiceId">The draft invoice.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record CancelInvoiceCommand(Guid InvoiceId) : ICommand;

/// <summary>Rejects a malformed cancel command before it reaches the handler.</summary>
public sealed class CancelInvoiceCommandValidator : AbstractValidator<CancelInvoiceCommand>
{
    /// <summary>Builds the rules.</summary>
    public CancelInvoiceCommandValidator()
    {
        RuleFor(command => command.InvoiceId).NotEmpty();
    }
}

/// <summary>Cancels the draft.</summary>
/// <param name="invoices">Invoice lookup.</param>
public sealed class CancelInvoiceCommandHandler(
    IInvoiceRepository invoices) : ICommandHandler<CancelInvoiceCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(CancelInvoiceCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Invoice invoice = await invoices.FindAsync(command.InvoiceId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvoicesNotFoundException("invoice", command.InvoiceId);

        invoice.Cancel();

        return Unit.Value;
    }
}

/// <summary>
/// Generates the invoices for one fulfilled order or sale: one invoice per supplying company,
/// each posted in its own company's database (ADR-102). Returns one id per company.
/// </summary>
/// <param name="SourceDocumentId">The fulfilled order or sale.</param>
/// <param name="SourceDocumentNumber">Its human-readable number, shared by every segment.</param>
/// <param name="SourceType">Which kind of document that is.</param>
/// <param name="OrderingCompanyId">The company the order was captured against. Links are checked from here.</param>
/// <param name="CustomerId">The customer who owes.</param>
/// <param name="Currency">The ISO 4217 currency.</param>
/// <param name="Segments">One segment per supplying company, from the sourcing outcome.</param>
/// <param name="GroupDocumentRef">The shared reference, when there is more than one segment.</param>
/// <param name="IdempotencyKey">Stable across retries of the same generation.</param>
/// <param name="InitiatedBy">Who asked, in audit-principal form.</param>
/// <param name="SettlementTerms">How the source order settles, inherited onto every segment (ADR-111).</param>
/// <remarks>
/// Thin by design, like the sourcing commit: the issuing service owns the cross-company saga,
/// and this handler resolves no company database at all — which is exactly what
/// <c>MultiCompanyGuardTests</c> asserts.
/// </remarks>
[CommandSideEffect(SideEffect.Write)]
public sealed record GenerateInvoicesFromOrderCommand(
    Guid SourceDocumentId,
    string SourceDocumentNumber,
    InvoiceSourceType SourceType,
    Guid OrderingCompanyId,
    Guid CustomerId,
    string Currency,
    IReadOnlyList<InvoiceCompanySegment> Segments,
    string? GroupDocumentRef,
    string IdempotencyKey,
    string InitiatedBy,
    string SettlementTerms = "Standard") : ICommand<IReadOnlyList<Guid>>;

/// <summary>Rejects a malformed generation command before it reaches the handler.</summary>
public sealed class GenerateInvoicesFromOrderCommandValidator : AbstractValidator<GenerateInvoicesFromOrderCommand>
{
    /// <summary>Builds the rules.</summary>
    public GenerateInvoicesFromOrderCommandValidator()
    {
        RuleFor(command => command.SourceDocumentId).NotEmpty();
        RuleFor(command => command.SourceDocumentNumber).NotEmpty().MaximumLength(32);
        RuleFor(command => command.SourceType).IsInEnum();
        RuleFor(command => command.OrderingCompanyId).NotEmpty();
        RuleFor(command => command.CustomerId).NotEmpty();
        RuleFor(command => command.Currency).NotEmpty().Length(3);
        RuleFor(command => command.Segments).NotEmpty();
        RuleFor(command => command.GroupDocumentRef).MaximumLength(64);
        RuleFor(command => command.IdempotencyKey).NotEmpty().MaximumLength(256);
        RuleFor(command => command.InitiatedBy).NotEmpty().MaximumLength(128);
        RuleFor(command => command.SettlementTerms).NotEmpty().MaximumLength(16);
    }
}

/// <summary>Generates through <see cref="IInvoiceIssuingService"/>.</summary>
/// <param name="issuing">Issues one posted invoice per company.</param>
/// <param name="tenant">The ambient tenant.</param>
public sealed class GenerateInvoicesFromOrderCommandHandler(
    IInvoiceIssuingService issuing,
    ITenantContext tenant) : ICommandHandler<GenerateInvoicesFromOrderCommand, IReadOnlyList<Guid>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> HandleAsync(
        GenerateInvoicesFromOrderCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (tenant.TenantId == Guid.Empty)
        {
            throw new InvalidOperationException("Invoice generation needs an authenticated tenant.");
        }

        IReadOnlyList<IssuedInvoice> issued = await issuing.IssueAsync(
            new InvoiceIssuingRequest(
                tenant.TenantId,
                command.OrderingCompanyId,
                command.SourceDocumentId,
                command.SourceDocumentNumber,
                command.SourceType,
                command.CustomerId,
                command.Currency,
                command.Segments,
                command.GroupDocumentRef,
                command.IdempotencyKey,
                command.InitiatedBy,
                command.SettlementTerms),
            cancellationToken).ConfigureAwait(false);

        return issued.Select(invoice => invoice.InvoiceId).ToList();
    }
}
