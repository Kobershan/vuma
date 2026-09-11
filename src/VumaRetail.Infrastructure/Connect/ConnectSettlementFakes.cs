#pragma warning disable CS1591
using System.Collections.Concurrent;
using VumaRetail.Application.Connect;

namespace VumaRetail.Infrastructure.Connect;

/// <summary>Deterministic provider fake used by tests and local development; never moves funds.</summary>
public sealed class InMemoryConnectPaymentGateway : IPaymentGateway
{
    private readonly ConcurrentDictionary<Guid, GatewayPaymentResult> _payments = new();

    public Task<GatewayPaymentResult> AuthoriseAsync(ConnectPaymentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        GatewayPaymentResult result = _payments.GetOrAdd(request.PaymentId,
            _ => request.Amount.IsNegative || request.Amount.IsZero
                ? new(ConnectPaymentStatus.Failed, $"FAKE-{request.PaymentId:N}", "Payment amount must be positive.")
                : new(ConnectPaymentStatus.Authorised, $"FAKE-{request.PaymentId:N}"));
        return Task.FromResult(result);
    }

    public Task<GatewayPaymentResult> CaptureAsync(Guid paymentId, string providerReference, CancellationToken cancellationToken = default)
    {
        if (paymentId == Guid.Empty || string.IsNullOrWhiteSpace(providerReference))
            throw new ArgumentException("A payment and provider reference are required.");
        GatewayPaymentResult result = _payments.TryGetValue(paymentId, out GatewayPaymentResult? current)
            ? current with { Status = current.Status == ConnectPaymentStatus.Authorised ? ConnectPaymentStatus.Captured : current.Status }
            : new(ConnectPaymentStatus.Failed, providerReference, "Payment was not authorised.");
        _payments[paymentId] = result;
        return Task.FromResult(result);
    }
}

public sealed class InMemoryConnectSettlementProvider : ISettlementProvider
{
    public Task<SettlementResult> SettleAsync(ConnectPaymentRequest request, string providerReference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(providerReference)) throw new ArgumentException("A provider reference is required.");
        return Task.FromResult(new SettlementResult(ConnectPaymentStatus.Captured, $"REM-{request.PaymentId:N}"));
    }
}

public sealed class InMemoryConnectLedgerPoster : IConnectLedgerPoster
{
    private readonly ConcurrentDictionary<string, byte> _posted = new();
    public Task PostRetailerPayableAsync(ConnectPaymentRequest request, string remittanceReference, CancellationToken cancellationToken = default)
    { _posted.TryAdd($"ap:{request.PaymentId:N}", 0); return Task.CompletedTask; }
    public Task PostSupplierReceivableAsync(ConnectPaymentRequest request, string remittanceReference, CancellationToken cancellationToken = default)
    { _posted.TryAdd($"ar:{request.PaymentId:N}", 0); return Task.CompletedTask; }
}
