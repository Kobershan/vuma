using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Imports;
using VumaRetail.Application.Partners;
using VumaRetail.Domain.Imports;
using VumaRetail.Domain.Partners;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.Imports.Targets;

/// <summary>
/// Imports a supplier or customer list into Stage 06 <see cref="Partner"/> rows.
/// </summary>
/// <remarks>
/// <para>
/// <b>An imported supplier is a partner, not a second kind of supplier (ADR-079).</b> This handler
/// calls <see cref="Partner.Create"/> — the same factory <c>CreatePartnerCommandHandler</c> calls — so
/// the rule that a partner is a customer or a supplier and never neither is enforced on an imported
/// row by the same line of code that enforces it on a typed one. It does not call that handler
/// (<c>CONVENTIONS.md</c> §4).
/// </para>
/// <para>
/// <b>The address is all-or-nothing.</b> <see cref="Address"/> requires a line, a city and a country,
/// and a file that carries a street with no town produces a row error naming what is missing rather
/// than a partner with half an address. Half an address is worse than none: it looks complete on a
/// screen and fails at the point somebody tries to deliver to it.
/// </para>
/// <para>
/// Suppliers and customers are two target kinds over one entity because the <em>files</em> differ, and
/// the field catalogue is what a screen builds its mapping UI from. One catalogue describing both
/// would have to make every field optional, which is how a required field quietly stops being checked.
/// </para>
/// </remarks>
/// <param name="partners">Partner lookup and insertion.</param>
/// <param name="tenant">The ambient tenant.</param>
/// <param name="usage">Whether anything now references a partner this import created.</param>
/// <param name="principal">Who is acting — stamped onto a soft delete.</param>
/// <param name="clock">The only source of time (<c>CONVENTIONS.md</c> §6).</param>
public abstract class PartnerImportTargetHandler(
    IPartnerRepository partners,
    ITenantContext tenant,
    IImportUsageProbe usage,
    IPrincipalAccessor principal,
    IClock clock) : ImportTargetHandler
{
    /// <summary>The partner code — the natural key an import matches duplicates on.</summary>
    public const string CodeField = "code";

    /// <summary>The trading name.</summary>
    public const string NameField = "name";

    private const string EmailField = "email";
    private const string PhoneField = "phone";
    private const string TaxNumberField = "taxNumber";
    private const string AddressLine1Field = "addressLine1";
    private const string AddressLine2Field = "addressLine2";
    private const string CityField = "city";
    private const string RegionField = "region";
    private const string PostalCodeField = "postalCode";
    private const string CountryCodeField = "countryCode";

    /// <summary>Whether the rows in this file are people the tenant buys from or sells to.</summary>
    protected abstract PartnerType PartnerType { get; }

    /// <inheritdoc />
    public override async Task<ImportSemanticVerdict> ValidateAsync(
        IReadOnlyDictionary<string, string?> values,
        ImportContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(context);

        List<ImportRowError> errors = [];
        string code = RequiredText(values, CodeField);

        if (AddressErrorOrNull(values) is { } addressError)
        {
            errors.Add(addressError);
        }

        if (Text(values, CountryCodeField) is { Length: not 2 } country)
        {
            errors.Add(ImportRowError.OutOfRange(
                CountryCodeField,
                $"'{country}' is not a two-letter ISO 3166-1 country code — 'ZA', not 'South Africa'."));
        }

        Partner? existing = await partners.FindByCodeAsync(code, cancellationToken).ConfigureAwait(false);

        if (existing is null)
        {
            return errors.Count > 0 ? ImportSemanticVerdict.Invalid(errors) : ImportSemanticVerdict.WouldCreate();
        }

        switch (context.DuplicateStrategy)
        {
            case ImportDuplicateStrategy.Skip:
                // Reported as a skip even when the row has other errors: the row is not going to be
                // applied either way, and "this one already exists" is the answer the person needs.
                return ImportSemanticVerdict.Skip(existing.Id);

            case ImportDuplicateStrategy.Fail:
                errors.Add(ImportRowError.Duplicate(CodeField, code));
                return ImportSemanticVerdict.Invalid(errors);

            default:
                return errors.Count > 0
                    ? ImportSemanticVerdict.Invalid(errors)
                    : ImportSemanticVerdict.WouldUpdate(existing.Id);
        }
    }

    /// <inheritdoc />
    public override async Task<ImportApplyResult> ApplyAsync(
        IReadOnlyDictionary<string, string?> values,
        ImportContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(context);

        string code = RequiredText(values, CodeField);
        string name = RequiredText(values, NameField);
        Address? address = AddressOrNull(values);
        string? email = Text(values, EmailField);
        string? phone = Text(values, PhoneField);
        string? taxNumber = Text(values, TaxNumberField);

        Partner? existing = await partners.FindByCodeAsync(code, cancellationToken).ConfigureAwait(false);

        if (existing is null)
        {
            Partner created = Partner.Create(
                tenant.TenantId, code, name, PartnerType, address, email, phone, taxNumber);

            partners.Add(created);

            return ImportApplyResult.Created(created.Id);
        }

        string beforeImage = WriteBeforeImage(PartnerBeforeImage.Of(existing));

        existing.SetDetails(name, address, email, phone, taxNumber);

        // A partner who was only a customer and turns up in a supplier file is both, not a supplier
        // who has stopped being a customer. Overwriting the type here would silently drop the AR side
        // of somebody the shop still sells to.
        if (!existing.Type.HasFlag(PartnerType))
        {
            existing.SetType(existing.Type | PartnerType);
        }

        return ImportApplyResult.Updated(existing.Id, beforeImage);
    }

    /// <inheritdoc />
    public override async Task<ImportRollbackObstacle?> CanCompensateAsync(
        ImportRow row, ImportContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (row.Outcome is not ImportRowOutcome.Created)
        {
            // An update is restored from its before-image, which is always possible: the fields this
            // handler touches are descriptive, and nothing else in the system references them.
            return null;
        }

        Partner? partner = row.TargetEntityId is { } id
            ? await partners.FindAsync(id, cancellationToken).ConfigureAwait(false)
            : null;

        if (partner is null)
        {
            return null;
        }

        // Rule 6: a created partner that another document now points at cannot be removed, because
        // removing it would leave an invoice or a sale pointing at a deleted trading party.
        string? reference = await usage
            .DescribePartnerUsageAsync(partner.Id, cancellationToken)
            .ConfigureAwait(false);

        return reference is null
            ? null
            : new ImportRollbackObstacle(
                row.RowNumber,
                $"partner {partner.Code} has been used since it was imported ({reference})");
    }

    /// <inheritdoc />
    public override async Task<Guid?> CompensateAsync(
        ImportRow row, ImportContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(row);

        Partner? partner = row.TargetEntityId is { } id
            ? await partners.FindAsync(id, cancellationToken).ConfigureAwait(false)
            : null;

        if (partner is null)
        {
            // Already gone. A rollback's job is to leave the world as it was, and it already is.
            return null;
        }

        if (row.Outcome is ImportRowOutcome.Created)
        {
            // Soft delete, never a DELETE (§7 rule 8). The global query filter takes it out of every
            // read from the next statement onward, and the row stays on disk saying who removed it.
            partner.MarkDeleted(principal.Principal, clock.UtcNow);

            return null;
        }

        PartnerBeforeImage before = ReadBeforeImage<PartnerBeforeImage>(row);

        partner.SetDetails(before.Name, before.Address, before.Email, before.Phone, before.TaxNumber);
        partner.SetType(before.Type);

        if (before.IsActive)
        {
            partner.Activate();
        }
        else
        {
            partner.Deactivate();
        }

        return null;
    }

    /// <summary>The field catalogue both partner targets share, named for the file it describes.</summary>
    /// <param name="party">The word this file's rows are called — <c>supplier</c> or <c>customer</c>.</param>
    /// <param name="codeAliases">The header texts that mean the code column in this kind of file.</param>
    /// <param name="nameAliases">The header texts that mean the name column.</param>
    protected static IReadOnlyList<ImportFieldDescriptor> FieldsFor(
        string party, IReadOnlyList<string> codeAliases, IReadOnlyList<string> nameAliases)
        =>
        [
            new(
                CodeField,
                ImportFieldType.Text,
                IsRequired: true,
                $"The {party}'s unique code in this tenant. Rows are matched on it, so it is what "
                + "decides whether a row creates or updates.",
                "ACME001",
                codeAliases),
            new(
                NameField,
                ImportFieldType.Text,
                IsRequired: true,
                $"The {party}'s trading name.",
                "Acme Wholesalers",
                nameAliases),
            new(
                EmailField,
                ImportFieldType.Text,
                IsRequired: false,
                "An email address.",
                "accounts@acme.co.za",
                ["e-mail", "email address", "contact email"]),
            new(
                PhoneField,
                ImportFieldType.Text,
                IsRequired: false,
                "A contact number.",
                "021 555 0100",
                ["telephone", "tel", "contact number", "mobile", "cell", "cellphone"]),
            new(
                TaxNumberField,
                ImportFieldType.Text,
                IsRequired: false,
                "A tax registration number.",
                "4123456789",
                ["vat number", "vat no", "vat registration", "tax reference", "tax ref", "vat"]),
            new(
                AddressLine1Field,
                ImportFieldType.Text,
                IsRequired: false,
                "The street address. Supplying it means the city and country are required too — half "
                + "an address looks complete and fails at the point somebody tries to deliver to it.",
                "14 Long Street",
                ["address", "street", "address line 1", "address1", "physical address"]),
            new(
                AddressLine2Field,
                ImportFieldType.Text,
                IsRequired: false,
                "A second address line.",
                "Unit 3",
                ["address line 2", "address2", "suburb"]),
            new(
                CityField,
                ImportFieldType.Text,
                IsRequired: false,
                "The city or town.",
                "Cape Town",
                ["town", "city/town"]),
            new(
                RegionField,
                ImportFieldType.Text,
                IsRequired: false,
                "The province, state or region.",
                "Western Cape",
                ["province", "state", "region/province"]),
            new(
                PostalCodeField,
                ImportFieldType.Text,
                IsRequired: false,
                "The postal code.",
                "8001",
                ["postcode", "post code", "zip", "zip code", "postal"]),
            new(
                CountryCodeField,
                ImportFieldType.Text,
                IsRequired: false,
                "The two-letter ISO 3166-1 country code. Bind a constant default when the file has no "
                + "country column, which most single-country files do not.",
                "ZA",
                ["country", "country code", "iso country"]),
        ];

    /// <summary>Builds the address, or <c>null</c> when the row carries none.</summary>
    /// <param name="values">The row's normalised values.</param>
    private static Address? AddressOrNull(IReadOnlyDictionary<string, string?> values)
    {
        string? line1 = Text(values, AddressLine1Field);
        string? city = Text(values, CityField);
        string? country = Text(values, CountryCodeField);

        if (line1 is null || city is null || country is null)
        {
            return null;
        }

        return Address.Create(
            line1,
            city,
            country,
            Text(values, AddressLine2Field),
            Text(values, RegionField),
            Text(values, PostalCodeField));
    }

    /// <summary>Refuses a partial address, naming the parts that are missing.</summary>
    /// <param name="values">The row's normalised values.</param>
    /// <returns>The error, or <c>null</c> when the row's address is whole or absent.</returns>
    private static ImportRowError? AddressErrorOrNull(IReadOnlyDictionary<string, string?> values)
    {
        (string Field, string? Value)[] parts =
        [
            (AddressLine1Field, Text(values, AddressLine1Field)),
            (CityField, Text(values, CityField)),
            (CountryCodeField, Text(values, CountryCodeField)),
        ];

        // Nothing at all is fine — a partner with no address is ordinary. Anything at all, including
        // an optional line 2 or a postal code on its own, means an address was intended.
        bool intended = parts.Any(part => part.Value is not null)
            || Text(values, AddressLine2Field) is not null
            || Text(values, RegionField) is not null
            || Text(values, PostalCodeField) is not null;

        if (!intended)
        {
            return null;
        }

        string[] missing = parts.Where(part => part.Value is null).Select(part => part.Field).ToArray();

        return missing.Length == 0
            ? null
            : ImportRowError.OutOfRange(
                missing[0],
                $"This row carries part of an address, so it needs all of it. Missing: "
                + $"{string.Join(", ", missing)}.");
    }
}

/// <summary>The partner fields a rollback restores, and nothing else.</summary>
/// <remarks>
/// Only what <see cref="PartnerImportTargetHandler.ApplyAsync"/> can change. Storing the whole entity
/// would make the image break the first time a later stage adds a field, and restoring a field this
/// handler never touched would undo somebody else's edit.
/// </remarks>
/// <param name="Name">The trading name before the import.</param>
/// <param name="Address">The address before the import.</param>
/// <param name="Email">The email before the import.</param>
/// <param name="Phone">The phone number before the import.</param>
/// <param name="TaxNumber">The tax number before the import.</param>
/// <param name="Type">Whether the partner was a customer, a supplier or both.</param>
/// <param name="IsActive">Whether the partner was trading.</param>
public sealed record PartnerBeforeImage(
    string Name,
    Address? Address,
    string? Email,
    string? Phone,
    string? TaxNumber,
    PartnerType Type,
    bool IsActive)
{
    /// <summary>Captures a partner's current state.</summary>
    /// <param name="partner">The partner, before it is changed.</param>
    public static PartnerBeforeImage Of(Partner partner)
    {
        ArgumentNullException.ThrowIfNull(partner);

        return new PartnerBeforeImage(
            partner.Name,
            partner.Address,
            partner.Email,
            partner.Phone,
            partner.TaxNumber,
            partner.Type,
            partner.IsActive);
    }
}

/// <summary>Imports a supplier list.</summary>
/// <param name="partners">Partner lookup and insertion.</param>
/// <param name="tenant">The ambient tenant.</param>
/// <param name="usage">Whether anything now references a partner this import created.</param>
/// <param name="principal">Who is acting.</param>
/// <param name="clock">The only source of time.</param>
public sealed class SupplierImportTargetHandler(
    IPartnerRepository partners,
    ITenantContext tenant,
    IImportUsageProbe usage,
    IPrincipalAccessor principal,
    IClock clock)
    : PartnerImportTargetHandler(partners, tenant, usage, principal, clock)
{
    /// <inheritdoc />
    public override ImportTargetKind Kind => ImportTargetKind.Suppliers;

    /// <inheritdoc />
    public override ImportTargetDescriptor Descriptor => new(
        ImportTargetKind.Suppliers,
        "Suppliers",
        "People and businesses this shop buys from. A supplier who is already on file as a customer "
        + "stays a customer as well — the roles add up, they do not replace each other.",
        RequiresStore: false,
        [CodeField],
        FieldsFor(
            "supplier",
            ["supplier code", "supplier no", "supplier number", "account", "account no",
                "account number", "vendor code", "creditor code", "partner code"],
            ["supplier", "supplier name", "vendor", "vendor name", "company", "company name",
                "trading name", "business name", "creditor name", "partner name"]));

    /// <inheritdoc />
    protected override PartnerType PartnerType => PartnerType.Supplier;
}

/// <summary>Imports a customer list.</summary>
/// <param name="partners">Partner lookup and insertion.</param>
/// <param name="tenant">The ambient tenant.</param>
/// <param name="usage">Whether anything now references a partner this import created.</param>
/// <param name="principal">Who is acting.</param>
/// <param name="clock">The only source of time.</param>
public sealed class CustomerImportTargetHandler(
    IPartnerRepository partners,
    ITenantContext tenant,
    IImportUsageProbe usage,
    IPrincipalAccessor principal,
    IClock clock)
    : PartnerImportTargetHandler(partners, tenant, usage, principal, clock)
{
    /// <inheritdoc />
    public override ImportTargetKind Kind => ImportTargetKind.Customers;

    /// <inheritdoc />
    public override ImportTargetDescriptor Descriptor => new(
        ImportTargetKind.Customers,
        "Customers",
        "People and businesses this shop sells to on account. A customer who is already on file as a "
        + "supplier stays a supplier as well.",
        RequiresStore: false,
        [CodeField],
        FieldsFor(
            "customer",
            ["customer code", "customer no", "customer number", "account", "account no",
                "account number", "debtor code", "partner code"],
            ["customer", "customer name", "company", "company name", "trading name", "business name",
                "debtor name", "partner name"]));

    /// <inheritdoc />
    protected override PartnerType PartnerType => PartnerType.Customer;
}
