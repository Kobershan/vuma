namespace VumaRetail.Application.Abstractions;

/// <summary>
/// Whether a command writes. This is the hook the read-only enforcement in Stage 04b hangs on.
/// </summary>
/// <remarks>
/// <para>
/// ADR-028: a lapsed subscription drops a tenant to read-only — full read, report, reprint and export;
/// no writes, including no sales. Stage 04b implements that as a <em>single interceptor</em> in the
/// command pipeline rather than as checks scattered through forty modules, and the interceptor needs
/// to know which commands write.
/// </para>
/// <para>
/// <see cref="Unclassified"/> is the zero value on purpose. If the default were
/// <see cref="ReadOnly"/>, a module author who forgot the attribute would ship a write that stays
/// permitted while the tenant is read-only — a silent hole in the commercial model. If the default
/// were <see cref="Write"/>, they would ship a query that gets blocked. Neither failure should be
/// reachable, so an unclassified command is a build break: the architecture test in
/// <c>VumaRetail.ArchitectureTests</c> fails, and CI is red.
/// </para>
/// <para>
/// The mechanism is here in Stage 00, before there is a single command to classify, because
/// retrofitting it across a finished system is expensive and easy to get incompletely right.
/// </para>
/// </remarks>
public enum SideEffect
{
    /// <summary>Nobody said. Not a valid state — the build fails on it.</summary>
    Unclassified = 0,

    /// <summary>Reads only. Permitted while the tenant is read-only.</summary>
    ReadOnly = 1,

    /// <summary>Changes state. Refused with <c>403 LICENCE_READ_ONLY</c> while the tenant is read-only.</summary>
    Write = 2,
}

/// <summary>
/// Declares whether a command writes, and whether it is one of the narrow exemptions that stay
/// permitted while a tenant is read-only.
/// </summary>
/// <remarks>
/// Mandatory on every <see cref="ICommand{TResult}"/> implementation.
/// </remarks>
/// <param name="effect">Whether the command writes.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class CommandSideEffectAttribute(SideEffect effect) : Attribute
{
    /// <summary>Whether the command writes.</summary>
    public SideEffect Effect { get; } = effect;

    /// <summary>
    /// Set only on the three carve-outs ADR-028 keeps writable in read-only: taking a payment or
    /// updating a payment method, flushing already-captured offline data, and running a backup.
    /// </summary>
    /// <remarks>
    /// Every command that sets this is a hole in the commercial lever, so the set is meant to stay at
    /// three and to be reviewable in one place. <c>licence-safety</c> checks it, and adding a fourth
    /// should require an ADR rather than a code review.
    /// </remarks>
    public ReadOnlyExemption Exemption { get; init; } = ReadOnlyExemption.None;
}

/// <summary>
/// The writes that survive read-only enforcement, per ADR-028.
/// </summary>
public enum ReadOnlyExemption
{
    /// <summary>Not exempt. Refused while read-only. This is almost every command.</summary>
    None = 0,

    /// <summary>
    /// Taking a payment or updating a stored payment method. A customer must always be able to pay from
    /// inside the product — locking the payment screen behind the lapse it is meant to cure is the
    /// failure mode ADR-029 exists to prevent.
    /// </summary>
    Payment = 1,

    /// <summary>
    /// Flushing data a terminal already captured before enforcement began. The sale happened; refusing
    /// to record it would destroy the tenant's books to make a billing point.
    /// </summary>
    OfflineFlush = 2,

    /// <summary>
    /// Backup. A tenant's data protection must never depend on their subscription status, and R4's
    /// restore path has to stay intact.
    /// </summary>
    Backup = 3,
}
