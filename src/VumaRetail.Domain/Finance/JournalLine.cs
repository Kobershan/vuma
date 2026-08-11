using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Finance;

/// <summary>
/// One debit or credit line of a posted <see cref="Journal"/>.
/// </summary>
/// <remarks>
/// Always created through <see cref="Journal.Post"/>, never on its own — a journal line with no
/// balanced counterpart is exactly the mistake the parent's balance check exists to prevent.
/// Analysis dimensions beyond store (carried on <see cref="Entity.StoreId"/> already) are bare
/// opaque ids: Stage 06 has not built department, cost centre, project, channel or employee yet, and
/// Finance must not guess at their shape.
/// </remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.AppendOnly)]
public sealed class JournalLine : Entity, IImmutableRecord
{
    private JournalLine(Guid tenantId, Guid? storeId)
        : base(tenantId, storeId)
    {
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private JournalLine()
    {
    }

    /// <summary>The journal this line belongs to.</summary>
    public Guid JournalId { get; private set; }

    /// <summary>The line's position within the journal, for stable ordering on a printed report.</summary>
    public int LineNumber { get; private set; }

    /// <summary>The GL account this line posts to.</summary>
    public Guid AccountId { get; private set; }

    /// <summary>The debit amount, or <c>null</c> if this line is a credit.</summary>
    public Money? Debit { get; private set; }

    /// <summary>The credit amount, or <c>null</c> if this line is a debit.</summary>
    public Money? Credit { get; private set; }

    /// <summary>A line-level narration.</summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>The department analysis dimension, bare opaque id.</summary>
    public Guid? DepartmentId { get; private set; }

    /// <summary>The cost-centre analysis dimension, bare opaque id.</summary>
    public Guid? CostCentreId { get; private set; }

    /// <summary>The project analysis dimension, bare opaque id.</summary>
    public Guid? ProjectId { get; private set; }

    /// <summary>The channel analysis dimension, bare opaque id.</summary>
    public Guid? ChannelId { get; private set; }

    /// <summary>The employee analysis dimension, bare opaque id.</summary>
    public Guid? EmployeeId { get; private set; }

    /// <summary>The signed amount in the account's currency: positive for a debit, negative for a credit.</summary>
    public decimal SignedAmount => Debit?.Amount ?? -Credit!.Value.Amount;

    /// <summary>Builds one line. Internal to the module — use <see cref="Journal.Post"/>.</summary>
    /// <exception cref="ArgumentException">Neither or both of debit and credit were supplied.</exception>
    internal static JournalLine Create(
        Guid tenantId,
        Guid? storeId,
        Guid journalId,
        int lineNumber,
        Guid accountId,
        Money? debit,
        Money? credit,
        string description,
        Guid? departmentId,
        Guid? costCentreId,
        Guid? projectId,
        Guid? channelId,
        Guid? employeeId)
    {
        if (debit is null && credit is null)
        {
            throw new ArgumentException("A journal line must carry a debit or a credit amount.");
        }

        if (debit is not null && credit is not null)
        {
            throw new ArgumentException("A journal line cannot carry both a debit and a credit amount.");
        }

        return new JournalLine(tenantId, storeId)
        {
            JournalId = journalId,
            LineNumber = lineNumber,
            AccountId = accountId,
            Debit = debit,
            Credit = credit,
            Description = description?.Trim() ?? string.Empty,
            DepartmentId = departmentId,
            CostCentreId = costCentreId,
            ProjectId = projectId,
            ChannelId = channelId,
            EmployeeId = employeeId,
        };
    }
}
