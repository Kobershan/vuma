#pragma warning disable CS1591, IDE0011, CA1062
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Connect;

public enum ConnectPaymentMethod { Card = 1, Eft = 2, InstantEft = 3, DebitOrder = 4 }
public enum ConnectRemittanceStatus { Pending = 1, Settled = 2, Failed = 3 }

[Replicated(ReplicationScope.Bidirectional, ConflictPolicy.CloudWins)]
public sealed class ConnectRemittanceAdvice : Entity
{
    private ConnectRemittanceAdvice(Guid retailerTenantId, Guid supplierTenantId, Guid connectionId,
        Guid paymentId, string invoiceReference, Money amount, ConnectPaymentMethod method,
        string providerReference, string remittanceReference, DateTimeOffset issuedAt) : base(retailerTenantId)
    {
        RetailerTenantId = retailerTenantId;
        SupplierTenantId = supplierTenantId;
        ConnectionId = connectionId;
        PaymentId = paymentId;
        InvoiceReference = invoiceReference.Trim();
        Amount = amount;
        Method = method;
        ProviderReference = providerReference.Trim();
        RemittanceReference = remittanceReference.Trim();
        IssuedAt = issuedAt;
        Status = ConnectRemittanceStatus.Settled;
    }

    private ConnectRemittanceAdvice() { }

    public Guid RetailerTenantId { get; private set; }
    public Guid SupplierTenantId { get; private set; }
    public Guid ConnectionId { get; private set; }
    public Guid PaymentId { get; private set; }
    public string InvoiceReference { get; private set; } = string.Empty;
    public Money Amount { get; private set; }
    public ConnectPaymentMethod Method { get; private set; }
    public string ProviderReference { get; private set; } = string.Empty;
    public string RemittanceReference { get; private set; } = string.Empty;
    public ConnectRemittanceStatus Status { get; private set; }
    public DateTimeOffset IssuedAt { get; private set; }

    public static ConnectRemittanceAdvice Issue(Guid retailerTenantId, Guid supplierTenantId, Guid connectionId,
        Guid paymentId, string invoiceReference, Money amount, ConnectPaymentMethod method,
        string providerReference, string remittanceReference, DateTimeOffset issuedAt)
    {
        if (retailerTenantId == Guid.Empty || supplierTenantId == Guid.Empty || retailerTenantId == supplierTenantId)
            throw new ArgumentException("A remittance requires two different tenants.");
        if (connectionId == Guid.Empty || paymentId == Guid.Empty || string.IsNullOrWhiteSpace(invoiceReference))
            throw new ArgumentException("A remittance requires a connection, payment and invoice reference.");
        return new(retailerTenantId, supplierTenantId, connectionId, paymentId, invoiceReference, amount, method,
            providerReference, remittanceReference, issuedAt);
    }
}
