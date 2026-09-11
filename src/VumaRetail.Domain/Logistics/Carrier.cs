#pragma warning disable CS1591
using VumaRetail.Domain.Entities;

namespace VumaRetail.Domain.Logistics;

public sealed class Carrier : Entity
{
    private Carrier(Guid tenantId, string code, string name, string? phone) : base(tenantId)
    { Code = code; Name = name; Phone = phone; IsActive = true; }
    private Carrier() { }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Phone { get; private set; }
    public bool IsActive { get; private set; }
    public static Carrier Create(Guid tenantId, string code, string name, string? phone = null)
    {
        if (tenantId == Guid.Empty) { throw new ArgumentException("Tenant is required.", nameof(tenantId)); }
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > 32) { throw new ArgumentException("Carrier code is required and must be 32 characters or fewer.", nameof(code)); }
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 160) { throw new ArgumentException("Carrier name is required and must be 160 characters or fewer.", nameof(name)); }
        return new Carrier(tenantId, code.Trim().ToUpperInvariant(), name.Trim(), string.IsNullOrWhiteSpace(phone) ? null : phone.Trim());
    }
    public void Deactivate() => IsActive = false;
}
