using FluentAssertions;
using VumaRetail.Domain.HrManagement;

namespace VumaRetail.UnitTests.Projects;

public sealed class PayrollRunTests
{
    [Fact]
    public void Payroll_run_totals_are_currency_safe_and_finalize_immutably()
    {
        Guid tenant = Guid.NewGuid(), company = Guid.NewGuid(), employee = Guid.NewGuid();
        PayrollRun run = PayrollRun.Create(tenant, company, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), "zar", Guid.NewGuid());
        PayrollLine line = PayrollLine.Create(tenant, company, run.Id, employee, 10.1234567m, 100.12345m, 50m, "ZAR");

        run.AddLine(line);
        run.GrossAmount.Should().Be(1013.5954m);
        run.DeductionAmount.Should().Be(50m);
        run.NetAmount.Should().Be(963.5954m);
        run.FinalizeRun();
        FluentActions.Invoking(() => run.AddLine(PayrollLine.Create(tenant, company, run.Id, Guid.NewGuid(), 1m, 1m, 0m, "ZAR")))
            .Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Payroll_line_rejects_deductions_above_gross()
    {
        FluentActions.Invoking(() => PayrollLine.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1m, 10m, 10.01m, "ZAR"))
            .Should().Throw<ArgumentOutOfRangeException>();
    }
}
