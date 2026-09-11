using VumaRetail.Application.Abstractions.CustomerAccounts;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Orders;
using VumaRetail.Application.Abstractions.Sales;
using VumaRetail.Domain.Conversations;

namespace VumaRetail.Application.Conversations;

/// <summary>Executes the read-only order-status intent against the scoped order API.</summary>
public sealed class OrderStatusIntentHandler(
    IConversationScopeReader scopes,
    ICustomerAccountRepository accounts,
    ISalesOrderRepository orders,
    ICompanyContext company) : IConversationIntentHandler
{
    /// <inheritdoc />
    public ConversationIntent Intent => ConversationIntent.OrderStatus;

    /// <inheritdoc />
    public async Task<IntentResult> HandleAsync(
        Conversation conversation,
        IReadOnlyDictionary<string, string> entities,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        if (!entities.TryGetValue("orderNumber", out string? orderNumber)
            && !entities.TryGetValue("order_number", out orderNumber))
        {
            throw new InvalidOperationException("An order number is required to check order status.");
        }

        IReadOnlyList<ConversationAccountScope> granted = await scopes
            .ListAsync(conversation.ContactBindingId, cancellationToken).ConfigureAwait(false);
        Guid? activeCompany = company.CompanyId;
        IEnumerable<ConversationAccountScope> relevant = activeCompany is { } companyId
            ? granted.Where(scope => scope.OperatingCompanyId == companyId)
            : granted;

        HashSet<Guid> partnerIds = [];
        foreach (ConversationAccountScope scope in relevant)
        {
            VumaRetail.Domain.CustomerAccounts.CustomerAccount? account = await accounts
                .FindAsync(scope.CustomerAccountId, cancellationToken).ConfigureAwait(false);
            if (account is not null)
            {
                partnerIds.Add(account.PartnerId);
            }
        }

        if (partnerIds.Count == 0)
        {
            throw new InvalidOperationException("No customer account is authorized for this binding.");
        }

        VumaRetail.Domain.Orders.SalesOrder? order = await orders
            .FindByOrderNumberForPartnersAsync(orderNumber, partnerIds, cancellationToken)
            .ConfigureAwait(false);
        if (order is null)
        {
            throw new InvalidOperationException("Order not found.");
        }

        return new IntentResult(
            order.Id,
            [$"Order {order.OrderNumber} status: {order.Status}."],
            IdempotencyKey: idempotencyKey);
    }
}

/// <summary>Base for verified, account-scoped document requests.</summary>
public abstract class ScopedDocumentIntentHandler(
    IConversationScopeReader scopes,
    ICustomerAccountRepository accounts,
    IContactBindingManagementService bindings,
    IDocumentDeliveryService delivery,
    IClock clock) : IConversationIntentHandler
{
    /// <inheritdoc />
    public abstract ConversationIntent Intent { get; }
    /// <summary>Prefix used when minting the delivery reference.</summary>
    protected abstract string ReferencePrefix { get; }
    /// <summary>Human-readable document name used in the response.</summary>
    protected abstract string EntityName { get; }

    /// <inheritdoc />
    public virtual async Task<IntentResult> HandleAsync(
        Conversation conversation,
        IReadOnlyDictionary<string, string> entities,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        ContactBinding binding = await bindings.FindAsync(conversation.ContactBindingId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("The conversation binding no longer exists.");
        IReadOnlyList<ConversationAccountScope> granted = await scopes
            .ListAsync(conversation.ContactBindingId, cancellationToken).ConfigureAwait(false);
        List<VumaRetail.Domain.CustomerAccounts.CustomerAccount> authorizedAccounts = [];
        foreach (ConversationAccountScope scope in granted)
        {
            if (await accounts.FindAsync(scope.CustomerAccountId, cancellationToken).ConfigureAwait(false) is { } account)
            {
                authorizedAccounts.Add(account);
            }
        }
        if (authorizedAccounts.Count == 0)
        {
            throw new InvalidOperationException("No customer account is authorized for this binding.");
        }

        string reference = await ResolveReferenceAsync(entities, authorizedAccounts, cancellationToken).ConfigureAwait(false);
        DocumentDeliveryToken token = await delivery
            .MintAsync(binding, $"{ReferencePrefix}:{reference}", clock.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        return new IntentResult(
            token.Id,
            [$"Verified {EntityName} reference {reference} is ready for one-time delivery: {token.Token}."],
            IdempotencyKey: idempotencyKey);
    }

    /// <summary>Resolves and structurally validates the requested document against authorized accounts.</summary>
    protected virtual Task<string> ResolveReferenceAsync(
        IReadOnlyDictionary<string, string> entities,
        IReadOnlyList<VumaRetail.Domain.CustomerAccounts.CustomerAccount> authorizedAccounts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(authorizedAccounts);
        if (entities.TryGetValue("reference", out string? reference)
            || entities.TryGetValue("documentReference", out reference))
        {
            if (!string.IsNullOrWhiteSpace(reference)
                && authorizedAccounts.Any(account => string.Equals(account.AccountNumber, reference.Trim(), StringComparison.OrdinalIgnoreCase)
                    || string.Equals(account.Id.ToString("D"), reference.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                return Task.FromResult(reference.Trim());
            }
            throw new InvalidOperationException("The requested document is outside the authorized account scope.");
        }

        return Task.FromResult(authorizedAccounts[0].AccountNumber);
    }
}

/// <summary>Delivers an account statement through the verified document transport.</summary>
public sealed class StatementIntentHandler(
    IConversationScopeReader scopes,
    ICustomerAccountRepository accounts,
    IContactBindingManagementService bindings,
    IDocumentDeliveryService delivery,
    IClock clock)
    : ScopedDocumentIntentHandler(scopes, accounts, bindings, delivery, clock)
{
    /// <inheritdoc />
    public override ConversationIntent Intent => ConversationIntent.RequestStatement;
    /// <inheritdoc />
    protected override string ReferencePrefix => "customer-account-statement";
    /// <inheritdoc />
    protected override string EntityName => "account statement";
}

/// <summary>Delivers an invoice copy through the verified document transport.</summary>
public sealed class InvoiceCopyIntentHandler(
    IConversationScopeReader scopes,
    ICustomerAccountRepository accounts,
    IContactBindingManagementService bindings,
    IDocumentDeliveryService delivery,
    IClock clock,
    IInvoiceRepository invoices)
    : ScopedDocumentIntentHandler(scopes, accounts, bindings, delivery, clock)
{
    /// <inheritdoc />
    public override ConversationIntent Intent => ConversationIntent.RequestInvoiceCopy;
    /// <inheritdoc />
    protected override string ReferencePrefix => "invoice-copy";
    /// <inheritdoc />
    protected override string EntityName => "invoice";

    /// <summary>Resolves an invoice number to an existing, account-owned invoice id.</summary>
    protected override async Task<string> ResolveReferenceAsync(
        IReadOnlyDictionary<string, string> entities,
        IReadOnlyList<VumaRetail.Domain.CustomerAccounts.CustomerAccount> authorizedAccounts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(authorizedAccounts);
        if (!entities.TryGetValue("reference", out string? reference)
            && !entities.TryGetValue("documentReference", out reference))
        {
            throw new InvalidOperationException("An invoice number is required.");
        }

        VumaRetail.Domain.Sales.Invoices.Invoice? invoice = await invoices
            .FindByNumberAsync(reference.Trim(), cancellationToken).ConfigureAwait(false);
        if (invoice is null || !authorizedAccounts.Any(account => account.PartnerId == invoice.CustomerId))
        {
            throw new InvalidOperationException("The requested invoice is outside the authorized account scope or does not exist.");
        }

        return invoice.Id.ToString("D");
    }
}

/// <summary>Delivers proof of delivery through the verified document transport.</summary>
public sealed class PodIntentHandler(
    IConversationScopeReader scopes,
    ICustomerAccountRepository accounts,
    IContactBindingManagementService bindings,
    IDocumentDeliveryService delivery,
    IClock clock)
    : ScopedDocumentIntentHandler(scopes, accounts, bindings, delivery, clock)
{
    /// <inheritdoc />
    public override ConversationIntent Intent => ConversationIntent.RequestPod;
    /// <inheritdoc />
    protected override string ReferencePrefix => "proof-of-delivery";
    /// <inheritdoc />
    protected override string EntityName => "proof of delivery";

    /// <summary>Stage 24 owns POD records and rendering; fail closed until that dependency exists.</summary>
    public override Task<IntentResult> HandleAsync(
        Conversation conversation,
        IReadOnlyDictionary<string, string> entities,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Proof of delivery is not available until Stage 24 is deployed.");
}

/// <summary>Delivers a credit-note reference through the verified document transport.</summary>
public sealed class CreditNoteRequestIntentHandler(
    IConversationScopeReader scopes,
    ICustomerAccountRepository accounts,
    IContactBindingManagementService bindings,
    IDocumentDeliveryService delivery,
    IClock clock)
    : ScopedDocumentIntentHandler(scopes, accounts, bindings, delivery, clock)
{
    /// <inheritdoc />
    public override ConversationIntent Intent => ConversationIntent.RequestCreditNote;
    /// <inheritdoc />
    protected override string ReferencePrefix => "credit-note";
    /// <inheritdoc />
    protected override string EntityName => "credit note";
}

/// <summary>Safe pre-submission boundary for order requests; creation occurs only after confirmation.</summary>
public sealed class PlaceOrderIntentHandler(IConversationScopeReader scopes) : IConversationIntentHandler
{
    /// <inheritdoc />
    public ConversationIntent Intent => ConversationIntent.PlaceOrder;

    /// <inheritdoc />
    public async Task<IntentResult> HandleAsync(
        Conversation conversation,
        IReadOnlyDictionary<string, string> entities,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        if ((await scopes.ListAsync(conversation.ContactBindingId, cancellationToken).ConfigureAwait(false)).Count == 0)
        {
            throw new InvalidOperationException("No customer account is authorized for this binding.");
        }

        string cart = entities.TryGetValue("cartReference", out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim() : "the requested items";
        return new IntentResult(
            conversation.Id,
            [$"Please confirm placing {cart}. No order has been submitted."],
            RequiresConfirmation: true,
            IdempotencyKey: idempotencyKey);
    }
}
