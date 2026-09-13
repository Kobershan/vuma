#pragma warning disable CS1591
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.HrManagement;

/// <summary>An auditable employee conduct case with one-way decision state.</summary>
[Replicated(ReplicationScope.Bidirectional, ConflictPolicy.CloudWins)]
public sealed class DisciplinaryCase : Entity
{
    private DisciplinaryCase(Guid tenantId, Guid employeeId, DateOnly incidentOn, string allegation,
        DateTimeOffset openedAt) : base(tenantId)
    {
        EmployeeId = employeeId;
        IncidentOn = incidentOn;
        Allegation = allegation.Trim();
        OpenedAt = openedAt;
    }

    private DisciplinaryCase() { }

    public Guid EmployeeId { get; private set; }
    public DateOnly IncidentOn { get; private set; }
    public string Allegation { get; private set; } = string.Empty;
    public DateTimeOffset OpenedAt { get; private set; }
    public DisciplinaryCaseStatus Status { get; private set; } = DisciplinaryCaseStatus.Open;
    public DateTimeOffset? InvestigatingAt { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public string? Decision { get; private set; }

    public static DisciplinaryCase Open(Guid tenantId, Guid employeeId, DateOnly incidentOn,
        string allegation, DateTimeOffset openedAt)
    {
        if (tenantId == Guid.Empty || employeeId == Guid.Empty)
        {
            throw new ArgumentException("Tenant and employee are required.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(allegation);
        return new DisciplinaryCase(tenantId, employeeId, incidentOn, allegation, openedAt);
    }

    public void StartInvestigation(DateTimeOffset at)
    {
        if (Status != DisciplinaryCaseStatus.Open)
        {
            throw new InvalidOperationException("Only an open disciplinary case can be investigated.");
        }
        if (at < OpenedAt)
        {
            throw new ArgumentException("Investigation cannot precede case opening.", nameof(at));
        }
        InvestigatingAt = at;
        Status = DisciplinaryCaseStatus.Investigating;
    }

    public void Decide(string decision, DateTimeOffset at)
    {
        if (Status != DisciplinaryCaseStatus.Investigating)
        {
            throw new InvalidOperationException("Only an investigated disciplinary case can be decided.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(decision);
        if (at < InvestigatingAt)
        {
            throw new ArgumentException("Decision cannot precede investigation.", nameof(at));
        }
        Decision = decision.Trim();
        DecidedAt = at;
        Status = DisciplinaryCaseStatus.Decided;
    }
}

public enum DisciplinaryCaseStatus
{
    Open = 1,
    Investigating = 2,
    Decided = 3,
}
