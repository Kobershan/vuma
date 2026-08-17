using VumaRetail.Application.Partners;
using VumaRetail.Domain.Partners;
using VumaRetail.Domain.Procurement;

namespace VumaRetail.Application.Procurement;

/// <summary>
/// The one check every procurement document makes about the partner it names: they exist, they are
/// this tenant's, they are a supplier, and they are still active.
/// </summary>
/// <remarks>
/// <para>
/// This is the application-layer half of <c>CONVENTIONS.md</c> §2. No table in <c>procurement</c> draws
/// a foreign key into <c>partners</c>, so nothing in the database stops an order naming a customer, a
/// deactivated supplier or a partner belonging to nobody — the reference is validated here instead. A
/// module that skipped this would produce an order that reads correctly and cannot be sent to anyone.
/// </para>
/// <para>
/// <b>Deactivated is refused, not warned about.</b> A partner is retired from new trading rather than
/// deleted (<c>Partner.Deactivate</c>), and the entire point of retiring one is that nobody places new
/// business with them. Existing orders against a supplier retired afterwards are untouched — they are
/// history, and history is not restated.
/// </para>
/// </remarks>
internal static class ProcurementPartners
{
    /// <summary>Resolves a supplier, refusing anything that is not one.</summary>
    /// <param name="partners">The partner repository.</param>
    /// <param name="partnerId">The partner.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The supplier.</returns>
    /// <exception cref="ProcurementNotFoundException">No such partner in this tenant.</exception>
    /// <exception cref="ProcurementRuleException">The partner is not an active supplier.</exception>
    internal static async Task<Partner> RequireSupplierAsync(
        IPartnerRepository partners, Guid partnerId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(partners);

        Partner partner = await partners.FindAsync(partnerId, cancellationToken).ConfigureAwait(false)
            ?? throw new ProcurementNotFoundException("supplier", partnerId);

        if (!partner.Type.HasFlag(PartnerType.Supplier))
        {
            throw new ProcurementRuleException(
                "PROCUREMENT_PARTNER_IS_NOT_A_SUPPLIER",
                $"Partner '{partner.Code}' is not marked as a supplier. Change their type before buying "
                + "from them — an order to a customer is a document nobody can act on.");
        }

        if (!partner.IsActive)
        {
            throw new ProcurementRuleException(
                "PROCUREMENT_SUPPLIER_IS_INACTIVE",
                $"Supplier '{partner.Code}' has been retired from new trading. Reactivate them, or buy "
                + "from somebody else — existing orders against them are unaffected.");
        }

        return partner;
    }
}
