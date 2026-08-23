namespace VumaRetail.Application.Abstractions.Registry;

/// <summary>
/// The company this database belongs to, and therefore the value stamped onto every row's
/// <c>CompanyId</c> column (ADR-140).
/// </summary>
/// <remarks>
/// <para>
/// Unlike <see cref="ITenantContext"/>, this drives no query filter. A single company database holds
/// exactly one company's rows by construction — the database boundary is the isolation (ADR-099) — so
/// there is nothing to filter and no bypass scope to guard. The only thing this port does is answer
/// "which company," the same way <c>IClock</c> answers "what time" for the persistence layer to stamp
/// with, never a decision a caller makes per row.
/// </para>
/// <para>
/// <see cref="Guid.Empty"/> before a company is registered against this database — the migration and
/// design-time path, and every installation until Stage 06c's Part H seeds the existing single-tenant
/// installation to its one company — mirroring <see cref="ITenantContext.TenantId"/>'s own convention
/// for "no tenant resolved yet."
/// </para>
/// </remarks>
public interface ICompanyContext
{
    /// <summary>The company this database belongs to, or <see cref="Guid.Empty"/> if not yet resolved.</summary>
    Guid CompanyId { get; }

    /// <summary>Sets the ambient company. Called at the request edge or the connection binding, not from a handler.</summary>
    /// <param name="companyId">The company this database belongs to.</param>
    void SetCompany(Guid companyId);
}
