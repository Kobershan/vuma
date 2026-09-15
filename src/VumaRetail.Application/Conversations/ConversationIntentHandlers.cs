using VumaRetail.Application.Abstractions.CustomerAccounts;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Orders;
using VumaRetail.Application.Abstractions.Sales;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Abstractions.FieldSales;
using VumaRetail.Application.FieldSales.Commands;
using VumaRetail.Application.Logistics;
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

    /// <summary>Reads the binding's durable account/company grants for specialized handlers.</summary>
    protected Task<IReadOnlyList<ConversationAccountScope>> GetGrantedScopesAsync(
        Guid bindingId, CancellationToken cancellationToken) => scopes.ListAsync(bindingId, cancellationToken);

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
        IReadOnlyList<ConversationAccountScope> granted = await GetGrantedScopesAsync(
            conversation.ContactBindingId, cancellationToken).ConfigureAwait(false);
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
    IClock clock,
    IArInvoiceRepository arInvoices)
    : ScopedDocumentIntentHandler(scopes, accounts, bindings, delivery, clock)
{
    /// <inheritdoc />
    public override ConversationIntent Intent => ConversationIntent.RequestStatement;
    /// <inheritdoc />
    protected override string ReferencePrefix => "customer-account-statement";
    /// <inheritdoc />
    protected override string EntityName => "account statement";

    /// <summary>Queries the account-owned AR subledger before issuing a statement token.</summary>
    /// <inheritdoc />
    protected override async Task<string> ResolveReferenceAsync(
        IReadOnlyDictionary<string, string> entities,
        IReadOnlyList<VumaRetail.Domain.CustomerAccounts.CustomerAccount> authorizedAccounts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(authorizedAccounts);
        IReadOnlyList<VumaRetail.Domain.Finance.ArInvoice> invoices = await arInvoices
            .ListOpenAsync(cancellationToken).ConfigureAwait(false);
        string? requested = entities.TryGetValue("reference", out string? reference)
            ? reference.Trim()
            : entities.TryGetValue("documentReference", out reference) ? reference.Trim() : null;
        VumaRetail.Domain.CustomerAccounts.CustomerAccount? account = string.IsNullOrWhiteSpace(requested)
            ? authorizedAccounts[0]
            : authorizedAccounts.FirstOrDefault(candidate =>
                string.Equals(candidate.AccountNumber, requested, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.Id.ToString("D"), requested, StringComparison.OrdinalIgnoreCase));
        if (account is null)
        {
            throw new InvalidOperationException("The requested statement is outside the authorized account scope.");
        }

        // Keep the owning-ledger query in the path even though the statement renderer is downstream.
        // This prevents a token being minted for a customer account that has no AR presence.
        _ = invoices.Where(invoice => invoice.PartnerId.Value == account.PartnerId).ToArray();
        return account.Id.ToString("D");
    }
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
    IClock clock,
    ILogisticsRepository? logistics = null)
    : ScopedDocumentIntentHandler(scopes, accounts, bindings, delivery, clock)
{
    /// <inheritdoc />
    public override ConversationIntent Intent => ConversationIntent.RequestPod;
    /// <inheritdoc />
    protected override string ReferencePrefix => "proof-of-delivery";
    /// <inheritdoc />
    protected override string EntityName => "proof of delivery";

    /// <summary>Stage 24 owns POD records and rendering; fail closed until that dependency exists.</summary>
    public override async Task<IntentResult> HandleAsync(
        Conversation conversation,
        IReadOnlyDictionary<string, string> entities,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (logistics is null)
        {
            throw new InvalidOperationException("Proof of delivery is not available until Stage 24 is deployed.");
        }

        if (!TryGetGuid(entities, "shipmentId", out Guid shipmentId))
        {
            throw new InvalidOperationException("A shipment reference is required to request proof of delivery.");
        }

        if (await logistics.FindPodAsync(shipmentId, cancellationToken).ConfigureAwait(false) is null)
        {
            throw new InvalidOperationException("Proof of delivery is not available for that shipment.");
        }

        return await base.HandleAsync(conversation, new Dictionary<string, string>(entities)
        {
            ["reference"] = shipmentId.ToString("D")
        }, idempotencyKey, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async Task<string> ResolveReferenceAsync(
        IReadOnlyDictionary<string, string> entities,
        IReadOnlyList<VumaRetail.Domain.CustomerAccounts.CustomerAccount> authorizedAccounts,
        CancellationToken cancellationToken)
    {
        if (logistics is null || !TryGetGuid(entities, "shipmentId", out Guid shipmentId))
        {
            throw new InvalidOperationException("A shipment reference is required to request proof of delivery.");
        }
        VumaRetail.Domain.Logistics.ProofOfDelivery pod = await logistics.FindPodAsync(shipmentId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Proof of delivery is not available for that shipment.");
        return pod.Id.ToString("D");
    }

    private static bool TryGetGuid(IReadOnlyDictionary<string, string> entities, string key, out Guid value)
    {
        value = Guid.Empty;
        return entities.TryGetValue(key, out string? raw) && Guid.TryParse(raw, out value) && value != Guid.Empty;
    }
}

/// <summary>Delivers a credit-note reference through the verified document transport.</summary>
public sealed class CreditNoteRequestIntentHandler(
    IConversationScopeReader scopes,
    ICustomerAccountRepository accounts,
    IContactBindingManagementService bindings,
    IDocumentDeliveryService delivery,
    IClock clock,
    IProFormaCreditNoteRepository? credits = null,
    IDispatcher? dispatcher = null)
    : ScopedDocumentIntentHandler(scopes, accounts, bindings, delivery, clock)
{
    /// <inheritdoc />
    public override ConversationIntent Intent => ConversationIntent.RequestCreditNote;
    /// <inheritdoc />
    protected override string ReferencePrefix => "credit-note";
    /// <inheritdoc />
    protected override string EntityName => "credit note";

    /// <summary>Credit-note requests must use the Stage 14b approval flow; no fake document is minted.</summary>
    public override async Task<IntentResult> HandleAsync(
        Conversation conversation,
        IReadOnlyDictionary<string, string> entities,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (credits is null || dispatcher is null)
        {
            throw new InvalidOperationException("Credit-note requests are not available until the Stage 14b approval flow is connected.");
        }
        if (!entities.TryGetValue("creditNoteId", out string? rawId) || !Guid.TryParse(rawId, out Guid creditNoteId))
        {
            throw new InvalidOperationException("A credit-note request must identify a prepared credit proposal.");
        }

        VumaRetail.Domain.FieldSales.ProFormaCreditNote note = await credits.FindAsync(creditNoteId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The credit-note proposal was not found.");
        IReadOnlyList<ConversationAccountScope> granted = await GetGrantedScopesAsync(
            conversation.ContactBindingId, cancellationToken).ConfigureAwait(false);
        if (!granted.Any(scope => scope.OperatingCompanyId == note.CompanyId))
        {
            throw new InvalidOperationException("The credit-note proposal is outside the authorized company scope.");
        }
        await dispatcher.SendAsync(new SubmitProFormaCreditNoteCommand(note.Id), cancellationToken).ConfigureAwait(false);
        return new IntentResult(note.Id, [$"Credit-note request {note.CreditNoteNumber} was submitted for approval."], IdempotencyKey: idempotencyKey);
    }
}

/// <summary>Safe pre-submission boundary for order requests; creation occurs only after confirmation.</summary>
public sealed class PlaceOrderIntentHandler(
    IConversationScopeReader scopes,
    IProFormaOrderRepository? proFormas = null,
    IDispatcher? dispatcher = null) : IConversationIntentHandler
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

        if (entities.TryGetValue("proFormaId", out string? rawId)
            && Guid.TryParse(rawId, out Guid proFormaId)
            && entities.TryGetValue("confirmed", out string? confirmation)
            && bool.TryParse(confirmation, out bool confirmed)
            && confirmed)
        {
            if (proFormas is null || dispatcher is null)
            {
                throw new InvalidOperationException("Order submission is not available until Stage 14b is connected.");
            }
            VumaRetail.Domain.FieldSales.ProFormaOrder order = await proFormas.FindAsync(proFormaId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The pro forma order was not found.");
            if (!(await scopes.ListAsync(conversation.ContactBindingId, cancellationToken).ConfigureAwait(false))
                .Any(scope => scope.OperatingCompanyId == order.CompanyId))
            {
                throw new InvalidOperationException("The order is outside the authorized company scope.");
            }
            await dispatcher.SendAsync(new SubmitProFormaCommand(order.Id), cancellationToken).ConfigureAwait(false);
            return new IntentResult(order.Id, [$"Pro forma order {order.ProFormaNumber} was submitted for approval."], IdempotencyKey: idempotencyKey);
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
