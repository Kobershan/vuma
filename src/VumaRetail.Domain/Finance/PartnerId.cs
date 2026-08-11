namespace VumaRetail.Domain.Finance;

/// <summary>
/// A bare, opaque reference to a counterparty — a customer for AR, a supplier for AP.
/// </summary>
/// <remarks>
/// <para>
/// Stage 06 (master data) owns customers and suppliers and has not been built yet; two other
/// sessions are building it and Stage 05 concurrently, each in its own worktree (see the Scope
/// note in <c>docs/stages/STAGE-07-finance.md</c>). Finance cannot wait for it, and must not guess
/// at its shape — so a partner is, from this module's point of view, nothing but an id. No name, no
/// contact detail, no credit terms live here. Resolving a <see cref="Value"/> to a display name is
/// Stage 06's read model, wired in when that stage lands, and it is wired in by Stage 06 adding a
/// lookup — this type does not change shape to accommodate it.
/// </para>
/// <para>
/// A distinct wrapper rather than a bare <see cref="Guid"/> so a handler cannot pass a store id or
/// an account id where a partner was meant, which the compiler cannot catch for two interchangeable
/// <c>Guid</c> parameters.
/// </para>
/// </remarks>
public readonly record struct PartnerId(Guid Value)
{
    /// <summary>Wraps an existing identifier, for example one carried on an inbound API request.</summary>
    /// <param name="value">The partner's id, as issued by whatever module owns it.</param>
    public static PartnerId From(Guid value) => new(value);

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
