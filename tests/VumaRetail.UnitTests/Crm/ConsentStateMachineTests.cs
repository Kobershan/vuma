using VumaRetail.Domain.Crm;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Crm;

/// <summary>
/// Consent state machine: per-type, per-customer, immediate withdrawal, expiry.
/// </summary>
public sealed class ConsentStateMachineTests
{
    private static readonly Guid CustomerId = Guid.NewGuid();
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NotAsked_goes_to_Given()
    {
        var consent = new Consent(Guid.NewGuid(), CompanyId, CustomerId, ConsentType.MarketingEmail);
        consent.State.Should().Be(ConsentState.NotAsked);
        consent.Give(Now, "signup-form", "user:operator");
        consent.State.Should().Be(ConsentState.Given);
        consent.GrantedAt.Should().Be(Now);
    }

    [Fact]
    public void Given_goes_to_Withdrawn()
    {
        var consent = new Consent(Guid.NewGuid(), CompanyId, CustomerId, ConsentType.MarketingEmail);
        consent.Give(Now, "signup-form", "user:operator");
        consent.Withdraw(Now.AddHours(1), "user:operator", "too many emails");
        consent.State.Should().Be(ConsentState.Withdrawn);
        consent.WithdrawnAt.Should().Be(Now.AddHours(1));
    }

    [Fact]
    public void Given_with_past_expiry_is_not_valid()
    {
        var consent = new Consent(
            Guid.NewGuid(), CompanyId, CustomerId, ConsentType.MarketingPush,
            expiresAt: Now.AddDays(-1));
        consent.Give(Now, "app-prompt", "user:operator");
        consent.IsValid(Now).Should().BeFalse();
    }

    [Fact]
    public void Given_with_future_expiry_is_valid()
    {
        var consent = new Consent(
            Guid.NewGuid(), CompanyId, CustomerId, ConsentType.DataProcessing,
            expiresAt: Now.AddDays(365));
        consent.Give(Now, "signup-form", "user:operator");
        consent.IsValid(Now).Should().BeTrue();
    }

    [Fact]
    public void IsValid_returns_false_for_Withdrawn()
    {
        var consent = new Consent(Guid.NewGuid(), CompanyId, CustomerId, ConsentType.MarketingEmail);
        consent.Give(Now, "signup-form", "user:operator");
        consent.Withdraw(Now.AddHours(1), "user:operator");
        consent.IsValid(Now.AddHours(2)).Should().BeFalse();
    }

    [Fact]
    public void Duplicate_give_throws()
    {
        var consent = new Consent(Guid.NewGuid(), CompanyId, CustomerId, ConsentType.MarketingEmail);
        consent.Give(Now, "signup-form", "user:operator");
        Action act = () => consent.Give(Now.AddMinutes(1), "signup-form", "user:operator");
        act.Should().Throw<DuplicateConsentException>();
    }

    [Fact]
    public void Reconsent_after_withdrawal_is_a_new_grant()
    {
        var consent = new Consent(Guid.NewGuid(), CompanyId, CustomerId, ConsentType.MarketingEmail);
        consent.Give(Now, "signup-form", "user:operator");
        consent.Withdraw(Now.AddHours(1), "user:operator");
        consent.Give(Now.AddDays(30), "repermission-campaign", "user:operator");
        consent.State.Should().Be(ConsentState.Given);
        consent.IsValid(Now.AddDays(30)).Should().BeTrue();
    }
}
