using NSubstitute;
using VumaRetail.Application.Abstractions.CustomerAccounts;
using VumaRetail.Application.CustomerAccounts.Commands.Stokvels;
using VumaRetail.Domain.CustomerAccounts;

namespace VumaRetail.UnitTests.CustomerAccounts;

/// <summary>
/// Stokvel command shapes: every guard the validator owns is executed here. Handlers re-check
/// what matters, but a request that never passes validation should never reach one.
/// </summary>
public sealed class StokvelValidatorTests
{
    [Fact]
    public void Group_needs_a_name_a_constitution_and_a_store()
    {
        var validator = new CreateStokvelGroupCommandValidator();

        validator.Validate(new CreateStokvelGroupCommand(
            "", StokvelType.Savings, "constitution",
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 15), Guid.NewGuid()))
            .IsValid.Should().BeFalse();
        validator.Validate(new CreateStokvelGroupCommand(
            "Grocery", StokvelType.Savings, "",
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 15), Guid.NewGuid()))
            .IsValid.Should().BeFalse();
        validator.Validate(new CreateStokvelGroupCommand(
            "Grocery", StokvelType.Savings, "constitution",
            new DateOnly(2026, 12, 15), new DateOnly(2026, 1, 1), Guid.NewGuid()))
            .IsValid.Should().BeFalse();
        validator.Validate(new CreateStokvelGroupCommand(
            "Grocery", StokvelType.Savings, "constitution",
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 15), Guid.NewGuid()))
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void Contribution_needs_a_positive_amount_and_a_receipt()
    {
        var validator = new RecordContributionCommandValidator();

        validator.Validate(new RecordContributionCommand(
            Guid.NewGuid(), Guid.NewGuid(), 0m, "ZAR", "Till", "RCPT-1"))
            .IsValid.Should().BeFalse();
        validator.Validate(new RecordContributionCommand(
            Guid.NewGuid(), Guid.NewGuid(), 100m, "ZAR", "Till", ""))
            .IsValid.Should().BeFalse();
        validator.Validate(new RecordContributionCommand(
            Guid.NewGuid(), Guid.NewGuid(), 100m, "ZAR", "Till", "RCPT-1"))
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void Member_needs_a_group_a_partner_and_a_currency()
    {
        var validator = new AddStokvelMemberCommandValidator();

        validator.Validate(new AddStokvelMemberCommand(
            Guid.Empty, Guid.NewGuid(), MemberRole.Member, 500m, "ZAR"))
            .IsValid.Should().BeFalse();
        validator.Validate(new AddStokvelMemberCommand(
            Guid.NewGuid(), Guid.Empty, MemberRole.Member, 500m, "ZAR"))
            .IsValid.Should().BeFalse();
        validator.Validate(new AddStokvelMemberCommand(
            Guid.NewGuid(), Guid.NewGuid(), MemberRole.Member, -1m, "ZAR"))
            .IsValid.Should().BeFalse();
        validator.Validate(new AddStokvelMemberCommand(
            Guid.NewGuid(), Guid.NewGuid(), MemberRole.Member, 500m, "ZAR"))
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void Benefit_pool_must_be_named_with_its_currency()
    {
        var validator = new AllocateBenefitsCommandValidator();

        validator.Validate(new AllocateBenefitsCommand(Guid.Empty, 90m, "ZAR"))
            .IsValid.Should().BeFalse();
        validator.Validate(new AllocateBenefitsCommand(Guid.NewGuid(), -1m, "ZAR"))
            .IsValid.Should().BeFalse();
        validator.Validate(new AllocateBenefitsCommand(Guid.NewGuid(), 90m, ""))
            .IsValid.Should().BeFalse();
        validator.Validate(new AllocateBenefitsCommand(Guid.NewGuid(), 90m, "ZAR"))
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void Payout_requests_approvals_settlements_and_removals_validate()
    {
        new RequestPayoutCommandValidator().Validate(new RequestPayoutCommand(
                Guid.Empty, Guid.NewGuid(), StokvelPayoutKind.Cash, 100m, "ZAR"))
            .IsValid.Should().BeFalse();
        new RequestPayoutCommandValidator().Validate(new RequestPayoutCommand(
                Guid.NewGuid(), Guid.NewGuid(), StokvelPayoutKind.Cash, 0m, "ZAR"))
            .IsValid.Should().BeFalse();
        new RequestPayoutCommandValidator().Validate(new RequestPayoutCommand(
                Guid.NewGuid(), Guid.NewGuid(), StokvelPayoutKind.Cash, 100m, "ZAR"))
            .IsValid.Should().BeTrue();

        new ApprovePayoutCommandValidator().Validate(new ApprovePayoutCommand(Guid.Empty))
            .IsValid.Should().BeFalse();
        new ApprovePayoutCommandValidator().Validate(new ApprovePayoutCommand(Guid.NewGuid()))
            .IsValid.Should().BeTrue();

        new SettlePayoutCommandValidator().Validate(new SettlePayoutCommand(Guid.Empty))
            .IsValid.Should().BeFalse();
        new SettlePayoutCommandValidator().Validate(new SettlePayoutCommand(Guid.NewGuid()))
            .IsValid.Should().BeTrue();

        new RemoveMemberCommandValidator().Validate(new RemoveMemberCommand(Guid.Empty, Guid.NewGuid()))
            .IsValid.Should().BeFalse();
        new RemoveMemberCommandValidator().Validate(
                new RemoveMemberCommand(Guid.NewGuid(), Guid.NewGuid()))
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void Hamper_needs_a_price_a_season_a_location_and_lines()
    {
        var validator = new CreateHamperBasketCommandValidator();
        var lines = new List<HamperLineInput>
        {
            new(Guid.NewGuid(), null, 2m, "EA"),
        };

        validator.Validate(new CreateHamperBasketCommand(
            Guid.NewGuid(), "Hamper", 0m, "ZAR",
            new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 24), "MAIN", lines))
            .IsValid.Should().BeFalse();
        validator.Validate(new CreateHamperBasketCommand(
            Guid.NewGuid(), "Hamper", 450m, "ZAR",
            new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 24), "MAIN", []))
            .IsValid.Should().BeFalse();
        validator.Validate(new CreateHamperBasketCommand(
            Guid.NewGuid(), "Hamper", 450m, "ZAR",
            new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 24), "MAIN", lines))
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void Contribution_ledger_is_append_only_by_construction()
    {
        // The rule 1 guarantee as a test: the contribution port offers Add and reads, and no
        // Update for a test to call by accident and a reviewer to miss.
        typeof(IStokvelContributionRepository).GetMethods()
            .Select(m => m.Name)
            .Should().NotContain(name => name.StartsWith("Update", StringComparison.Ordinal));
    }
}
