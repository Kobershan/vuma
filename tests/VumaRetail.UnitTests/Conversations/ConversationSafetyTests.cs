using System.Security.Cryptography;
using System.Text;
using NSubstitute;
using VumaRetail.Application.Abstractions.CustomerAccounts;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Orders;
using VumaRetail.Domain.CustomerAccounts;
using VumaRetail.Domain.Orders;
using VumaRetail.Domain.Primitives;
using VumaRetail.Application.Conversations;
using VumaRetail.Domain.Conversations;

namespace VumaRetail.UnitTests.Conversations;

public sealed class ConversationSafetyTests
{
    [Fact]
    public void Reply_safety_rejects_a_number_not_present_in_api_facts()
    {
        ReplyFacts facts = new(["Balance is R100.00 as at 2026-09-11."], "Unable to provide the balance.");

        ReplySafety.ContainsOnlyApiFacts("Balance is R100.00 as at 2026-09-11.", facts).Should().BeTrue();
        ReplySafety.ContainsOnlyApiFacts("Balance is R999.00 as at 2026-09-11.", facts).Should().BeFalse();
    }

    [Fact]
    public async Task Template_composer_returns_the_fallback_if_a_fact_check_fails()
    {
        string reply = await new TemplateReplyComposer().ComposeAsync(
            new ReplyFacts(["Invoice 100 is ready."], "Please contact a human."));

        reply.Should().Be("Invoice 100 is ready.");
    }

    private static readonly DateTimeOffset At = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("please send my statement", ConversationIntent.RequestStatement)]
    [InlineData("where is my order", ConversationIntent.OrderStatus)]
    [InlineData("send the POD", ConversationIntent.RequestPod)]
    [InlineData("I need an invoice copy", ConversationIntent.RequestInvoiceCopy)]
    [InlineData("request a credit note", ConversationIntent.RequestCreditNote)]
    [InlineData("please send 4 maize bags", ConversationIntent.PlaceOrder)]
    public async Task Keyword_fallback_only_returns_allowlisted_intents(string text, ConversationIntent expected)
    {
        IntentClassification result = await new KeywordIntentClassifier().ClassifyAsync(text);
        Assert.Equal(expected, result.Intent);
        Assert.True(result.Confidence >= 0.7m);
    }

    [Fact]
    public async Task Keyword_classifier_extracts_order_number_for_status_queries()
    {
        IntentClassification result = await new KeywordIntentClassifier().ClassifyAsync("where is order ORD-123/4?");

        Assert.Equal(ConversationIntent.OrderStatus, result.Intent);
        Assert.Equal("ORD-123/4", result.Entities["orderNumber"]);
    }

    [Fact]
    public async Task Unknown_message_escalates_without_guessing()
    {
        var conversation = new Conversation(Guid.NewGuid(), Guid.NewGuid(), ConversationChannel.WhatsApp, At);
        await new ConversationStateMachine(new KeywordIntentClassifier()).HandleAsync(conversation, "tell me something else", At.AddMinutes(1));
        Assert.Equal(ConversationState.Escalated, conversation.State);
    }

    [Fact]
    public async Task Incomplete_conversation_times_out_to_human_escalation()
    {
        var conversation = new Conversation(Guid.NewGuid(), Guid.NewGuid(), ConversationChannel.WhatsApp, At);
        conversation.BeginVerification(ConversationIntent.RequestStatement, At);

        ConversationState state = await new ConversationStateMachine(new KeywordIntentClassifier())
            .HandleAsync(conversation, "still here", At.AddMinutes(30));

        Assert.Equal(ConversationState.Escalated, state);
    }

    [Fact]
    public void Otp_is_hashed_single_use_and_limited_to_three_attempts()
    {
        var binding = new ContactBinding(Guid.NewGuid(), "+27825550134", Guid.NewGuid(), ConversationChannel.WhatsApp);
        var service = new VerificationService();
        VerificationChallenge challenge = service.Issue(binding, "123456", At);
        Assert.False(service.Verify(binding, challenge, "000000", At));
        Assert.False(service.Verify(binding, challenge, "000000", At));
        Assert.False(service.Verify(binding, challenge, "000000", At));
        Assert.False(service.Verify(binding, challenge, "123456", At));
        Assert.NotEqual("123456", challenge.Hash);
        VerificationChallenge boundary = service.Issue(binding, "654321", At);
        Assert.False(service.Verify(binding, boundary, "654321", boundary.ExpiresAt));
    }

    [Fact]
    public async Task Document_delivery_requires_verified_binding_and_is_one_time()
    {
        var binding = new ContactBinding(Guid.NewGuid(), "customer@example.test", Guid.NewGuid(), ConversationChannel.Email);
        var service = new DocumentDeliveryService();
        Assert.Throws<InvalidOperationException>(() => service.Mint(binding, "invoice/123", At));
        Assert.Throws<ArgumentException>(() => new DocumentDeliveryToken(binding.TenantId, binding.Id, " ", At));
        binding.Verify(At);
        binding.GrantConsent();
        DocumentDeliveryToken token = service.Mint(binding, "invoice/123", At);
        Assert.True(service.TryFetch(token, At.AddMinutes(1)));
        Assert.False(service.TryFetch(token, At.AddMinutes(2)));
        Assert.Null(await service.FetchAsync(token.Token, At.AddMinutes(3)));
    }

    [Fact]
    public async Task Durable_document_delivery_delegates_fetch_to_atomic_store_consume()
    {
        var store = Substitute.For<IDocumentDeliveryTokenStore>();
        var binding = new ContactBinding(Guid.NewGuid(), "customer@example.test", Guid.NewGuid(), ConversationChannel.Email);
        binding.Verify(At);
        binding.GrantConsent();
        DocumentDeliveryToken token = new(binding.TenantId, binding.Id, "invoice/atomic", At);
        store.ConsumeAsync(token.Token, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(token);

        string? reference = await new DocumentDeliveryService(store).FetchAsync(token.Token, At.AddMinutes(1));

        Assert.Equal("invoice/atomic", reference);
        await store.Received(1).ConsumeAsync(token.Token, At.AddMinutes(1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task In_memory_document_delivery_rejects_a_token_from_another_tenant()
    {
        Guid ownerTenant = Guid.NewGuid();
        var owner = new ContactBinding(ownerTenant, "owner@example.test", Guid.NewGuid(), ConversationChannel.Email);
        owner.Verify(At);
        owner.GrantConsent();
        DocumentDeliveryToken token = new(ownerTenant, owner.Id, "invoice/tenant", At);
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(Guid.NewGuid());
        DocumentDeliveryService service = new(tenant: tenant);
        service.Mint(owner, token.DocumentReference, At);

        Assert.Null(await service.FetchAsync(token.Token, At.AddMinutes(1)));
        tenant.TenantId.Returns(ownerTenant);
        Assert.Null(await service.FetchAsync(token.Token, At.AddMinutes(1)));
    }

    [Fact]
    public void Sensitive_document_delivery_requires_verification_within_24_hours()
    {
        var binding = new ContactBinding(Guid.NewGuid(), "customer@example.test", Guid.NewGuid(), ConversationChannel.Email);
        binding.Verify(At.AddHours(-25));
        binding.GrantConsent();

        Assert.Throws<InvalidOperationException>(() => new DocumentDeliveryService().Mint(binding, "statement/123", At));
    }

    [Fact]
    public void Document_delivery_requires_explicit_consent_and_stops_after_withdrawal()
    {
        var binding = new ContactBinding(Guid.NewGuid(), "customer@example.test", Guid.NewGuid(), ConversationChannel.Email);
        binding.Verify(At);
        var service = new DocumentDeliveryService();

        Assert.Throws<InvalidOperationException>(() => service.Mint(binding, "invoice/123", At));

        binding.GrantConsent();
        service.Mint(binding, "invoice/123", At);
        binding.WithdrawConsent();

        Assert.Throws<InvalidOperationException>(() => service.Mint(binding, "invoice/124", At.AddMinutes(1)));
    }

    [Fact]
    public void WhatsApp_webhook_signature_rejects_tampering_and_missing_signatures()
    {
        const string body = "{\"text\":\"hello\"}";
        const string secret = "test-webhook-secret";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        string signature = "sha256=" + Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

        Assert.True(ConversationWebhookSecurity.Verify(body, signature, secret));
        Assert.False(ConversationWebhookSecurity.Verify(body + " ", signature, secret));
        Assert.False(ConversationWebhookSecurity.Verify(body, null, secret));
        Assert.False(ConversationWebhookSecurity.Verify(body, null, string.Empty));
    }

    [Fact]
    public async Task Universal_stop_escalates_the_conversation()
    {
        var conversation = new Conversation(Guid.NewGuid(), Guid.NewGuid(), ConversationChannel.Email, At);
        await new ConversationStateMachine(new KeywordIntentClassifier()).HandleAsync(conversation, "STOP", At);
        Assert.Equal(ConversationState.Escalated, conversation.State);
    }

    [Fact]
    public void Rate_limiter_separates_message_and_sensitive_document_budgets()
    {
        var limiter = new ConversationRateLimiter(messagesPerHour: 2, sensitiveRequestsPerDay: 1);
        Guid tenant = Guid.NewGuid();
        Assert.True(limiter.TryConsume(tenant, "+27825550134", ConversationIntent.OrderStatus, At));
        Assert.True(limiter.TryConsume(tenant, "+27825550134", ConversationIntent.RequestStatement, At));
        Assert.False(limiter.TryConsume(tenant, "+27825550134", ConversationIntent.RequestInvoiceCopy, At));
        Assert.True(limiter.TryConsume(tenant, "+27825550134", ConversationIntent.OrderStatus, At.AddHours(1)));
        Assert.False(limiter.TryConsume(tenant, "+27825550134", ConversationIntent.RequestInvoiceCopy, At.AddHours(1)));
        Assert.True(limiter.TryConsume(tenant, "+27825550134", ConversationIntent.RequestInvoiceCopy, At.AddDays(1)));
    }

    [Fact]
    public async Task Intent_router_rejects_unknown_intents_and_replays_idempotent_submissions()
    {
        var handler = new StubIntentHandler(ConversationIntent.OrderStatus);
        var router = new ConversationIntentRouter([handler]);
        var conversation = new Conversation(Guid.NewGuid(), Guid.NewGuid(), ConversationChannel.WhatsApp, At);
        var classification = new IntentClassification(ConversationIntent.OrderStatus, new Dictionary<string, string>(), 1m);
        IntentResult first = await router.RouteAsync(conversation, classification, "same-key");
        IntentResult second = await router.RouteAsync(conversation, classification, "same-key");
        Assert.Equal(first, second);
        Assert.Equal(1, handler.Calls);
        await Assert.ThrowsAsync<InvalidOperationException>(() => router.RouteAsync(
            conversation,
            new IntentClassification(ConversationIntent.RequestPod, new Dictionary<string, string>(), 1m),
            "other-key"));
    }

    [Fact]
    public async Task Order_status_handler_does_not_query_without_a_granted_scope()
    {
        var scopes = Substitute.For<IConversationScopeReader>();
        scopes.ListAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ConversationAccountScope>>([]));
        var accounts = Substitute.For<ICustomerAccountRepository>();
        var orders = Substitute.For<ISalesOrderRepository>();
        var company = Substitute.For<ICompanyContext>();
        var handler = new OrderStatusIntentHandler(scopes, accounts, orders, company);
        var conversation = new Conversation(Guid.NewGuid(), Guid.NewGuid(), ConversationChannel.WhatsApp, At);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(
            conversation,
            new Dictionary<string, string> { ["orderNumber"] = "ORD-1" },
            "status-key"));

        Assert.Empty(orders.ReceivedCalls());
    }

    [Fact]
    public async Task Order_status_handler_queries_only_the_granted_partner_scope()
    {
        Guid tenant = Guid.NewGuid();
        Guid binding = Guid.NewGuid();
        Guid companyId = Guid.NewGuid();
        Guid accountId = Guid.NewGuid();
        Guid partnerId = Guid.NewGuid();
        var scope = new ConversationAccountScope(tenant, binding, companyId, accountId);
        var scopes = Substitute.For<IConversationScopeReader>();
        scopes.ListAsync(binding, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ConversationAccountScope>>([scope]));
        var account = CustomerAccount.Open(tenant, null, "ACT-1", partnerId, new Money(1000m, "ZAR"), 30, companyId);
        var accounts = Substitute.For<ICustomerAccountRepository>();
        accounts.FindAsync(accountId, Arg.Any<CancellationToken>()).Returns(account);
        var orders = Substitute.For<ISalesOrderRepository>();
        var order = SalesOrder.Create(tenant, null, "ORD-1", partnerId, SalesChannel.Phone,
            OrderFulfilmentType.ClickAndCollect, Guid.NewGuid(), null, "ZAR", At, null);
        orders.FindByOrderNumberForPartnersAsync("ORD-1", Arg.Is<IReadOnlySet<Guid>>(ids => ids.SetEquals(new HashSet<Guid> { partnerId })), Arg.Any<CancellationToken>())
            .Returns(order);
        var company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(companyId);

        IntentResult result = await new OrderStatusIntentHandler(scopes, accounts, orders, company)
            .HandleAsync(new Conversation(tenant, binding, ConversationChannel.WhatsApp, At),
                new Dictionary<string, string> { ["orderNumber"] = "ORD-1" }, "status-key");

        result.Facts.Should().ContainSingle().Which.Should().Contain("ORD-1").And.Contain("Draft");
    }

    private sealed class StubIntentHandler(ConversationIntent intent) : IConversationIntentHandler
    {
        public ConversationIntent Intent { get; } = intent;
        public int Calls { get; private set; }
        public Task<IntentResult> HandleAsync(Conversation conversation, IReadOnlyDictionary<string, string> entities, string idempotencyKey, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new IntentResult(Guid.NewGuid(), ["API result"], IdempotencyKey: idempotencyKey));
        }
    }
}
