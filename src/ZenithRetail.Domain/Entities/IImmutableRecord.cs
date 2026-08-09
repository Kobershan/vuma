namespace ZenithRetail.Domain.Entities;

/// <summary>
/// Marks an entity that may be inserted and never changed or deleted afterwards.
/// </summary>
/// <remarks>
/// <para>
/// Two rules land here. <c>CLAUDE.md</c> §7 rule 7 and ADR-012: a posted financial document is
/// amended by a reversal, never by an edit. Requirement R6: the audit trail is immutable, because a
/// trail somebody can rewrite is not evidence of anything.
/// </para>
/// <para>
/// Enforcement is in the persistence layer, not in each entity. The <c>DbContext</c> refuses to save
/// an <see cref="IImmutableRecord"/> in the <c>Modified</c> or <c>Deleted</c> state, so the guarantee
/// holds for every entity that ever carries this marker without any of them writing a check. An
/// append-only ledger (ADR-005) is the same idea applied to a whole table.
/// </para>
/// </remarks>
public interface IImmutableRecord;
