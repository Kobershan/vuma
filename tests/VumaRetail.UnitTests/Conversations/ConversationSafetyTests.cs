using VumaRetail.Application.Conversations;
using VumaRetail.Domain.Conversations;

namespace VumaRetail.UnitTests.Conversations;

public sealed class ConversationSafetyTests
{
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
    public async Task Unknown_message_escalates_without_guessing()
    {
        var conversation = new Conversation(Guid.NewGuid(), Guid.NewGuid(), ConversationChannel.WhatsApp, At);
        await new ConversationStateMachine(new KeywordIntentClassifier()).HandleAsync(conversation, "tell me something else", At.AddMinutes(1));
        Assert.Equal(ConversationState.Escalated, conversation.State);
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
    }

    [Fact]
    public void Document_delivery_requires_verified_binding_and_is_one_time()
    {
        var binding = new ContactBinding(Guid.NewGuid(), "customer@example.test", Guid.NewGuid(), ConversationChannel.Email);
        var service = new DocumentDeliveryService();
        Assert.Throws<InvalidOperationException>(() => service.Mint(binding, "invoice/123", At));
        binding.Verify(At);
        DocumentDeliveryToken token = service.Mint(binding, "invoice/123", At);
        Assert.True(service.TryFetch(token, At.AddMinutes(1)));
        Assert.False(service.TryFetch(token, At.AddMinutes(2)));
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
}
