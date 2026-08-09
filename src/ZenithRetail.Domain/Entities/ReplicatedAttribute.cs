using ZenithRetail.Domain.Primitives;

namespace ZenithRetail.Domain.Entities;

/// <summary>
/// Which tiers of the terminal → store → cloud chain an entity replicates between.
/// </summary>
public enum ReplicationScope
{
    /// <summary>
    /// Never leaves the node that wrote it — a terminal's outbound queue, a local cache, a device log.
    /// Not the absence of a decision; the decision that this row is local.
    /// </summary>
    NodeLocal = 0,

    /// <summary>Terminal ↔ store server only. Trading data a till needs and the cloud does not.</summary>
    StoreLocal = 1,

    /// <summary>Store server → cloud, one way. Nearly all trading data: sales, stock, documents.</summary>
    StoreToCloud = 2,

    /// <summary>Cloud → store server, one way. Tenant configuration, pricing policy, licences.</summary>
    CloudToStore = 3,

    /// <summary>Changes originate at either tier and converge. The expensive case; use it deliberately.</summary>
    Bidirectional = 4,
}

/// <summary>
/// Declares how an entity replicates and how a divergence on it is settled.
/// </summary>
/// <remarks>
/// <para>
/// ADR-007 requires every replicated entity to name its <see cref="ConflictPolicy"/> explicitly,
/// because an entity whose conflict behaviour nobody chose is an entity that will lose somebody's
/// data quietly. This attribute is where that choice is recorded, next to the entity, in the same
/// diff that creates it.
/// </para>
/// <para>
/// It exists in Stage 01 rather than Stage 04 on purpose. Stage 04 builds the sync engine and reads
/// this registry — but Stages 02 and 03 create entities in between, and a registry that has to be
/// retrofitted across entities somebody else wrote is a registry with holes in it. Stage 04's
/// completeness test can then simply assert that every persisted entity carries one.
/// </para>
/// </remarks>
/// <param name="scope">Which tiers the entity replicates between.</param>
/// <param name="conflictPolicy">How a divergence is settled.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ReplicatedAttribute(ReplicationScope scope, ConflictPolicy conflictPolicy) : Attribute
{
    /// <summary>Which tiers the entity replicates between.</summary>
    public ReplicationScope Scope { get; } = scope;

    /// <summary>How a divergence between two nodes is settled (ADR-007).</summary>
    public ConflictPolicy ConflictPolicy { get; } = conflictPolicy;
}
