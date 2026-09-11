 #pragma warning disable CS1591, IDE0011
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.HrManagement;

/// <summary>A tenant employee and their employment lifecycle.</summary>
[Replicated(ReplicationScope.Bidirectional, ConflictPolicy.CloudWins)]
public sealed class Employee : Entity
{
    private Employee(Guid tenantId, string number, string firstName, string lastName, DateTimeOffset hiredAt, EmploymentType type)
        : base(tenantId) { EmployeeNumber = number; FirstName = firstName; LastName = lastName; HiredAt = hiredAt; EmploymentType = type; }
    private Employee() { }

    public string EmployeeNumber { get; private set; } = string.Empty;
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public string? PreferredName { get; private set; }
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public DateTimeOffset HiredAt { get; private set; }
    public DateTimeOffset? TerminatedAt { get; private set; }
    public EmploymentType EmploymentType { get; private set; }
    public EmploymentStatus Status { get; private set; } = EmploymentStatus.Active;

    public static Employee Create(Guid tenantId, string employeeNumber, string firstName, string lastName, DateTimeOffset hiredAt, EmploymentType employmentType, string? preferredName = null, string? email = null, string? phone = null)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant is required.", nameof(tenantId));
        if (employmentType == EmploymentType.Unknown) throw new ArgumentException("Employment type is required.", nameof(employmentType));
        return new Employee(tenantId, Required(employeeNumber, nameof(employeeNumber)).ToUpperInvariant(), Required(firstName, nameof(firstName)), Required(lastName, nameof(lastName)), hiredAt, employmentType)
        { PreferredName = Optional(preferredName), Email = Optional(email), Phone = Optional(phone) };
    }

    public void UpdateDetails(string firstName, string lastName, string? preferredName, string? email, string? phone)
    { FirstName = Required(firstName, nameof(firstName)); LastName = Required(lastName, nameof(lastName)); PreferredName = Optional(preferredName); Email = Optional(email); Phone = Optional(phone); }
    public void Suspend() => Status = EmploymentStatus.Suspended;
    public void Activate() => Status = EmploymentStatus.Active;
    public void Terminate(DateTimeOffset at)
    { if (at < HiredAt) throw new ArgumentException("Termination cannot precede hiring.", nameof(at)); TerminatedAt = at; Status = EmploymentStatus.Terminated; }
    private static string Required(string value, string name) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.", name) : value.Trim();
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>How an employee is engaged.</summary>
public enum EmploymentType
{
    /// <summary>Unset.</summary>
    Unknown = 0,
    /// <summary>Ongoing employment.</summary>
    Permanent = 1,
    /// <summary>Defined term.</summary>
    FixedTerm = 2,
    /// <summary>On-demand employment.</summary>
    Casual = 3,
    /// <summary>Independent contractor.</summary>
    Contractor = 4,
}

/// <summary>Employee lifecycle state.</summary>
public enum EmploymentStatus
{
    /// <summary>Schedulable.</summary>
    Active = 1,
    /// <summary>Temporarily unavailable.</summary>
    Suspended = 2,
    /// <summary>Employment ended.</summary>
    Terminated = 3,
}
