#pragma warning disable CS1591, IDE0011
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Connect;

/// <summary>A mutual, revocable supplier/retailer relationship.</summary>
[Replicated(ReplicationScope.Bidirectional, ConflictPolicy.CloudWins)]
public sealed class TradingConnection : Entity
{
    private TradingConnection(Guid tenantId, Guid supplierTenantId, Guid retailerTenantId, string? supplierAccount,
        string? retailerAccount, DateTimeOffset createdAt) : base(tenantId)
    {
        SupplierTenantId = supplierTenantId;
        RetailerTenantId = retailerTenantId;
        SupplierAccountReference = supplierAccount;
        RetailerAccountReference = retailerAccount;
        Status = TradingConnectionStatus.Pending;
        CreatedAtUtc = createdAt;
    }

    private TradingConnection() { }

    public Guid SupplierTenantId { get; private set; }
    public Guid RetailerTenantId { get; private set; }
    public TradingConnectionStatus Status { get; private set; }
    public string? SupplierAccountReference { get; private set; }
    public string? RetailerAccountReference { get; private set; }
    public string Currency { get; private set; } = "ZAR";
    public decimal CreditLimit { get; private set; }
    public int LeadTimeDays { get; private set; }
    public decimal MinimumOrderValue { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? AcceptedAtUtc { get; private set; }
    public DateTimeOffset? SuspendedAtUtc { get; private set; }
    public DateTimeOffset? EndedAtUtc { get; private set; }

    public static TradingConnection Request(Guid supplierTenantId, Guid retailerTenantId, string? supplierAccount,
        string? retailerAccount, DateTimeOffset at)
    {
        if (supplierTenantId == Guid.Empty || retailerTenantId == Guid.Empty || supplierTenantId == retailerTenantId)
            throw new ArgumentException("A connection requires two different tenants.");
        return new TradingConnection(supplierTenantId, supplierTenantId, retailerTenantId, supplierAccount,
            retailerAccount, at);
    }

    public void Accept(string currency, decimal creditLimit, int leadTimeDays, decimal minimumOrderValue,
        DateTimeOffset at)
    {
        if (Status is not (TradingConnectionStatus.Pending or TradingConnectionStatus.Invited))
            throw new InvalidOperationException($"Cannot accept a connection in {Status} state.");
        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3) throw new ArgumentException("Currency must be ISO-4217.");
        if (creditLimit < 0 || leadTimeDays < 0 || minimumOrderValue < 0) throw new ArgumentOutOfRangeException();
        Currency = currency.Trim().ToUpperInvariant();
        CreditLimit = creditLimit;
        LeadTimeDays = leadTimeDays;
        MinimumOrderValue = minimumOrderValue;
        Status = TradingConnectionStatus.Active;
        AcceptedAtUtc = at;
    }

    public void Suspend(DateTimeOffset at)
    {
        if (Status != TradingConnectionStatus.Active) throw new InvalidOperationException("Only an active connection can be suspended.");
        Status = TradingConnectionStatus.Suspended;
        SuspendedAtUtc = at;
    }

    public void End(DateTimeOffset at)
    {
        if (Status == TradingConnectionStatus.Ended) return;
        Status = TradingConnectionStatus.Ended;
        EndedAtUtc = at;
    }
}
