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

    [Fact]
    public void NotAsked_goes_to_Given()
    {
        var consent = new Consent(Guid.NewGuid(), CustomerId, ConsentType.MarketingEmail);
        consent.State.Should().Be(ConsentState.NotAsked);
        consent.Give();
        consent.State.Should().Be(ConsentState.Given);
        consent.GrantedAt.Should().NotBeNull();
    }

    [Fact]
    public void Given_goes_to_Withdrawn()
    {
        var consent = new Consent(Guid.NewGuid(), CustomerId, ConsentType.MarketingEmail);
        consent.Give();
        consent.Withdraw();
        consent.State.Should().Be(ConsentState.Withdrawn);
        consent.WithdrawnAt.Should().NotBeNull();
    }

    [Fact]
    public void Given_with_past_expiry_is_not_valid()
    {
        var consent = new Consent(Guid.NewGuid(), CustomerId, ConsentType.MarketingPush,
            expiresAt: DateTimeOffset.UtcNow.AddDays(-1));
        consent.Give();
        consent.IsConsentValid().Should().BeFalse();
    }

    [Fact]
    public void Given_with_future_expiry_is_valid()
    {
        var consent = new Consent(Guid.NewGuid(), CustomerId, ConsentType.DataProcessing,
            expiresAt: DateTimeOffset.UtcNow.AddDays(365));
        consent.Give();
        consent.IsConsentValid().Should().BeTrue();
    }

    [Fact]
    public void IsConsentValid_returns_false_for_Withdrawn()
    {
        var consent = new Consent(Guid.NewGuid(), CustomerId, ConsentType.MarketingEmail);
        consent.Give();
        consent.Withdraw();
        consent.IsConsentValid().Should().BeFalse();
    }

    [Fact]
    public void Duplicate_give_throws()
    {
        var consent = new Consent(Guid.NewGuid(), CustomerId, ConsentType.MarketingEmail);
        consent.Give();
        Action act = () => consent.Give();
        act.Should().Throw<DuplicateConsentException>();
    }
}
