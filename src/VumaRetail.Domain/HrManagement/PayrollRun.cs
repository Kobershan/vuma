using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.HrManagement;

/// <summary>Lifecycle state of a payroll run.</summary>
public enum PayrollRunStatus
{
    /// <summary>Still editable.</summary>
    Draft,
    /// <summary>Closed and immutable.</summary>
    Finalized
}

/// <summary>Company-scoped payroll aggregate.</summary>
[Replicated(ReplicationScope.Bidirectional, ConflictPolicy.CloudWins)]
public sealed class PayrollRun : Entity
{
    private PayrollRun(Guid tenantId, Guid companyId, DateOnly from, DateOnly to, string currency, Guid requestId) : base(tenantId)
    {
        AssignCompany(companyId); From = from; To = to; Currency = currency.Trim().ToUpperInvariant(); RequestId = requestId; Status = PayrollRunStatus.Draft;
    }
    private PayrollRun() { }
    /// <inheritdoc />
    public DateOnly From { get; private set; }
    /// <inheritdoc />
    public DateOnly To { get; private set; }
    /// <inheritdoc />
    public string Currency { get; private set; } = string.Empty;
    /// <inheritdoc />
    public Guid RequestId { get; private set; }
    /// <inheritdoc />
    public PayrollRunStatus Status { get; private set; }
    /// <inheritdoc />
    public decimal GrossAmount { get; private set; }
    /// <inheritdoc />
    public decimal DeductionAmount { get; private set; }
    /// <inheritdoc />
    public decimal NetAmount { get; private set; }

    /// <summary>Creates a draft payroll run.</summary>
    public static PayrollRun Create(Guid tenantId, Guid companyId, DateOnly from, DateOnly to, string currency, Guid requestId)
    {
        if (tenantId == Guid.Empty || companyId == Guid.Empty || requestId == Guid.Empty) { throw new ArgumentException("Payroll identity is required."); }
        if (to < from) { throw new ArgumentException("Payroll period cannot end before it starts."); }
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        return new PayrollRun(tenantId, companyId, from, to, currency, requestId);
    }

    /// <summary>Adds an employee calculation while the run is editable.</summary>
    public void AddLine(PayrollLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (Status != PayrollRunStatus.Draft) { throw new InvalidOperationException("A finalized payroll run is immutable."); }
        if (line.TenantId != TenantId || line.CompanyId != CompanyId || line.Currency != Currency) { throw new InvalidOperationException("Payroll line is outside the run scope."); }
        GrossAmount += line.GrossAmount; DeductionAmount += line.DeductionAmount; NetAmount += line.NetAmount;
    }
    /// <summary>Closes the run permanently.</summary>
    public void FinalizeRun()
    { if (Status != PayrollRunStatus.Draft) { throw new InvalidOperationException("Payroll run is already finalized."); } Status = PayrollRunStatus.Finalized; }
}

/// <summary>Immutable payroll calculation for one employee.</summary>
[Replicated(ReplicationScope.Bidirectional, ConflictPolicy.CloudWins)]
public sealed class PayrollLine : Entity
{
    private PayrollLine(Guid tenantId, Guid companyId, Guid runId, Guid employeeId, decimal hours, decimal hourlyRate, decimal gross, decimal deductions, string currency) : base(tenantId)
    { AssignCompany(companyId); PayrollRunId = runId; EmployeeId = employeeId; Hours = hours; HourlyRate = hourlyRate; GrossAmount = gross; DeductionAmount = deductions; NetAmount = gross - deductions; Currency = currency.Trim().ToUpperInvariant(); }
    private PayrollLine() { }
    /// <inheritdoc />
    public Guid PayrollRunId { get; private set; }
    /// <inheritdoc />
    public Guid EmployeeId { get; private set; }
    /// <inheritdoc />
    public decimal Hours { get; private set; }
    /// <inheritdoc />
    public decimal HourlyRate { get; private set; }
    /// <inheritdoc />
    public decimal GrossAmount { get; private set; }
    /// <inheritdoc />
    public decimal DeductionAmount { get; private set; }
    /// <inheritdoc />
    public decimal NetAmount { get; private set; }
    /// <inheritdoc />
    public string Currency { get; private set; } = string.Empty;
    /// <summary>Creates a validated employee payroll calculation.</summary>
    public static PayrollLine Create(Guid tenantId, Guid companyId, Guid runId, Guid employeeId, decimal hours, decimal hourlyRate, decimal deductions, string currency)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        if (companyId == Guid.Empty || runId == Guid.Empty || employeeId == Guid.Empty || hours < 0m || hourlyRate < 0m || deductions < 0m) { throw new ArgumentException("Payroll line values are invalid."); }
        decimal gross = decimal.Round(hours * hourlyRate, 4, MidpointRounding.AwayFromZero);
        if (deductions > gross) { throw new ArgumentOutOfRangeException(nameof(deductions), "Deductions cannot exceed gross pay."); }
        return new PayrollLine(tenantId, companyId, runId, employeeId, decimal.Round(hours, 6), decimal.Round(hourlyRate, 4), gross, decimal.Round(deductions, 4), currency);
    }
}
