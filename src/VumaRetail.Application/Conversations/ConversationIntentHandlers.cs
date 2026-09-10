using VumaRetail.Application.Abstractions.CustomerAccounts;
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
