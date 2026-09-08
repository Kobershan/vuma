using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Sales.Invoices;

/// <summary>Something invoice-owned was asked for that does not exist.</summary>
/// <param name="what">What was being looked for, for example <c>invoice</c>.</param>
/// <param name="id">The identifier that found nothing.</param>
public sealed class InvoicesNotFoundException(string what, Guid id)
    : DomainException("INVOICES_NOT_FOUND", $"No {what} with id {id}.", DomainProblemKind.NotFound);

/// <summary>An invoice business rule was broken.</summary>
/// <param name="code">The stable machine-readable code.</param>
/// <param name="message">What the rule says.</param>
public sealed class InvoicesRuleException(string code, string message) : DomainException(code, message)
{
    /// <summary>Something was done to an invoice that its status does not allow.</summary>
    /// <param name="actual">The status it is actually in.</param>
    public static InvoicesRuleException InvoiceNotDraft(InvoiceStatus actual)
        => new("INVOICE_NOT_DRAFT", $"This operation requires a draft invoice; the invoice is {actual}.");

    /// <summary>A posted invoice was written to. Posted means posted (ADR-012).</summary>
    public static InvoicesRuleException InvoiceAlreadyPosted()
        => new("INVOICE_ALREADY_POSTED", "This invoice has already been posted and is immutable.");

    /// <summary>An already-cancelled invoice was cancelled again.</summary>
    public static InvoicesRuleException InvoiceAlreadyCancelled()
        => new("INVOICE_ALREADY_CANCELLED", "This invoice has already been cancelled.");

    /// <summary>The requested invoice was not found in this company.</summary>
    public static InvoicesRuleException InvoiceNotFound()
        => new("INVOICE_NOT_FOUND", "The requested invoice was not found.");

    /// <summary>An invoice was posted with nothing on it.</summary>
    public static InvoicesRuleException InvoiceMustHaveLines()
        => new("INVOICE_NO_LINES", "An invoice must have at least one line.");

    /// <summary>An invoice line was built without its pack size snapshot (ADR-112).</summary>
    public static InvoicesRuleException PackSizeNotResolved()
        => new("INVOICE_PACK_SIZE_NOT_RESOLVED", "Could not resolve the pack size for this invoice line.");

    /// <summary>An invoice line named neither an item nor a variant, or named both.</summary>
    public static InvoicesRuleException ExactlyOneItemOrVariantRequired()
        => new("INVOICE_EXACTLY_ONE_ITEM_OR_VARIANT", "An invoice line identifies exactly one of an item or a variant.");

    /// <summary>A quantity that must be positive was zero or negative.</summary>
    public static InvoicesRuleException QuantityMustBePositive()
        => new("INVOICE_QUANTITY_MUST_BE_POSITIVE", "The quantity must be greater than zero.");

    /// <summary>A split produced no value in any company — there is nothing to invoice.</summary>
    public static InvoicesRuleException CannotSplitZeroAmount()
        => new("INVOICE_SPLIT_ZERO_AMOUNT", "Cannot split an order that produces zero total across companies.");

    /// <summary>An invoice was asked for in a company the caller may not see.</summary>
    /// <param name="companyId">The company.</param>
    public static InvoicesRuleException CompanyNotAuthorized(Guid companyId)
        => new("INVOICE_COMPANY_NOT_AUTHORIZED", $"The caller may not see invoices for company {companyId}.");

    /// <summary>Something was done to an invoice that its status does not allow.</summary>
    /// <param name="actual">The status it is actually in.</param>
    public static InvoicesRuleException UnexpectedInvoiceStatus(InvoiceStatus actual)
        => new("INVOICE_UNEXPECTED_STATUS", $"This operation cannot be performed while the invoice is {actual}.");
}
