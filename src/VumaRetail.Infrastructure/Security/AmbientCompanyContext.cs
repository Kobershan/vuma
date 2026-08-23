using VumaRetail.Application.Abstractions.Registry;

namespace VumaRetail.Infrastructure.Security;

/// <summary>
/// The default <see cref="ICompanyContext"/>: scoped per request, per POS operation, per sync batch —
/// the same lifetime as <see cref="AmbientTenantContext"/>.
/// </summary>
/// <remarks>
/// Until Stage 06c's Part H seeds the existing single-tenant installation to its one company (and until
/// a later part resolves it per terminal or connection), this stays <see cref="Guid.Empty"/> for every
/// installation — a known, honest gap, not a silent one: every row stamped in the meantime carries
/// <c>company_id = 00000000-0000-0000-0000-000000000000</c>, which is visibly "not yet resolved" rather
/// than a value that looks plausible and is wrong.
/// </remarks>
public sealed class AmbientCompanyContext : ICompanyContext
{
    /// <inheritdoc />
    public Guid CompanyId { get; private set; }

    /// <inheritdoc />
    public void SetCompany(Guid companyId) => CompanyId = companyId;
}
