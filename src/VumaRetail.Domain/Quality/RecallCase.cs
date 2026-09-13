#pragma warning disable CS1591
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Quality;

public enum RecallCaseStatus
{
    Open,
    Closed,
}

public sealed record RecallTraceReference(string Kind, string Reference);

/// <summary>Company-scoped recall with explicit lot/serial and downstream trace references.</summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.AppendOnly)]
public sealed class RecallCase : Entity
{
    private readonly List<RecallTraceReference> _trace = [];

    private RecallCase(Guid tenantId, Guid? storeId, Guid companyId, Guid operationId, string caseNumber,
        string lotReference, string reason, DateTimeOffset openedAt) : base(tenantId, storeId)
    {
        AssignCompany(companyId);
        OperationId = operationId;
        CaseNumber = caseNumber;
        LotReference = lotReference;
        Reason = reason;
        OpenedAt = openedAt;
        Status = RecallCaseStatus.Open;
    }

    private RecallCase() { }

    public Guid OperationId { get; private set; }
    public string CaseNumber { get; private set; } = string.Empty;
    public string LotReference { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public RecallCaseStatus Status { get; private set; }
    public DateTimeOffset OpenedAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    public string? ClosureReason { get; private set; }
    public IReadOnlyList<RecallTraceReference> TraceReferences => _trace;

    public static RecallCase Open(Guid tenantId, Guid? storeId, Guid companyId, Guid operationId, string caseNumber,
        string lotReference, string reason, DateTimeOffset openedAt)
    {
        if (tenantId == Guid.Empty || companyId == Guid.Empty || operationId == Guid.Empty)
        {
            throw new ArgumentException("A recall requires tenant, company and operation identities.");
        }
        Require(caseNumber, 128, nameof(caseNumber));
        Require(lotReference, 128, nameof(lotReference));
        Require(reason, 2000, nameof(reason));
        return new RecallCase(tenantId, storeId, companyId, operationId, caseNumber.Trim(), lotReference.Trim(), reason.Trim(), openedAt);
    }

    public void AddTraceReference(string kind, string reference)
    {
        if (Status != RecallCaseStatus.Open)
        {
            throw new InvalidOperationException("Only an open recall can receive trace references.");
        }
        Require(kind, 32, nameof(kind));
        Require(reference, 256, nameof(reference));
        if (!_trace.Contains(new RecallTraceReference(kind.Trim(), reference.Trim())))
        {
            _trace.Add(new RecallTraceReference(kind.Trim(), reference.Trim()));
        }
    }

    public void Close(DateTimeOffset at, string reason)
    {
        if (Status != RecallCaseStatus.Open)
        {
            throw new InvalidOperationException("Only an open recall can be closed.");
        }
        Require(reason, 2000, nameof(reason));
        Status = RecallCaseStatus.Closed;
        ClosedAt = at;
        ClosureReason = reason.Trim();
    }

    private static void Require(string value, int max, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Trim().Length > max)
        {
            throw new ArgumentException($"Recall text must be {max} characters or fewer.", parameterName);
        }
    }
}
