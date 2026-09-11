using FluentAssertions;
using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.CustomerAccounts;
using VumaRetail.Application.Conversations;
using VumaRetail.Domain.Conversations;
using VumaRetail.Domain.CustomerAccounts;
using VumaRetail.Domain.Primitives;
using VumaRetail.Application.Abstractions.Sales;
using VumaRetail.Domain.Sales.Invoices;

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

    [Fact]
    public async Task Every_document_intent_uses_the_same_verified_structural_scope()
    {
        Guid tenantId = Guid.NewGuid();
        Guid bindingId = Guid.NewGuid();
        Guid accountId = Guid.NewGuid();
        DateTimeOffset now = new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);
        ContactBinding binding = new(tenantId, "+27110000001", Guid.NewGuid(), ConversationChannel.WhatsApp);
        binding.Verify(now);
        binding.GrantConsent();
        Conversation conversation = new(tenantId, bindingId, ConversationChannel.WhatsApp, now);
        ConversationAccountScope scope = new(tenantId, bindingId, Guid.NewGuid(), accountId);
        CustomerAccount account = CustomerAccount.Open(tenantId, null, "ACT-2", Guid.NewGuid(), new Money(100m, "ZAR"), 30);

        IConversationScopeReader scopes = Substitute.For<IConversationScopeReader>();
        scopes.ListAsync(bindingId, Arg.Any<CancellationToken>()).Returns([scope]);
        ICustomerAccountRepository accounts = Substitute.For<ICustomerAccountRepository>();
        accounts.FindAsync(accountId, Arg.Any<CancellationToken>()).Returns(account);
        IContactBindingManagementService bindings = Substitute.For<IContactBindingManagementService>();
        bindings.FindAsync(bindingId, Arg.Any<CancellationToken>()).Returns(binding);
        IClock clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);
        DocumentDeliveryService delivery = new();
        IInvoiceRepository invoices = Substitute.For<IInvoiceRepository>();
        invoices.FindByNumberAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(
            Invoice.Create(tenantId, null, "INV-1", scope.OperatingCompanyId, "SO-1", InvoiceSourceType.SalesOrder, account.PartnerId, "ZAR"));

        IConversationIntentHandler[] handlers =
        [
            new InvoiceCopyIntentHandler(scopes, accounts, bindings, delivery, clock, invoices),
            new CreditNoteRequestIntentHandler(scopes, accounts, bindings, delivery, clock),
        ];

        foreach (IConversationIntentHandler handler in handlers)
        {
            IntentResult result = await handler.HandleAsync(
                conversation,
                new Dictionary<string, string> { ["reference"] = "ACT-2" },
                $"idem-{handler.Intent}");

            result.Facts.Single().Should().Contain("one-time delivery");
            result.ResultId.Should().NotBeEmpty();
        }

        await accounts.Received(2).FindAsync(accountId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pod_handler_fails_closed_until_stage_24_exists()
    {
        Func<Task> action = () => new PodIntentHandler(
            Substitute.For<IConversationScopeReader>(),
            Substitute.For<ICustomerAccountRepository>(),
            Substitute.For<IContactBindingManagementService>(),
            new DocumentDeliveryService(),
            Substitute.For<IClock>())
            .HandleAsync(
                new Conversation(Guid.NewGuid(), Guid.NewGuid(), ConversationChannel.WhatsApp, DateTimeOffset.UtcNow),
                new Dictionary<string, string>(),
                "pod-key");

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Proof of delivery is not available until Stage 24 is deployed.");
    }

    [Fact]
    public async Task Document_intent_refuses_when_the_binding_has_no_authorized_account()
    {
        Guid tenantId = Guid.NewGuid();
        Guid bindingId = Guid.NewGuid();
        DateTimeOffset now = new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);
        ContactBinding binding = new(tenantId, "+27110000002", Guid.NewGuid(), ConversationChannel.Email);
        binding.Verify(now);
        binding.GrantConsent();
        Conversation conversation = new(tenantId, bindingId, ConversationChannel.Email, now);
        ConversationAccountScope scope = new(tenantId, bindingId, Guid.NewGuid(), Guid.NewGuid());

        IConversationScopeReader scopes = Substitute.For<IConversationScopeReader>();
        scopes.ListAsync(bindingId, Arg.Any<CancellationToken>()).Returns([scope]);
        ICustomerAccountRepository accounts = Substitute.For<ICustomerAccountRepository>();
        accounts.FindAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((CustomerAccount?)null);
        IContactBindingManagementService bindings = Substitute.For<IContactBindingManagementService>();
        bindings.FindAsync(bindingId, Arg.Any<CancellationToken>()).Returns(binding);
        IClock clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);

        Func<Task> action = () => new InvoiceCopyIntentHandler(
            scopes, accounts, bindings, new DocumentDeliveryService(), clock, Substitute.For<IInvoiceRepository>())
            .HandleAsync(conversation, new Dictionary<string, string>(), "idem-no-account");

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("No customer account is authorized for this binding.");
    }

    [Fact]
    public async Task Place_order_is_only_a_confirmation_prompt_and_never_submits_an_order()
    {
        Guid bindingId = Guid.NewGuid();
        Conversation conversation = new(Guid.NewGuid(), bindingId, ConversationChannel.WhatsApp, DateTimeOffset.UtcNow);
        IConversationScopeReader scopes = Substitute.For<IConversationScopeReader>();
        scopes.ListAsync(bindingId, Arg.Any<CancellationToken>())
            .Returns([new ConversationAccountScope(conversation.TenantId, bindingId, Guid.NewGuid(), Guid.NewGuid())]);

        IntentResult result = await new PlaceOrderIntentHandler(scopes).HandleAsync(
            conversation,
            new Dictionary<string, string> { ["cartReference"] = "cart-7" },
            "idem-order");

        result.RequiresConfirmation.Should().BeTrue();
        result.Facts.Single().Should().Contain("No order has been submitted");
    }
}
