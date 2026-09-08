using FluentValidation;
using VumaRetail.Application.CustomerAccounts.Commands.Accounts;
using VumaRetail.Application.CustomerAccounts.Commands.LayBy;
using VumaRetail.Application.CustomerAccounts.Commands.Scheduled;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.CustomerAccounts;

/// <summary>
/// Every command validates its own shape before a handler runs: bad input is refused at the edge
/// with a code, never halfway through a posting.
/// </summary>
public sealed class CustomerAccountsValidatorTests
{
    [Fact]
    public void Open_account_rejects_empty_partner_negative_limit_and_bad_terms()
    {
        var validator = new OpenCustomerAccountCommandValidator();

        validator.Validate(new OpenCustomerAccountCommand(
            Guid.Empty, 5000m, "ZAR", 30)).IsValid.Should().BeFalse();
        validator.Validate(new OpenCustomerAccountCommand(
            Guid.NewGuid(), 0m, "ZAR", 30)).IsValid.Should().BeFalse();
        validator.Validate(new OpenCustomerAccountCommand(
            Guid.NewGuid(), 5000m, "ZAR", 30)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Payment_needs_a_positive_amount_and_a_receipt_reference()
    {
        var validator = new RecordAccountPaymentCommandValidator();
        var allocations = new List<PaymentAllocationInput> { new(UuidV7.NewGuid(), 100m) };

        validator.Validate(new RecordAccountPaymentCommand(
            Guid.NewGuid(), 0m, "ZAR", "Till", "RCPT-1", allocations)).IsValid.Should().BeFalse();
        validator.Validate(new RecordAccountPaymentCommand(
            Guid.NewGuid(), 100m, "ZAR", "Till", string.Empty, allocations)).IsValid.Should().BeFalse();
        validator.Validate(new RecordAccountPaymentCommand(
            Guid.NewGuid(), 100m, "ZAR", "Till", "RCPT-1", [])).IsValid.Should().BeFalse();
        validator.Validate(new RecordAccountPaymentCommand(
            Guid.NewGuid(), 100m, "ZAR", "Till", "RCPT-1", allocations)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Holder_needs_a_person_a_name_and_a_positive_cap()
    {
        var validator = new AuthoriseHolderCommandValidator();

        validator.Validate(new AuthoriseHolderCommand(
            Guid.NewGuid(), Guid.Empty, "Lerato", 300m, "ZAR")).IsValid.Should().BeFalse();
        validator.Validate(new AuthoriseHolderCommand(
            Guid.NewGuid(), Guid.NewGuid(), "Lerato", 300m, "ZAR")).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Open_layby_needs_lines_a_deposit_a_term_and_a_location()
    {
        var validator = new OpenLayByAgreementCommandValidator();
        var lines = new List<LayByLineInput> { new(UuidV7.NewGuid(), null, 1m, "EA") };

        validator.Validate(new OpenLayByAgreementCommand(
            Guid.NewGuid(), "ZAR", [], 200m, "Till", 3, "LAYBY")).IsValid.Should().BeFalse();
        validator.Validate(new OpenLayByAgreementCommand(
            Guid.NewGuid(), "ZAR", lines, 0m, "Till", 3, "LAYBY")).IsValid.Should().BeFalse();
        validator.Validate(new OpenLayByAgreementCommand(
            Guid.NewGuid(), "ZAR", lines, 200m, "Till", 3, "LAYBY")).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Limit_hold_and_release_ids_must_not_be_empty()
    {
        new SetCreditLimitCommandValidator()
            .Validate(new SetCreditLimitCommand(Guid.Empty, 9000m, "ZAR")).IsValid.Should().BeFalse();
        new SetCreditLimitCommandValidator()
            .Validate(new SetCreditLimitCommand(UuidV7.NewGuid(), 9000m, "ZAR")).IsValid.Should().BeTrue();
        new PlaceAccountHoldCommandValidator()
            .Validate(new PlaceAccountHoldCommand(Guid.Empty, "Review.")).IsValid.Should().BeFalse();
        new PlaceAccountHoldCommandValidator()
            .Validate(new PlaceAccountHoldCommand(UuidV7.NewGuid(), "Review.")).IsValid.Should().BeTrue();
        new ReleaseAccountHoldCommandValidator()
            .Validate(new ReleaseAccountHoldCommand(Guid.Empty)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Instalment_complete_and_cancel_ids_must_not_be_empty()
    {
        new RecordLayByInstalmentCommandValidator()
            .Validate(new RecordLayByInstalmentCommand(
                Guid.Empty, 100m, "ZAR", "Till", "RCPT-1")).IsValid.Should().BeFalse();
        new CompleteLayByAgreementCommandValidator()
            .Validate(new CompleteLayByAgreementCommand(Guid.Empty)).IsValid.Should().BeFalse();
        new CancelLayByAgreementCommandValidator()
            .Validate(new CancelLayByAgreementCommand(UuidV7.NewGuid())).IsValid.Should().BeTrue();
    }
}
