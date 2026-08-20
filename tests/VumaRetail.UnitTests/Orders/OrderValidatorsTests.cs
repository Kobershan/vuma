using FluentValidation.Results;
using VumaRetail.Application.Orders.Commands;
using VumaRetail.Domain.Orders;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Orders;

/// <summary>
/// The FluentValidation rules guarding each command before it reaches its handler — the pipeline's
/// own slot 100, exercised directly here for the handful of commands the stage's other tests never
/// send a malformed instance of.
/// </summary>
public sealed class OrderValidatorsTests
{
    private static readonly Guid SomeId = UuidV7.NewGuid();

    [Fact]
    public void Cancel_order_line_requires_both_ids()
    {
        CancelOrderLineCommandValidator validator = new();

        ValidationResult valid = validator.Validate(new CancelOrderLineCommand(SomeId, SomeId));
        ValidationResult invalid = validator.Validate(new CancelOrderLineCommand(Guid.Empty, Guid.Empty));

        valid.IsValid.Should().BeTrue();
        invalid.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Cancel_order_requires_the_order_id_and_a_short_reason()
    {
        CancelOrderCommandValidator validator = new();

        ValidationResult valid = validator.Validate(new CancelOrderCommand(SomeId, "Changed their mind"));
        ValidationResult missingId = validator.Validate(new CancelOrderCommand(Guid.Empty, null));
        ValidationResult reasonTooLong = validator.Validate(new CancelOrderCommand(SomeId, new string('x', 501)));

        valid.IsValid.Should().BeTrue();
        missingId.IsValid.Should().BeFalse();
        reasonTooLong.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Record_settlement_requires_a_recognised_payment_status()
    {
        RecordOrderSettlementCommandValidator validator = new();

        ValidationResult valid = validator.Validate(new RecordOrderSettlementCommand(SomeId, OrderPaymentStatus.Paid, null, null));
        ValidationResult invalid = validator.Validate(new RecordOrderSettlementCommand(SomeId, (OrderPaymentStatus)999, null, null));

        valid.IsValid.Should().BeTrue();
        invalid.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Create_order_return_requires_a_reason()
    {
        CreateOrderReturnCommandValidator validator = new();

        ValidationResult valid = validator.Validate(new CreateOrderReturnCommand(SomeId, "Faulty item"));
        ValidationResult invalid = validator.Validate(new CreateOrderReturnCommand(SomeId, string.Empty));

        valid.IsValid.Should().BeTrue();
        invalid.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Add_order_return_line_requires_a_positive_quantity()
    {
        AddOrderReturnLineCommandValidator validator = new();

        ValidationResult valid = validator.Validate(new AddOrderReturnLineCommand(SomeId, SomeId, 1m));
        ValidationResult invalid = validator.Validate(new AddOrderReturnLineCommand(SomeId, SomeId, 0m));

        valid.IsValid.Should().BeTrue();
        invalid.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Complete_order_return_requires_the_return_id()
    {
        CompleteOrderReturnCommandValidator validator = new();

        ValidationResult valid = validator.Validate(new CompleteOrderReturnCommand(SomeId));
        ValidationResult invalid = validator.Validate(new CompleteOrderReturnCommand(Guid.Empty));

        valid.IsValid.Should().BeTrue();
        invalid.IsValid.Should().BeFalse();
    }
}
