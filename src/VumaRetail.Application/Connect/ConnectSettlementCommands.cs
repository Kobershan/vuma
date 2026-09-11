#pragma warning disable CS1591, IDE0011, CA1062
using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Connect;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.Connect;

[CommandSideEffect(SideEffect.Write)]
public sealed record SettleConnectPaymentCommand(Guid PaymentId, Guid ConnectionId, string InvoiceReference,
    decimal Amount, string Currency, ConnectPaymentMethod Method) : ICommand<SettlementResult>;

public sealed class SettleConnectPaymentCommandValidator : AbstractValidator<SettleConnectPaymentCommand>
{
    public SettleConnectPaymentCommandValidator()
    {
        RuleFor(x => x.PaymentId).NotEmpty();
        RuleFor(x => x.ConnectionId).NotEmpty();
        RuleFor(x => x.InvoiceReference).NotEmpty().MaximumLength(128);
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Currency).Length(3);
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed class SettleConnectPaymentCommandHandler(
    IPaymentGateway gateway, ISettlementProvider settlement, IConnectLedgerPoster ledger,
    IConnectRemittanceRepository remittances, ITradingConnectionRepository connections,
    ITenantContext tenant, IClock clock)
    : ICommandHandler<SettleConnectPaymentCommand, SettlementResult>
{
    public async Task<SettlementResult> HandleAsync(SettleConnectPaymentCommand command, CancellationToken cancellationToken = default)
    {
        TradingConnection connection = await connections.FindForTenantAsync(command.ConnectionId, tenant.TenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Connection not found.");
        if (connection.RetailerTenantId != tenant.TenantId || connection.Status != TradingConnectionStatus.Active)
            throw new UnauthorizedAccessException("Only the connected retailer can make a payment.");

        ConnectPaymentRequest request = new(command.PaymentId, connection.Id, connection.RetailerTenantId,
            connection.SupplierTenantId, [], new Money(command.Amount, command.Currency), command.Method, command.InvoiceReference);
        SettlementResult? existing = await remittances.FindSettlementAsync(command.PaymentId, tenant.TenantId, cancellationToken).ConfigureAwait(false);
        if (existing is not null) return existing;

        GatewayPaymentResult authorised = await gateway.AuthoriseAsync(request, cancellationToken).ConfigureAwait(false);
        if (authorised.Status != ConnectPaymentStatus.Authorised)
            return new(authorised.Status, string.Empty, authorised.FailureReason);
        GatewayPaymentResult captured = await gateway.CaptureAsync(command.PaymentId, authorised.ProviderReference, cancellationToken).ConfigureAwait(false);
        if (captured.Status != ConnectPaymentStatus.Captured)
            return new(captured.Status, string.Empty, captured.FailureReason);
        SettlementResult result = await settlement.SettleAsync(request, captured.ProviderReference, cancellationToken).ConfigureAwait(false);
        if (result.Status != ConnectPaymentStatus.Captured) return result;

        await ledger.PostRetailerPayableAsync(request, result.RemittanceReference, cancellationToken).ConfigureAwait(false);
        await ledger.PostSupplierReceivableAsync(request, result.RemittanceReference, cancellationToken).ConfigureAwait(false);
        remittances.Add(ConnectRemittanceAdvice.Issue(connection.RetailerTenantId, connection.SupplierTenantId,
            connection.Id, command.PaymentId, command.InvoiceReference, request.Amount, command.Method,
            captured.ProviderReference, result.RemittanceReference, clock.UtcNow));
        return result;
    }
}
