using VumaRetail.Application.Abstractions.CustomerAccounts;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Orders;
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
    public abstract ConversationIntent Intent { get; }
    protected abstract string ReferencePrefix { get; }
    protected abstract string EntityName { get; }

    public async Task<IntentResult> HandleAsync(
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
        int existingAccounts = 0;
        foreach (ConversationAccountScope scope in granted)
        {
            if (await accounts.FindAsync(scope.CustomerAccountId, cancellationToken).ConfigureAwait(false) is not null)
            {
                existingAccounts++;
            }
        }
        if (existingAccounts == 0)
        {
            throw new InvalidOperationException("No customer account is authorized for this binding.");
        }

        string reference = ResolveReference(entities, granted);
        DocumentDeliveryToken token = await delivery
            .MintAsync(binding, $"{ReferencePrefix}:{reference}", clock.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        return new IntentResult(
            token.Id,
            [$"Verified {EntityName} reference {reference} is ready for one-time delivery: {token.Token}."],
            IdempotencyKey: idempotencyKey);
    }

    private string ResolveReference(
        IReadOnlyDictionary<string, string> entities,
        IReadOnlyList<ConversationAccountScope> granted)
    {
        if (entities.TryGetValue("reference", out string? reference)
            || entities.TryGetValue("documentReference", out reference))
        {
            if (!string.IsNullOrWhiteSpace(reference)) return reference.Trim();
        }

        Guid accountId = granted[0].CustomerAccountId;
        return accountId.ToString("D");
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
    public override ConversationIntent Intent => ConversationIntent.RequestStatement;
    protected override string ReferencePrefix => "customer-account-statement";
    protected override string EntityName => "account statement";
}

/// <summary>Delivers an invoice copy through the verified document transport.</summary>
public sealed class InvoiceCopyIntentHandler(
    IConversationScopeReader scopes,
    ICustomerAccountRepository accounts,
    IContactBindingManagementService bindings,
    IDocumentDeliveryService delivery,
    IClock clock)
    : ScopedDocumentIntentHandler(scopes, accounts, bindings, delivery, clock)
{
    public override ConversationIntent Intent => ConversationIntent.RequestInvoiceCopy;
    protected override string ReferencePrefix => "invoice-copy";
    protected override string EntityName => "invoice";
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
    public override ConversationIntent Intent => ConversationIntent.RequestPod;
    protected override string ReferencePrefix => "proof-of-delivery";
    protected override string EntityName => "proof of delivery";
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
    public override ConversationIntent Intent => ConversationIntent.RequestCreditNote;
    protected override string ReferencePrefix => "credit-note";
    protected override string EntityName => "credit note";
}

/// <summary>Safe pre-submission boundary for order requests; creation occurs only after confirmation.</summary>
public sealed class PlaceOrderIntentHandler(IConversationScopeReader scopes) : IConversationIntentHandler
{
    public ConversationIntent Intent => ConversationIntent.PlaceOrder;

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
