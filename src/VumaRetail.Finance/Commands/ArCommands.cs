using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Domain.Finance;
using VumaRetail.Domain.Primitives;
using VumaRetail.Finance.Tax;

namespace VumaRetail.Finance.Commands;

/// <summary>One line of a customer invoice being drafted.</summary>
/// <param name="Description">What is being billed.</param>
/// <param name="Amount">
/// The amount as entered. Whether this is net or gross of tax depends entirely on
/// <paramref name="TaxCode"/>'s own configured treatment — the caller does not decide that.
/// </param>
/// <param name="TaxCode">The <see cref="TaxRule"/> code to apply, or empty for none.</param>
public sealed record ArInvoiceLineInput(string Description, decimal Amount, string TaxCode);

/// <summary>Opens a draft customer invoice with its lines.</summary>
/// <param name="PartnerId">The customer, by bare id (Stage 06 has not been built yet).</param>
/// <param name="InvoiceDate">The date on the invoice.</param>
/// <param name="DueDate">When payment is due.</param>
/// <param name="Currency">The ISO 4217 currency for the whole document.</param>
/// <param name="Lines">The invoice's lines.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record CreateArInvoiceCommand(
    Guid PartnerId,
    DateOnly InvoiceDate,
    DateOnly DueDate,
    string Currency,
    IReadOnlyList<ArInvoiceLineInput> Lines) : ICommand<Guid>;

/// <summary>Validates <see cref="CreateArInvoiceCommand"/>.</summary>
public sealed class CreateArInvoiceCommandValidator : AbstractValidator<CreateArInvoiceCommand>
{
    /// <summary>Builds the rules.</summary>
    public CreateArInvoiceCommandValidator()
    {
        RuleFor(command => command.PartnerId).NotEmpty();
        RuleFor(command => command.Currency).NotEmpty().Length(3);
        RuleFor(command => command.DueDate).GreaterThanOrEqualTo(command => command.InvoiceDate);
        RuleFor(command => command.Lines).NotEmpty();
        RuleForEach(command => command.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.Description).NotEmpty();
            line.RuleFor(l => l.Amount).GreaterThan(0);
        });
    }
}

/// <summary>Handles <see cref="CreateArInvoiceCommand"/>.</summary>
/// <param name="invoices">The AR sub-ledger.</param>
/// <param name="numbers">Issues the invoice number.</param>
/// <param name="taxEngine">Calculates tax from configured rules.</param>
/// <param name="tenant">The ambient tenant.</param>
public sealed class CreateArInvoiceCommandHandler(
    IArInvoiceRepository invoices, IDocumentNumberSequence numbers, TaxEngine taxEngine, ITenantContext tenant)
    : ICommandHandler<CreateArInvoiceCommand, Guid>
{
    /// <summary>The document-number series AR invoices draw from.</summary>
    public const string NumberSeries = "ARINV";

    /// <inheritdoc />
    /// <exception cref="TaxRuleNotFoundException">A line's tax code has no rule effective on the invoice date.</exception>
    public async Task<Guid> HandleAsync(CreateArInvoiceCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        string invoiceNumber = await numbers.NextAsync(NumberSeries, cancellationToken).ConfigureAwait(false);

        ArInvoice invoice = ArInvoice.Draft(
            tenant.TenantId,
            tenant.StoreId,
            PartnerId.From(command.PartnerId),
            invoiceNumber,
            command.InvoiceDate,
            command.DueDate,
            command.Currency);

        foreach (ArInvoiceLineInput line in command.Lines)
        {
            TaxCalculation tax = string.IsNullOrWhiteSpace(line.TaxCode)
                ? new TaxCalculation(string.Empty, new Money(line.Amount, command.Currency), Money.Zero(command.Currency), new Money(line.Amount, command.Currency), 0m)
                : await taxEngine.CalculateAsync(
                    line.TaxCode, new Money(line.Amount, command.Currency), command.InvoiceDate, cancellationToken)
                    .ConfigureAwait(false);

            invoice.AddLine(line.Description, tax.NetAmount, tax.TaxCode, tax.TaxAmount);
        }

        invoices.Add(invoice);

        return invoice.Id;
    }
}

/// <summary>Posts a draft customer invoice to the GL through the posting rules engine.</summary>
/// <param name="ArInvoiceId">The invoice to post.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record PostArInvoiceCommand(Guid ArInvoiceId) : ICommand;

/// <summary>Handles <see cref="PostArInvoiceCommand"/>.</summary>
/// <param name="invoices">The AR sub-ledger.</param>
/// <param name="poster">Where a financial event becomes a journal.</param>
/// <param name="tenant">The ambient tenant.</param>
/// <param name="clock">The only source of time.</param>
public sealed class PostArInvoiceCommandHandler(
    IArInvoiceRepository invoices, IFinancialEventPoster poster, ITenantContext tenant, IClock clock)
    : ICommandHandler<PostArInvoiceCommand, Unit>
{
    /// <summary>The event type <see cref="PostingRule"/> configuration matches against.</summary>
    public const string EventType = "ar.invoice.posted";

    /// <inheritdoc />
    /// <exception cref="FinanceDocumentNotFoundException">The invoice does not exist.</exception>
    /// <exception cref="DocumentNotDraftException">The invoice is not a draft.</exception>
    public async Task<Unit> HandleAsync(PostArInvoiceCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        ArInvoice invoice = await invoices.FindByIdAsync(command.ArInvoiceId, cancellationToken).ConfigureAwait(false)
            ?? throw new FinanceDocumentNotFoundException(nameof(ArInvoice), command.ArInvoiceId);

        Money net = invoice.Lines.Aggregate(Money.Zero(invoice.Currency), (sum, line) => sum + line.NetAmount);
        Money tax = invoice.Lines.Aggregate(Money.Zero(invoice.Currency), (sum, line) => sum + line.TaxAmount);
        Money gross = invoice.Lines.Aggregate(Money.Zero(invoice.Currency), (sum, line) => sum + line.GrossAmount);

        FinancialEvent financialEvent = new(
            EventType,
            tenant.TenantId,
            invoice.StoreId,
            clock.UtcNow,
            invoice.InvoiceNumber,
            new Dictionary<string, Money>(StringComparer.Ordinal) { ["Net"] = net, ["Tax"] = tax, ["Gross"] = gross });

        Guid journalId = await poster.PostAsync(financialEvent, cancellationToken).ConfigureAwait(false);

        invoice.Post(journalId);

        return Unit.Value;
    }
}

/// <summary>One allocation of a customer receipt against one invoice.</summary>
/// <param name="ArInvoiceId">The invoice.</param>
/// <param name="Amount">How much of the receipt is allocated to it.</param>
public sealed record ArReceiptAllocationInput(Guid ArInvoiceId, decimal Amount);

/// <summary>Records a payment received from a customer and allocates it against invoices.</summary>
/// <param name="PartnerId">The customer, by bare id.</param>
/// <param name="Currency">The receipt's currency.</param>
/// <param name="Allocations">Which invoices the receipt is allocated to, and how much each.</param>
/// <param name="BankAccountId">The bank account the funds landed in, if known.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record RecordArReceiptCommand(
    Guid PartnerId,
    string Currency,
    IReadOnlyList<ArReceiptAllocationInput> Allocations,
    Guid? BankAccountId = null) : ICommand<Guid>;

/// <summary>Validates <see cref="RecordArReceiptCommand"/>.</summary>
public sealed class RecordArReceiptCommandValidator : AbstractValidator<RecordArReceiptCommand>
{
    /// <summary>Builds the rules.</summary>
    public RecordArReceiptCommandValidator()
    {
        RuleFor(command => command.PartnerId).NotEmpty();
        RuleFor(command => command.Currency).NotEmpty().Length(3);
        RuleFor(command => command.Allocations).NotEmpty();
        RuleForEach(command => command.Allocations).ChildRules(allocation =>
            allocation.RuleFor(a => a.Amount).GreaterThan(0));
    }
}

/// <summary>Handles <see cref="RecordArReceiptCommand"/>.</summary>
/// <param name="receipts">Customer receipts.</param>
/// <param name="invoices">The AR sub-ledger.</param>
/// <param name="numbers">Issues the receipt number.</param>
/// <param name="poster">Where a financial event becomes a journal.</param>
/// <param name="tenant">The ambient tenant.</param>
/// <param name="clock">The only source of time.</param>
public sealed class RecordArReceiptCommandHandler(
    IArReceiptRepository receipts,
    IArInvoiceRepository invoices,
    IDocumentNumberSequence numbers,
    IFinancialEventPoster poster,
    ITenantContext tenant,
    IClock clock) : ICommandHandler<RecordArReceiptCommand, Guid>
{
    /// <summary>The document-number series receipts draw from.</summary>
    public const string NumberSeries = "ARREC";

    /// <summary>The event type <see cref="PostingRule"/> configuration matches against.</summary>
    public const string EventType = "ar.receipt.posted";

    /// <inheritdoc />
    /// <exception cref="FinanceDocumentNotFoundException">An allocation references an invoice that does not exist.</exception>
    /// <exception cref="OverAllocationException">An allocation exceeds an invoice's outstanding balance.</exception>
    public async Task<Guid> HandleAsync(RecordArReceiptCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        List<(Guid ArInvoiceId, Money Amount)> allocationTuples = [];
        Money total = Money.Zero(command.Currency);

        foreach (ArReceiptAllocationInput allocation in command.Allocations)
        {
            ArInvoice invoice = await invoices.FindByIdAsync(allocation.ArInvoiceId, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new FinanceDocumentNotFoundException(nameof(ArInvoice), allocation.ArInvoiceId);

            Money amount = new(allocation.Amount, command.Currency);
            invoice.Allocate(amount);

            allocationTuples.Add((allocation.ArInvoiceId, amount));
            total += amount;
        }

        string receiptNumber = await numbers.NextAsync(NumberSeries, cancellationToken).ConfigureAwait(false);

        FinancialEvent financialEvent = new(
            EventType,
            tenant.TenantId,
            tenant.StoreId,
            clock.UtcNow,
            receiptNumber,
            new Dictionary<string, Money>(StringComparer.Ordinal) { ["Amount"] = total });

        Guid journalId = await poster.PostAsync(financialEvent, cancellationToken).ConfigureAwait(false);

        ArReceipt receipt = ArReceipt.Record(
            tenant.TenantId,
            tenant.StoreId,
            PartnerId.From(command.PartnerId),
            receiptNumber,
            clock.UtcNow,
            total,
            journalId,
            allocationTuples,
            command.BankAccountId);

        receipts.Add(receipt);

        return receipt.Id;
    }
}
