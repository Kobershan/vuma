using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Imports;
using VumaRetail.Application.Platform;
using VumaRetail.Domain.Imports;
using VumaRetail.Domain.Platform;

namespace VumaRetail.Application.Imports;

/// <summary>
/// Builds the <see cref="ImportContext"/> every target handler call is given.
/// </summary>
/// <remarks>
/// <para>
/// One class rather than four lines repeated in the validate and commit handlers, because the two
/// must agree. A preview validated against one currency and a commit applied in another is
/// §4.13's foreign-currency bug again, and the only way to be sure they cannot differ is for there to
/// be one place the currency is decided.
/// </para>
/// <para>
/// <b>The currency comes from the store, then the tenant, and never from the file.</b> A cell reading
/// <c>USD 4.99</c> in a rand shop is a mistake in the spreadsheet; the parser strips the symbol and
/// the amount is read in the currency the shop actually trades in. Letting a file choose would let one
/// row's typo denominate somebody's price list in a currency the till cannot tender.
/// </para>
/// </remarks>
/// <param name="stores">Store lookup, for a branch that overrides the tenant's currency.</param>
/// <param name="tenants">Tenant lookup, for the base currency.</param>
/// <param name="tenant">The ambient tenant.</param>
public sealed class ImportBatchContextFactory(
    IStoreRepository stores, ITenantRepository tenants, ITenantContext tenant) : IImportContextFactory
{
    /// <inheritdoc />
    public async Task<ImportContext> ForAsync(ImportBatch batch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);

        string? currency = null;

        if (batch.StoreId is { } storeId)
        {
            Store? store = await stores.FindAsync(storeId, cancellationToken).ConfigureAwait(false);
            currency = store?.CurrencyOverride;
        }

        if (string.IsNullOrWhiteSpace(currency))
        {
            Tenant? owner = await tenants.FindAsync(tenant.TenantId, cancellationToken).ConfigureAwait(false);
            currency = owner?.BaseCurrency;
        }

        if (string.IsNullOrWhiteSpace(currency))
        {
            // A tenant with no base currency cannot be imported into, and guessing one would put a
            // fabricated currency code onto every price the file carries.
            throw new ImportRuleException(
                "IMPORTS_NO_CURRENCY",
                "This tenant has no base currency and the store does not override one, so there is "
                + "nothing to read the file's money columns in. Set the tenant's localisation first.");
        }

        return new ImportContext(
            batch.Id, batch.BatchNumber, batch.StoreId, batch.DuplicateStrategy, currency);
    }
}

/// <summary>Decides the standing instructions a batch's rows are judged and applied under.</summary>
/// <remarks>
/// A port so the validate and commit paths cannot resolve the currency differently — see the
/// implementation's remarks.
/// </remarks>
public interface IImportContextFactory
{
    /// <summary>Builds the context for a batch.</summary>
    /// <param name="batch">The batch.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<ImportContext> ForAsync(ImportBatch batch, CancellationToken cancellationToken = default);
}
