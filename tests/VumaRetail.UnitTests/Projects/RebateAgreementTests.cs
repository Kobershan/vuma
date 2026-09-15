using FluentAssertions;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Projects;

namespace VumaRetail.UnitTests.Projects;

public sealed class RebateAgreementTests
{
    [Fact]
    public void Active_agreement_calculates_rebate_above_threshold_with_currency_precision()
    {
        RebateAgreement agreement = RebateAgreement.Create(Guid.NewGuid(), null, Guid.NewGuid(),
            "SUP-REBATE-1", 2.5m, new Money(1000m, "ZAR"));

        agreement.Activate();

        agreement.Calculate(new Money(1234.56m, "ZAR")).Should().Be(new Money(30.86m, "ZAR"));
    }

    [Fact]
    public void Agreement_refuses_below_threshold_and_invalid_lifecycle_calculation()
    {
        RebateAgreement agreement = RebateAgreement.Create(Guid.NewGuid(), null, Guid.NewGuid(),
            "SUP-REBATE-2", 5m, new Money(1000m, "ZAR"));
        FluentActions.Invoking(() => agreement.Calculate(new Money(1500m, "ZAR")))
            .Should().Throw<InvalidOperationException>();
        agreement.Activate();
        agreement.Calculate(new Money(999.99m, "ZAR")).Should().Be(new Money(0m, "ZAR"));
        agreement.Reconcile();
        agreement.Calculate(new Money(1000m, "ZAR")).Should().Be(new Money(50m, "ZAR"));
    }
}
