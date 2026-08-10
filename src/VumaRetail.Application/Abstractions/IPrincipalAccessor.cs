namespace VumaRetail.Application.Abstractions;

/// <summary>
/// Who is acting. The audit interceptor reads this to stamp every row; business code never does.
/// </summary>
/// <remarks>
/// Requirement R6 is a full audit trail — who changed what, when, from which terminal. The "who" and
/// the "which terminal" come from here, and they arrive at the persistence layer without any module
/// having to pass them down. Stage 02 replaces the default implementation with one backed by the
/// authenticated identity; until then it reports the system principal.
/// </remarks>
public interface IPrincipalAccessor
{
    /// <summary>
    /// The acting principal, in the form written to <c>created_by</c> and <c>updated_by</c>.
    /// </summary>
    /// <remarks>
    /// A stable identifier, not a display name — display names are edited and an audit trail that
    /// changes retrospectively is not one. Shaped <c>user:{id}</c>, <c>terminal:{id}</c> or
    /// <c>system:{component}</c>.
    /// </remarks>
    string Principal { get; }

    /// <summary>The terminal the action came from, or <c>null</c> for a server-side or background action.</summary>
    Guid? TerminalId { get; }

    /// <summary>
    /// True when this is the system acting rather than a person — a sync receiver, a scheduled job, a
    /// migration. Recorded on the audit entry so an unattended change is distinguishable from a
    /// deliberate one during an investigation.
    /// </summary>
    bool IsSystem { get; }
}
