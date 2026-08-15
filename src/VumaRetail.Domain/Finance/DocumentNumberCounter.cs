using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Finance;

/// <summary>
/// The next value to issue for one <c>IDocumentNumberSequence</c> series, per tenant.
/// </summary>
/// <remarks>
/// <para>
/// Node-local infrastructure state, not a business record: two nodes never need to agree on what
/// number a store already handed out, because the number was already printed on the document before
/// either node's copy of this counter could diverge. Replicating it would only invite exactly the
/// double-issue this type exists to prevent — see ADR-065.
/// </para>
/// <para>
/// <c>Infrastructure.Persistence.Repositories.DocumentNumberSequence</c> locks this row with
/// <c>SELECT ... FOR UPDATE</c> inside the pipeline's own transaction (slot 200), rather than opening
/// a second one — <c>CLAUDE.md</c> §7 rule 2 and the "SaveChanges only from the persistence layer"
/// architecture rule mean a repository cannot commit its own increment. Holding the row lock for the
/// rest of the command's transaction is what makes two concurrent postings in the same series
/// serialise instead of racing, and rolling the counter back with everything else on a failed command
/// is what keeps the series gap-free rather than merely unique.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.NodeLocal, ConflictPolicy.LastWriterWins)]
public sealed class DocumentNumberCounter : Entity
{
    private DocumentNumberCounter(Guid tenantId, string series)
        : base(tenantId)
    {
        Series = series;
        NextValue = 1;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private DocumentNumberCounter()
    {
    }

    /// <summary>The series this counter advances, for example <c>JNL</c> or <c>ARINV</c>.</summary>
    public string Series { get; private set; } = string.Empty;

    /// <summary>The next value this series will issue.</summary>
    public long NextValue { get; private set; }

    /// <summary>Starts a series at 1.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="series">The series name.</param>
    public static DocumentNumberCounter Start(Guid tenantId, string series)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(series);

        return new DocumentNumberCounter(tenantId, series.Trim().ToUpperInvariant());
    }

    /// <summary>Issues the next value and advances the counter.</summary>
    /// <returns>The value issued.</returns>
    public long Advance()
    {
        long issued = NextValue;
        NextValue++;
        return issued;
    }
}
