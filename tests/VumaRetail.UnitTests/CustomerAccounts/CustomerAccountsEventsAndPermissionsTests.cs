using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.CustomerAccounts.Events;
using VumaRetail.Application.CustomerAccounts.Permissions;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.CustomerAccounts;

/// <summary>
/// The module's financial vocabulary and its permission surface: event types are spelled exactly
/// once (a typo'd event type posts nowhere, silently), and the catalogue carries exactly the
/// gated operations.
/// </summary>
public sealed class CustomerAccountsEventsAndPermissionsTests
{
    [Fact]
    public void Event_types_match_the_seeded_posting_rules()
    {
        Dictionary<string, Money> amounts = new() { ["Principal"] = new Money(100m, "ZAR") };
        var now = new DateTimeOffset(2026, 8, 16, 9, 30, 0, TimeSpan.Zero);

        new LayByDepositReceivedEvent(Guid.Empty, null, now, "LAY-1", amounts).EventType
            .Should().Be("layby.deposit.received");
        new LayByInstalmentReceivedEvent(Guid.Empty, null, now, "LAY-1", amounts).EventType
            .Should().Be("layby.instalment.received");
        new LayByCompletedEvent(Guid.Empty, null, now, "LAY-1", amounts).EventType
            .Should().Be("layby.completed");
        new LayByCancelledEvent(Guid.Empty, null, now, "LAY-1", amounts).EventType
            .Should().Be("layby.cancelled");
        new AccountInterestRaisedEvent(Guid.Empty, null, now, "ARINV-1", amounts).EventType
            .Should().Be("account.interest.raised");
        new AccountPaymentReceivedEvent(Guid.Empty, null, now, "ARREC-1", amounts).EventType
            .Should().Be("account.payment.received");
        new StokvelContributionReceivedEvent(Guid.Empty, null, now, "STK-1", amounts).EventType
            .Should().Be("stokvel.contribution.received");
        new StokvelBenefitAllocatedEvent(Guid.Empty, null, now, "STK-1", amounts).EventType
            .Should().Be("stokvel.benefit.allocated");
        new StokvelPayoutSettledEvent(Guid.Empty, null, now, "STK-1", amounts).EventType
            .Should().Be("stokvel.payout.settled");
    }

    [Fact]
    public void Events_compare_by_value_and_print_their_type()
    {
        Dictionary<string, Money> amounts = new() { ["Principal"] = new Money(100m, "ZAR") };
        var now = new DateTimeOffset(2026, 8, 16, 9, 30, 0, TimeSpan.Zero);

        (Func<IFinancialEvent> Build, string Type)[] cases =
        [
            (() => new LayByDepositReceivedEvent(Guid.Empty, null, now, "LAY-1", amounts), "layby.deposit.received"),
            (() => new LayByInstalmentReceivedEvent(Guid.Empty, null, now, "LAY-1", amounts), "layby.instalment.received"),
            (() => new LayByCompletedEvent(Guid.Empty, null, now, "LAY-1", amounts), "layby.completed"),
            (() => new LayByCancelledEvent(Guid.Empty, null, now, "LAY-1", amounts), "layby.cancelled"),
            (() => new AccountInterestRaisedEvent(Guid.Empty, null, now, "ARINV-1", amounts), "account.interest.raised"),
            (() => new AccountPaymentReceivedEvent(Guid.Empty, null, now, "ARREC-1", amounts), "account.payment.received"),
        ];

        foreach (var (build, type) in cases)
        {
            build().EventType.Should().Be(type);
            build().Should().Be(build());
        }

        new LayByDepositReceivedEvent(Guid.Empty, null, now, "LAY-1", amounts).Should().Be(
            new LayByDepositReceivedEvent(Guid.Empty, null, now, "LAY-1", amounts));
        new LayByCancelledEvent(Guid.Empty, null, now, "LAY-1", amounts).Should().NotBe(
            new LayByCompletedEvent(Guid.Empty, null, now, "LAY-1", amounts));
    }

    [Fact]
    public void Module_declares_five_permissions_and_a_non_core_manifest()
    {
        var permissions = new CustomerAccountsPermissions();

        permissions.Module.Should().Be("customeraccounts");
        permissions.Permissions.Should().HaveCount(5);
        permissions.Permissions.Select(p => p.Key.Value).Should().BeEquivalentTo(
            "customeraccounts.account.manage",
            "customeraccounts.account.view",
            "customeraccounts.layby.manage",
            "customeraccounts.stokvel.manage",
            "customeraccounts.stokvel.view");
        permissions.Permissions.Where(p => p.IsHighRisk).Should().HaveCount(3);

        var manifest = new CustomerAccountsModuleManifest();
        manifest.Module.Should().Be("customeraccounts");
        manifest.LicenceFlag.Should().Be("customeraccounts");
        manifest.IsCore.Should().BeFalse();
    }
}
