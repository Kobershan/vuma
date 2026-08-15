using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.Abstractions.Finance;

/// <summary>
/// A financial fact a module raises to Stage 07's posting rules engine (CLAUDE.md §7 rule 12,
/// ADR-016).
/// </summary>
/// <remarks>
/// <para>
/// This is the whole of rule 12's contract, and it is deliberately incapable of naming a GL account:
/// there is no <c>AccountId</c>, no <c>DebitAccount</c>/<c>CreditAccount</c>, nothing an
/// implementer could set to point at one. A producer names an event type and a set of amounts;
/// which accounts those amounts land on, and on which side, is entirely the posting rule's decision
/// (<c>VumaRetail.Domain.Finance.PostingRule</c>). A module that wanted to name an account would
/// have nowhere on this type to put it — the rule is true by construction, not by convention, and
/// <c>FinanceRulesTests</c> checks the construction rather than the convention.
/// </para>
/// <para>
/// No producer module exists yet — POS is Stage 09, procurement is Stage 12. This contract and
/// <see cref="IFinancialEventPoster"/> are what they will implement against; Stage 07 proves the
/// mechanism with two realistic example event types in its own tests
/// (<c>docs/stages/STAGE-07-finance.md</c>).
/// </para>
/// </remarks>
public interface IFinancialEvent
{
    /// <summary>
    /// The event type, dotted and lower case, for example <c>ar.invoice.posted</c> or
    /// <c>pos.sale.tendered</c>. Matched against <c>PostingRule.EventType</c>.
    /// </summary>
    string EventType { get; }

    /// <summary>The tenant this event belongs to.</summary>
    Guid TenantId { get; }

    /// <summary>The store this event happened at, if any.</summary>
    Guid? StoreId { get; }

    /// <summary>When the underlying business event happened, UTC.</summary>
    DateTimeOffset OccurredAt { get; }

    /// <summary>A reference back to the document that raised this event — an invoice number, a sale id.</summary>
    string SourceReference { get; }

    /// <summary>
    /// The named amounts this event carries, for example <c>Net</c>, <c>Tax</c> and <c>Gross</c>. A
    /// posting rule's lines each draw their value from exactly one of these by key.
    /// </summary>
    IReadOnlyDictionary<string, Money> Amounts { get; }

    /// <summary>The department analysis dimension, bare opaque id, if the rule inherits it.</summary>
    Guid? DepartmentId { get; }

    /// <summary>The cost-centre analysis dimension, bare opaque id, if the rule inherits it.</summary>
    Guid? CostCentreId { get; }

    /// <summary>The project analysis dimension, bare opaque id, if the rule inherits it.</summary>
    Guid? ProjectId { get; }

    /// <summary>The channel analysis dimension, bare opaque id, if the rule inherits it.</summary>
    Guid? ChannelId { get; }

    /// <summary>The employee analysis dimension, bare opaque id, if the rule inherits it.</summary>
    Guid? EmployeeId { get; }
}

/// <summary>
/// A ready-to-post <see cref="IFinancialEvent"/>. Producer modules may use this directly, or
/// implement the interface on a type of their own — the engine only ever depends on the interface.
/// </summary>
/// <param name="EventType">The event type, matched against <c>PostingRule.EventType</c>.</param>
/// <param name="TenantId">The tenant this event belongs to.</param>
/// <param name="StoreId">The store this event happened at, if any.</param>
/// <param name="OccurredAt">When the underlying business event happened, UTC.</param>
/// <param name="SourceReference">A reference back to the document that raised this event.</param>
/// <param name="Amounts">The named amounts this event carries.</param>
/// <param name="DepartmentId">The department analysis dimension, bare opaque id.</param>
/// <param name="CostCentreId">The cost-centre analysis dimension, bare opaque id.</param>
/// <param name="ProjectId">The project analysis dimension, bare opaque id.</param>
/// <param name="ChannelId">The channel analysis dimension, bare opaque id.</param>
/// <param name="EmployeeId">The employee analysis dimension, bare opaque id.</param>
public sealed record FinancialEvent(
    string EventType,
    Guid TenantId,
    Guid? StoreId,
    DateTimeOffset OccurredAt,
    string SourceReference,
    IReadOnlyDictionary<string, Money> Amounts,
    Guid? DepartmentId = null,
    Guid? CostCentreId = null,
    Guid? ProjectId = null,
    Guid? ChannelId = null,
    Guid? EmployeeId = null) : IFinancialEvent;

/// <summary>
/// Turns a raised <see cref="IFinancialEvent"/> into a balanced, posted GL journal.
/// </summary>
/// <remarks>
/// Implemented by <c>VumaRetail.Finance</c>'s posting rules engine. This is the single door a
/// producer module goes through to affect the ledger — there is no other write path onto a GL
/// account from outside the Finance module.
/// </remarks>
public interface IFinancialEventPoster
{
    /// <summary>
    /// Posts the event, resolving the active posting rule for its event type into a balanced journal.
    /// </summary>
    /// <param name="financialEvent">The event to post.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The id of the journal that was posted.</returns>
    /// <exception cref="Domain.Finance.PostingRuleNotFoundException">
    /// No active <c>PostingRule</c> is configured for the event's type.
    /// </exception>
    Task<Guid> PostAsync(IFinancialEvent financialEvent, CancellationToken cancellationToken = default);
}

/// <summary>The result of applying a <c>TaxRule</c> to an amount.</summary>
/// <param name="TaxCode">The rule's code.</param>
/// <param name="NetAmount">The amount excluding tax.</param>
/// <param name="TaxAmount">The tax.</param>
/// <param name="GrossAmount">Net plus tax.</param>
/// <param name="Rate">The rate that was applied, as a fraction.</param>
public sealed record TaxCalculation(string TaxCode, Money NetAmount, Money TaxAmount, Money GrossAmount, decimal Rate);

/// <summary>
/// Works out net, tax and gross for an amount under the tenant's configured tax rules
/// (<c>CLAUDE.md</c> §9 — tax is a rules engine, never a constant).
/// </summary>
/// <remarks>
/// <para>
/// Implemented by <c>VumaRetail.Finance</c>'s <c>TaxEngine</c>. It sits here, beside
/// <see cref="IFinancialEventPoster"/>, for the same reason that one does: a module that sells
/// something has to price the tax on it, and <c>VumaRetail.Application</c> cannot reference
/// <c>VumaRetail.Finance</c> — the dependency runs the other way. Stage 09's POS is the first caller
/// and the reason this port exists; Stage 07's own AR and AP commands use the concrete engine
/// directly because they live inside the Finance assembly already.
/// </para>
/// <para>
/// Note what this port deliberately does not expose: no <c>TaxRule</c>, no rate table, no way to ask
/// "what is the standard rate". A caller names a tax code and a date and is told the three amounts.
/// Anything more would let a module cache a rate and quietly stop being a rules engine.
/// </para>
/// </remarks>
public interface ITaxCalculator
{
    /// <summary>
    /// Calculates net, tax and gross for a stated amount under the rule effective for a code on a date.
    /// </summary>
    /// <param name="taxCode">The tax code, for example <c>STANDARD</c>.</param>
    /// <param name="statedAmount">
    /// The amount as given. Whether it is treated as inclusive or exclusive of tax comes from the
    /// matched rule, not from the caller — which is the point: a South African shelf price is
    /// inclusive and a wholesale price list is not, and neither the till nor the caller should have to
    /// know which.
    /// </param>
    /// <param name="asOf">The date the rule must be effective on — normally the document date.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="Domain.Finance.TaxRuleNotFoundException">No active rule matches the code on that date.</exception>
    Task<TaxCalculation> CalculateAsync(
        string taxCode, Money statedAmount, DateOnly asOf, CancellationToken cancellationToken = default);
}
