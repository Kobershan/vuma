using VumaRetail.Domain.Loyalty;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Crm;

/// <summary>
/// Consent state machine: per-type, per-customer, immediate withdrawal, expiry.
/// Scaffolding tests for Stage 19 (POPIA).
/// </summary>
public sealed class ConsentStateMachineTests
{
    private static readonly Guid CustomerId = Guid.NewGuid();

    private static readonly DateTimeOffset Now =
        new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NotAsked_goes_to_Given()
    {
        var consent = new Consent(Guid.NewGuid(), CustomerId, ConsentType.MarketingEmail);
        consent.State.Should().Be(ConsentState.NotAsked);
        consent.Give(Now);
        consent.State.Should().Be(ConsentState.Given);
        consent.GrantedAt.Should().Be(Now);
    }

    [Fact]
    public void Given_goes_to_Withdrawn()
    {
        var consent = new Consent(Guid.NewGuid(), CustomerId, ConsentType.MarketingEmail);
        consent.Give(Now);
        consent.Withdraw(Now.AddMinutes(1));
        consent.State.Should().Be(ConsentState.Withdrawn);
        consent.WithdrawnAt.Should().Be(Now.AddMinutes(1));
    }

    [Fact]
    public void Given_with_past_expiry_is_not_valid()
    {
        var consent = new Consent(Guid.NewGuid(), CustomerId, ConsentType.MarketingPush,
            expiresAt: Now.AddDays(-1));
        consent.Give(Now);
        consent.IsConsentValid(Now).Should().BeFalse();
    }

    [Fact]
    public void Given_with_future_expiry_is_valid()
    {
        var consent = new Consent(Guid.NewGuid(), CustomerId, ConsentType.DataProcessing,
            expiresAt: Now.AddDays(365));
        consent.Give(Now);
        consent.IsConsentValid(Now).Should().BeTrue();
    }

    [Fact]
    public void IsConsentValid_returns_false_for_Withdrawn()
    {
        var consent = new Consent(Guid.NewGuid(), CustomerId, ConsentType.MarketingEmail);
        consent.Give(Now);
        consent.Withdraw(Now.AddMinutes(1));
        consent.IsConsentValid(Now.AddMinutes(2)).Should().BeFalse();
    }

    [Fact]
    public void Duplicate_give_throws()
    {
        var consent = new Consent(Guid.NewGuid(), CustomerId, ConsentType.MarketingEmail);
        consent.Give(Now);
        Action act = () => consent.Give(Now.AddMinutes(1));
        act.Should().Throw<DuplicateConsentException>();
    }
}
