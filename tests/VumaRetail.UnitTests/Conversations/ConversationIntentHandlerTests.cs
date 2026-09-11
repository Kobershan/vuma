using FluentAssertions;
using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.CustomerAccounts;
using VumaRetail.Application.Conversations;
using VumaRetail.Domain.Conversations;
using VumaRetail.Domain.CustomerAccounts;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Conversations;

public sealed class ConversationIntentHandlerTests
{
    [Fact]
    public async Task Statement_handler_requires_live_scope_and_mints_a_one_time_document_token()
    {
        Guid tenantId = Guid.NewGuid();
        Guid bindingId = Guid.NewGuid();
        Guid accountId = Guid.NewGuid();
        ContactBinding binding = new(tenantId, "+27110000000", Guid.NewGuid(), ConversationChannel.WhatsApp);
        binding.Verify(DateTimeOffset.UtcNow);
        binding.GrantConsent();
        Conversation conversation = new(tenantId, bindingId, ConversationChannel.WhatsApp, DateTimeOffset.UtcNow);
        ConversationAccountScope scope = new(tenantId, bindingId, Guid.NewGuid(), accountId);
        CustomerAccount account = CustomerAccount.Open(tenantId, null, "ACT-1", Guid.NewGuid(), new Money(1000m, "ZAR"), 30);

        IConversationScopeReader scopes = Substitute.For<IConversationScopeReader>();
        scopes.ListAsync(bindingId, Arg.Any<CancellationToken>()).Returns([scope]);
        ICustomerAccountRepository accounts = Substitute.For<ICustomerAccountRepository>();
        accounts.FindAsync(accountId, Arg.Any<CancellationToken>()).Returns(account);
        IContactBindingManagementService bindings = Substitute.For<IContactBindingManagementService>();
        bindings.FindAsync(bindingId, Arg.Any<CancellationToken>()).Returns(binding);
        IClock clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        DocumentDeliveryService delivery = new();

        IntentResult result = await new StatementIntentHandler(scopes, accounts, bindings, delivery, clock)
            .HandleAsync(conversation, new Dictionary<string, string> { ["reference"] = "ACT-1" }, "idem-1");

        result.Facts.Single().Should().Contain("one-time delivery");
        result.IdempotencyKey.Should().Be("idem-1");
    }
}
