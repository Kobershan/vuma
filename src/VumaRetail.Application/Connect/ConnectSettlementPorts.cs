#pragma warning disable CS1591
using VumaRetail.Domain.Connect;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.Connect;

public enum ConnectPaymentStatus { Authorised = 1, Failed = 2, Captured = 3, Reversed = 4 }

public sealed record ConnectPaymentRequest(
    Guid PaymentId,
    Guid ConnectionId,
    Guid RetailerTenantId,
    Guid SupplierTenantId,
    IReadOnlyList<Guid> InvoiceIds,
    Money Amount,
    ConnectPaymentMethod Method,
    string IdempotencyKey);

public sealed record GatewayPaymentResult(
    ConnectPaymentStatus Status,
    string ProviderReference,
    string? FailureReason = null);

public sealed record SettlementResult(
    ConnectPaymentStatus Status,
    string RemittanceReference,
    string? FailureReason = null);

/// <summary>Licensed-provider boundary. No provider credentials or card data cross this port.</summary>
public interface IPaymentGateway
{
    Task<GatewayPaymentResult> AuthoriseAsync(ConnectPaymentRequest request, CancellationToken cancellationToken = default);
    Task<GatewayPaymentResult> CaptureAsync(Guid paymentId, string providerReference, CancellationToken cancellationToken = default);
}

/// <summary>Provider settlement boundary; Vuma records the result but never holds customer funds.</summary>
public interface ISettlementProvider
{
    Task<SettlementResult> SettleAsync(ConnectPaymentRequest request, string providerReference, CancellationToken cancellationToken = default);
}

/// <summary>Posts the paired AP/AR facts after provider settlement has succeeded.</summary>
public interface IConnectLedgerPoster
{
    Task PostRetailerPayableAsync(ConnectPaymentRequest request, string remittanceReference, CancellationToken cancellationToken = default);
    Task PostSupplierReceivableAsync(ConnectPaymentRequest request, string remittanceReference, CancellationToken cancellationToken = default);
}

public interface IConnectRemittanceRepository
{
    Task<SettlementResult?> FindSettlementAsync(Guid paymentId, Guid tenantId, CancellationToken cancellationToken = default);
    void Add(VumaRetail.Domain.Connect.ConnectRemittanceAdvice remittance);
}
