 #pragma warning disable CS1591, IDE0011
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.HrManagement;

/// <summary>Immutable employment terms snapshot.</summary>
[Replicated(ReplicationScope.Bidirectional, ConflictPolicy.AppendOnly)]
public sealed class EmploymentContract : Entity, IImmutableRecord
{
    private EmploymentContract(Guid tenantId, Guid employeeId, DateOnly startsOn, DateOnly? endsOn, decimal hourlyRate, string currency) : base(tenantId) { EmployeeId = employeeId; StartsOn = startsOn; EndsOn = endsOn; HourlyRate = hourlyRate; Currency = currency; }
    private EmploymentContract() { }
    public Guid EmployeeId { get; private set; }
    public DateOnly StartsOn { get; private set; }
    public DateOnly? EndsOn { get; private set; }
    public decimal HourlyRate { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public static EmploymentContract Create(Guid tenantId, Guid employeeId, DateOnly startsOn, DateOnly? endsOn, decimal hourlyRate, string currency)
    { if (tenantId == Guid.Empty || employeeId == Guid.Empty) throw new ArgumentException("Tenant and employee are required."); if (endsOn < startsOn) throw new ArgumentException("End cannot precede start.", nameof(endsOn)); if (hourlyRate < 0) throw new ArgumentOutOfRangeException(nameof(hourlyRate)); if (string.IsNullOrWhiteSpace(currency)) throw new ArgumentException("Currency is required.", nameof(currency)); return new EmploymentContract(tenantId, employeeId, startsOn, endsOn, hourlyRate, currency.Trim().ToUpperInvariant()); }
}
