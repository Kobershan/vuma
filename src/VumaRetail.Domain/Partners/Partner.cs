using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Partners;

/// <summary>
/// Somebody a tenant trades with — a supplier, a customer, or both.
/// </summary>
/// <remarks>
/// Identity and description only. No credit terms, no balance, no ledger account: Stage 07 owns AR/AP
/// against a <c>PartnerId</c> it doesn't need this stage to widen for (Stage 06's own scope note).
/// </remarks>
[Replicated(ReplicationScope.Bidirectional, ConflictPolicy.CloudWins)]
public sealed class Partner : Entity
{
    private Partner(Guid tenantId, string code, string name, PartnerType type)
        : base(tenantId)
    {
        Code = code;
        Name = name;
        Type = type;
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private Partner()
    {
    }

    /// <summary>The partner's unique code within the tenant, upper-cased.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>The partner's trading name.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Whether this partner is a customer, a supplier, or both.</summary>
    public PartnerType Type { get; private set; }

    /// <summary>The partner's address, or <c>null</c> if none has been recorded.</summary>
    public Address? Address { get; private set; }

    /// <summary>An email address, or <c>null</c>.</summary>
    public string? Email { get; private set; }

    /// <summary>A phone number, or <c>null</c>.</summary>
    public string? Phone { get; private set; }

    /// <summary>A tax registration number, or <c>null</c>.</summary>
    public string? TaxNumber { get; private set; }

    /// <summary>True while the partner may be traded with. Deactivate, not delete (§7 rule 8).</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>Creates a new partner.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="code">The partner code, upper-cased.</param>
    /// <param name="name">The partner's trading name.</param>
    /// <param name="type">Customer, supplier, or both. Never neither.</param>
    /// <param name="address">The partner's address, or <c>null</c>.</param>
    /// <param name="email">An email address, or <c>null</c>.</param>
    /// <param name="phone">A phone number, or <c>null</c>.</param>
    /// <param name="taxNumber">A tax registration number, or <c>null</c>.</param>
    /// <exception cref="PartnerRuleException">
    /// <paramref name="type"/> is <see cref="PartnerType.None"/> — the flag is why the type exists.
    /// </exception>
    public static Partner Create(
        Guid tenantId,
        string code,
        string name,
        PartnerType type,
        Address? address = null,
        string? email = null,
        string? phone = null,
        string? taxNumber = null)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A partner must belong to a tenant.", nameof(tenantId));
        }

        RequireValidType(type);

        return new Partner(
            tenantId,
            Require(code, nameof(code)).ToUpperInvariant(),
            Require(name, nameof(name)),
            type)
        {
            Address = address,
            Email = Trim(email),
            Phone = Trim(phone),
            TaxNumber = Trim(taxNumber),
        };
    }

    /// <summary>Updates the partner's descriptive fields.</summary>
    /// <param name="name">The new trading name.</param>
    /// <param name="address">The new address, or <c>null</c> to clear it.</param>
    /// <param name="email">The new email, or <c>null</c> to clear it.</param>
    /// <param name="phone">The new phone number, or <c>null</c> to clear it.</param>
    /// <param name="taxNumber">The new tax number, or <c>null</c> to clear it.</param>
    public void SetDetails(string name, Address? address, string? email, string? phone, string? taxNumber)
    {
        Name = Require(name, nameof(name));
        Address = address;
        Email = Trim(email);
        Phone = Trim(phone);
        TaxNumber = Trim(taxNumber);
    }

    /// <summary>Changes whether the partner is a customer, a supplier, or both.</summary>
    /// <param name="type">The new type. Never <see cref="PartnerType.None"/>.</param>
    /// <exception cref="PartnerRuleException"><paramref name="type"/> is <see cref="PartnerType.None"/>.</exception>
    public void SetType(PartnerType type)
    {
        RequireValidType(type);
        Type = type;
    }

    /// <summary>Retires the partner from new trading. History is retained; nothing is deleted (§7 rule 8).</summary>
    public void Deactivate() => IsActive = false;

    /// <summary>Brings a retired partner back into use.</summary>
    public void Activate() => IsActive = true;

    private static void RequireValidType(PartnerType type)
    {
        if (type == PartnerType.None)
        {
            throw PartnerRuleException.MustBeCustomerOrSupplier();
        }
    }

    private static string Require(string value, string parameterName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{parameterName} is required.", parameterName)
            : value.Trim();

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Whether a <see cref="Partner"/> is a customer, a supplier, or both.</summary>
[Flags]
public enum PartnerType
{
    /// <summary>Neither — not a valid state for a stored partner. The domain refuses it.</summary>
    None = 0,

    /// <summary>Somebody the tenant sells to.</summary>
    Customer = 1,

    /// <summary>Somebody the tenant buys from.</summary>
    Supplier = 2,
}
