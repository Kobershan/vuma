using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Domain.Finance;
using VumaRetail.Domain.Primitives;
using VumaRetail.Finance.Tax;

namespace VumaRetail.Finance.Commands;

/// <summary>One line of a supplier invoice being captured.</summary>
/// <param name="Description">What was billed.</param>
/// <param name="Amount">
/// The amount as entered. Whether this is net or gross of tax depends entirely on
/// <paramref name="TaxCode"/>'s own configured treatment.
/// </param>
/// <param name="TaxCode">The <see cref="TaxRule"/> code to apply, or empty for none.</param>
public sealed record ApInvoiceLineInput(string Description, decimal Amount, string TaxCode);

/// <summary>Captures a draft supplier invoice with its lines.</summary>
/// <param name="PartnerId">The supplier, by bare id (Stage 06 has not been built yet).</param>
/// <param name="SupplierInvoiceNumber">The supplier's own invoice number.</param>
/// <param name="InvoiceDate">The date on the supplier's invoice.</param>
/// <param name="DueDate">When payment is due.</param>
/// <param name="Currency">The ISO 4217 currency for the whole document.</param>
/// <param name="Lines">The invoice's lines.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record CreateApInvoiceCommand(
    Guid PartnerId,
    string SupplierInvoiceNumber,
    DateOnly InvoiceDate,
    DateOnly DueDate,
    string Currency,
    IReadOnlyList<ApInvoiceLineInput> Lines) : ICommand<Guid>;

/// <summary>Validates <see cref="CreateApInvoiceCommand"/>.</summary>
public sealed class CreateApInvoiceCommandValidator : AbstractValidator<CreateApInvoiceCommand>
{
    /// <summary>Builds the rules.</summary>
    public CreateApInvoiceCommandValidator()
    {
        RuleFor(command => command.PartnerId).NotEmpty();
        RuleFor(command => command.SupplierInvoiceNumber).NotEmpty().MaximumLength(64);
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

/// <summary>Handles <see cref="CreateApInvoiceCommand"/>.</summary>
/// <param name="invoices">The AP sub-ledger.</param>
/// <param name="taxEngine">Calculates tax from configured rules.</param>
/// <param name="tenant">The ambient tenant.</param>
public sealed class CreateApInvoiceCommandHandler(
    IApInvoiceRepository invoices, TaxEngine taxEngine, ITenantContext tenant)
    : ICommandHandler<CreateApInvoiceCommand, Guid>
{
    /// <inheritdoc />
    /// <exception cref="TaxRuleNotFoundException">A line's tax code has no rule effective on the invoice date.</exception>
    public async Task<Guid> HandleAsync(CreateApInvoiceCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        ApInvoice invoice = ApInvoice.Draft(
            tenant.TenantId,
            tenant.StoreId,
            PartnerId.From(command.PartnerId),
            command.SupplierInvoiceNumber,
            command.InvoiceDate,
            command.DueDate,
            command.Currency);

        foreach (ApInvoiceLineInput line in command.Lines)
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

/// <summary>Posts a draft supplier invoice to the GL through the posting rules engine.</summary>
/// <param name="ApInvoiceId">The invoice to post.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record PostApInvoiceCommand(Guid ApInvoiceId) : ICommand;

/// <summary>Handles <see cref="PostApInvoiceCommand"/>.</summary>
/// <param name="invoices">The AP sub-ledger.</param>
/// <param name="poster">Where a financial event becomes a journal.</param>
/// <param name="tenant">The ambient tenant.</param>
/// <param name="clock">The only source of time.</param>
public sealed class PostApInvoiceCommandHandler(
    IApInvoiceRepository invoices, IFinancialEventPoster poster, ITenantContext tenant, IClock clock)
    : ICommandHandler<PostApInvoiceCommand, Unit>
{
    /// <summary>The event type <see cref="PostingRule"/> configuration matches against.</summary>
    public const string EventType = "ap.invoice.posted";

    /// <inheritdoc />
    /// <exception cref="FinanceDocumentNotFoundException">The invoice does not exist.</exception>
    /// <exception cref="DocumentNotDraftException">The invoice is not a draft.</exception>
    public async Task<Unit> HandleAsync(PostApInvoiceCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        ApInvoice invoice = await invoices.FindByIdAsync(command.ApInvoiceId, cancellationToken).ConfigureAwait(false)
            ?? throw new FinanceDocumentNotFoundException(nameof(ApInvoice), command.ApInvoiceId);

        Money net = invoice.Lines.Aggregate(Money.Zero(invoice.Currency), (sum, line) => sum + line.NetAmount);
        Money tax = invoice.Lines.Aggregate(Money.Zero(invoice.Currency), (sum, line) => sum + line.TaxAmount);
        Money gross = invoice.Lines.Aggregate(Money.Zero(invoice.Currency), (sum, line) => sum + line.GrossAmount);

        FinancialEvent financialEvent = new(
            EventType,
            tenant.TenantId,
            invoice.StoreId,
            clock.UtcNow,
            invoice.SupplierInvoiceNumber,
            new Dictionary<string, Money>(StringComparer.Ordinal) { ["Net"] = net, ["Tax"] = tax, ["Gross"] = gross });

        Guid journalId = await poster.PostAsync(financialEvent, cancellationToken).ConfigureAwait(false);

        invoice.Post(journalId);

        return Unit.Value;
    }
}

/// <summary>One allocation of a supplier payment against one invoice.</summary>
/// <param name="ApInvoiceId">The invoice.</param>
/// <param name="Amount">How much of the payment is allocated to it.</param>
public sealed record ApPaymentAllocationInput(Guid ApInvoiceId, decimal Amount);

/// <summary>Records a payment made to a supplier and allocates it against invoices.</summary>
/// <param name="PartnerId">The supplier, by bare id.</param>
/// <param name="Currency">The payment's currency.</param>
/// <param name="Allocations">Which invoices the payment is allocated to, and how much each.</param>
/// <param name="BankAccountId">The bank account the funds were paid from, if known.</param>
/// <remarks>
/// AP payment release is exactly the kind of write Stage 05's approval engine is meant to gate, per
/// <c>docs/stages/STAGE-07-finance.md</c>'s Scope note. Unconditional today for the same reason
/// <see cref="PostManualJournalCommand"/> is.
/// </remarks>
[CommandSideEffect(SideEffect.Write)]
public sealed record RecordApPaymentCommand(
    Guid PartnerId,
    string Currency,
    IReadOnlyList<ApPaymentAllocationInput> Allocations,
    Guid? BankAccountId = null) : ICommand<Guid>;

/// <summary>Validates <see cref="RecordApPaymentCommand"/>.</summary>
public sealed class RecordApPaymentCommandValidator : AbstractValidator<RecordApPaymentCommand>
{
    /// <summary>Builds the rules.</summary>
    public RecordApPaymentCommandValidator()
    {
        RuleFor(command => command.PartnerId).NotEmpty();
        RuleFor(command => command.Currency).NotEmpty().Length(3);
        RuleFor(command => command.Allocations).NotEmpty();
        RuleForEach(command => command.Allocations).ChildRules(allocation =>
            allocation.RuleFor(a => a.Amount).GreaterThan(0));
    }
}

/// <summary>Handles <see cref="RecordApPaymentCommand"/>.</summary>
/// <param name="payments">Supplier payments.</param>
/// <param name="invoices">The AP sub-ledger.</param>
/// <param name="numbers">Issues the payment number.</param>
/// <param name="poster">Where a financial event becomes a journal.</param>
/// <param name="tenant">The ambient tenant.</param>
/// <param name="clock">The only source of time.</param>
public sealed class RecordApPaymentCommandHandler(
    IApPaymentRepository payments,
    IApInvoiceRepository invoices,
    IDocumentNumberSequence numbers,
    IFinancialEventPoster poster,
    ITenantContext tenant,
    IClock clock) : ICommandHandler<RecordApPaymentCommand, Guid>
{
    /// <summary>The document-number series payments draw from.</summary>
    public const string NumberSeries = "APPAY";

    /// <summary>The event type <see cref="PostingRule"/> configuration matches against.</summary>
    public const string EventType = "ap.payment.posted";

    /// <inheritdoc />
    /// <exception cref="FinanceDocumentNotFoundException">An allocation references an invoice that does not exist.</exception>
    /// <exception cref="OverAllocationException">An allocation exceeds an invoice's outstanding balance.</exception>
    public async Task<Guid> HandleAsync(RecordApPaymentCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        List<(Guid ApInvoiceId, Money Amount)> allocationTuples = [];
        Money total = Money.Zero(command.Currency);

        foreach (ApPaymentAllocationInput allocation in command.Allocations)
        {
            ApInvoice invoice = await invoices.FindByIdAsync(allocation.ApInvoiceId, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new FinanceDocumentNotFoundException(nameof(ApInvoice), allocation.ApInvoiceId);

            Money amount = new(allocation.Amount, command.Currency);
            invoice.Allocate(amount);

            allocationTuples.Add((allocation.ApInvoiceId, amount));
            total += amount;
        }

        string paymentNumber = await numbers.NextAsync(NumberSeries, cancellationToken).ConfigureAwait(false);

        FinancialEvent financialEvent = new(
            EventType,
            tenant.TenantId,
            tenant.StoreId,
            clock.UtcNow,
            paymentNumber,
            new Dictionary<string, Money>(StringComparer.Ordinal) { ["Amount"] = total });

        Guid journalId = await poster.PostAsync(financialEvent, cancellationToken).ConfigureAwait(false);

        ApPayment payment = ApPayment.Record(
            tenant.TenantId,
            tenant.StoreId,
            PartnerId.From(command.PartnerId),
            paymentNumber,
            clock.UtcNow,
            total,
            journalId,
            allocationTuples,
            command.BankAccountId);

        payments.Add(payment);

        return payment.Id;
    }
}
