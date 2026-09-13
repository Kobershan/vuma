using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using VumaRetail.Application.Ecommerce;
using VumaRetail.Domain.Ecommerce;

namespace VumaRetail.UnitTests.Ecommerce;

public sealed class EcommerceDomainTests
{
    private static readonly Guid TenantId = Guid.Parse("01900000-0000-7000-8000-000000000001");
    private static readonly Guid CompanyId = Guid.Parse("01900000-0000-7000-8000-000000000002");
    private static readonly Guid ChannelId = Guid.Parse("01900000-0000-7000-8000-000000000003");
    private static readonly Guid BasketId = Guid.Parse("01900000-0000-7000-8000-000000000004");

    [Fact]
    public void Checkout_expires_after_24_hours_and_cannot_be_decided_afterward()
    {
        DateTimeOffset created = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);
        CheckoutIntent intent = CheckoutIntent.Submit(TenantId, CompanyId, ChannelId, BasketId, "customer-1", "key-1", "fingerprint", created);

        intent.ExpiresAtUtc.Should().Be(created.AddHours(24));
        intent.Expire(created.AddHours(24));
        intent.Status.Should().Be(CheckoutIntentStatus.Expired);
        FluentActions.Invoking(() => intent.Confirm(created.AddHours(24))).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Checkout_decision_boundary_expires_pending_intent_without_a_background_job()
    {
        DateTimeOffset created = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);
        CheckoutIntent intent = CheckoutIntent.Submit(TenantId, CompanyId, ChannelId, BasketId, "customer-1", "key-1", "fingerprint", created);

        FluentActions.Invoking(() => intent.Reject("store offline", created.AddHours(24)))
            .Should().Throw<InvalidOperationException>();
        intent.Status.Should().Be(CheckoutIntentStatus.Expired);
        intent.DecidedAtUtc.Should().Be(created.AddHours(24));
    }

    [Fact]
    public void Payment_webhook_signature_is_constant_time_verified_and_tamper_safe()
    {
        const string body = "{\"eventId\":\"evt-1\"}";
        const string secret = "test-secret-with-sufficient-entropy";
        string signature = "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body)));

        PaymentWebhookSecurity.Verify(body, signature, secret).Should().BeTrue();
        PaymentWebhookSecurity.Verify(body + " ", signature, secret).Should().BeFalse();
        PaymentWebhookSecurity.Verify(body, signature, "wrong-secret").Should().BeFalse();
    }

    [Fact]
    public void Basket_line_keeps_server_authoritative_price_separate_from_browser_advisory_price()
    {
        CommerceBasketLine line = CommerceBasketLine.Add(TenantId, CompanyId, BasketId, Guid.NewGuid(),
            2m, 1m, 100m, "ZAR");

        line.AdvisoryUnitPrice.Should().Be(1m);
        line.AuthoritativeUnitPrice.Should().Be(100m);
    }

    [Theory]
    [InlineData(PaymentAttemptStatus.Authorised, PaymentAttemptStatus.Captured, true)]
    [InlineData(PaymentAttemptStatus.Captured, PaymentAttemptStatus.Authorised, false)]
    [InlineData(PaymentAttemptStatus.Failed, PaymentAttemptStatus.Captured, false)]
    [InlineData(PaymentAttemptStatus.Captured, PaymentAttemptStatus.Reversed, true)]
    public void Payment_status_transitions_are_monotonic(PaymentAttemptStatus current, PaymentAttemptStatus next, bool allowed)
        => PaymentAttempt.IsAllowedTransition(current, next).Should().Be(allowed);
}
