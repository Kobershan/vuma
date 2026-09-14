#pragma warning disable CS1591
using System.Security.Cryptography;
using System.Text;
using VumaRetail.Domain.Ecommerce;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;

namespace VumaRetail.Application.Ecommerce;

/// <summary>Persistence boundary for storefront channel registrations.</summary>
public interface IChannelConnectionRepository
{
    Task<ChannelConnection?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ChannelConnection?> FindByHostAsync(string host, CancellationToken cancellationToken = default);
    void Add(ChannelConnection connection);
}

/// <summary>Persistence boundary for published storefront products.</summary>
public interface IPublishedProductRepository
{
    Task<IReadOnlyList<PublishedProduct>> ListAsync(Guid channelConnectionId, int limit, CancellationToken cancellationToken = default);
    void Add(PublishedProduct product);
}

public interface ICommerceBasketRepository
{
    Task<CommerceBasket?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    void Add(CommerceBasket basket);
}

public interface ICommerceBasketLineRepository
{
    Task<IReadOnlyList<CommerceBasketLine>> ListForBasketAsync(Guid basketId, CancellationToken cancellationToken = default);
    void Add(CommerceBasketLine line);
}

public interface ICheckoutIntentRepository
{
    Task<CheckoutIntent?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CheckoutIntent?> FindByIdempotencyKeyAsync(string ownerKey, string idempotencyKey, CancellationToken cancellationToken = default);
    void Add(CheckoutIntent intent);
}

public interface IPaymentAttemptRepository
{
    Task<PaymentAttempt?> FindByEventIdAsync(string eventId, CancellationToken cancellationToken = default);
    Task<PaymentAttempt?> FindLatestForCheckoutAsync(Guid checkoutId, string providerPaymentId, CancellationToken cancellationToken = default);
    void Add(PaymentAttempt attempt);
}

[CommandSideEffect(SideEffect.Write)]
public sealed record ApplyPaymentNotificationCommand(Guid CheckoutId, Guid CompanyId, string EventId,
    string PayloadFingerprint, string ProviderPaymentId, string Status, string? ProviderReference) : ICommand<Guid>;

public sealed class ApplyPaymentNotificationCommandHandler(
    ICheckoutIntentRepository checkouts, IPaymentAttemptRepository attempts,
    ITenantContext tenant, ICompanyContext company, IClock clock)
    : ICommandHandler<ApplyPaymentNotificationCommand, Guid>
{
    public async Task<Guid> HandleAsync(ApplyPaymentNotificationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        RegisterChannelCommandHandler.EnsureCompany(company, command.CompanyId, "payment");
        PaymentAttempt? existing = await attempts.FindByEventIdAsync(command.EventId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            if (existing.CompanyId != command.CompanyId)
            {
                throw new InvalidOperationException("Payment event belongs to another company.");
            }
            if (existing.CheckoutIntentId != command.CheckoutId ||
                !string.Equals(existing.PayloadFingerprint, command.PayloadFingerprint.Trim(), StringComparison.Ordinal) ||
                !string.Equals(existing.ProviderPaymentId, command.ProviderPaymentId.Trim(), StringComparison.Ordinal) ||
                existing.Status != statusFromCommand(command.Status) ||
                !string.Equals(existing.ProviderReference, command.ProviderReference?.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Payment event was replayed with different content.");
            }
            return existing.Id;
        }
        CheckoutIntent checkout = await checkouts.FindAsync(command.CheckoutId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Checkout intent not found.");
        if (checkout.CompanyId != command.CompanyId)
        {
            throw new InvalidOperationException("Payment checkout is not in the active company.");
        }
        if (!Enum.TryParse<PaymentAttemptStatus>(command.Status, true, out PaymentAttemptStatus status))
        {
            throw new ArgumentException("Unknown payment status.", nameof(command));
        }
        PaymentAttempt? latest = await attempts.FindLatestForCheckoutAsync(checkout.Id, command.ProviderPaymentId, cancellationToken).ConfigureAwait(false);
        if (latest is not null && !PaymentAttempt.IsAllowedTransition(latest.Status, status))
        {
            throw new InvalidOperationException("Payment status cannot move backwards or change after reversal.");
        }
        PaymentAttempt attempt = PaymentAttempt.Record(tenant.TenantId, command.CompanyId, checkout.Id,
            command.EventId, command.PayloadFingerprint, command.ProviderPaymentId, status,
            command.ProviderReference, clock.UtcNow);
        attempts.Add(attempt);
        return attempt.Id;
    }

    private static PaymentAttemptStatus statusFromCommand(string status)
        => Enum.TryParse<PaymentAttemptStatus>(status, true, out PaymentAttemptStatus parsed)
            ? parsed
            : (PaymentAttemptStatus)(-1);
}

public static class PaymentWebhookSecurity
{
    public static bool Verify(string body, string? presentedSignature, string secret)
    {
        if (string.IsNullOrWhiteSpace(body) || string.IsNullOrWhiteSpace(presentedSignature) || string.IsNullOrWhiteSpace(secret) ||
            !presentedSignature.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        try
        {
            byte[] expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body));
            byte[] actual = Convert.FromHexString(presentedSignature["sha256=".Length..]);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record SubmitCheckoutCommand(Guid BasketId, Guid CompanyId, string OwnerKey,
    string IdempotencyKey, string ContentFingerprint) : ICommand<Guid>;

public sealed class SubmitCheckoutCommandHandler(
    ICommerceBasketRepository baskets, ICommerceBasketLineRepository lines, ICheckoutIntentRepository intents,
    ITenantContext tenant, ICompanyContext company, IClock clock)
    : ICommandHandler<SubmitCheckoutCommand, Guid>
{
    public async Task<Guid> HandleAsync(SubmitCheckoutCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        RegisterChannelCommandHandler.EnsureCompany(company, command.CompanyId, "checkout");
        CheckoutIntent? existing = await intents.FindByIdempotencyKeyAsync(command.OwnerKey, command.IdempotencyKey, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            if (!string.Equals(existing.ContentFingerprint, command.ContentFingerprint.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Checkout idempotency key was reused with different content.");
            }
            return existing.Id;
        }
        CommerceBasket basket = await baskets.FindAsync(command.BasketId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Basket not found.");
        if (basket.CompanyId != command.CompanyId || basket.Status != CommerceBasketStatus.Open ||
            !string.Equals(basket.OwnerKey, command.OwnerKey.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Basket is not available to this owner.");
        }
        if ((await lines.ListForBasketAsync(basket.Id, cancellationToken).ConfigureAwait(false)).Count == 0)
        {
            throw new InvalidOperationException("Cannot submit an empty basket.");
        }
        CheckoutIntent intent = CheckoutIntent.Submit(tenant.TenantId, command.CompanyId, basket.ChannelConnectionId,
            basket.Id, command.OwnerKey, command.IdempotencyKey, command.ContentFingerprint, clock.UtcNow);
        intents.Add(intent);
        return intent.Id;
    }
}

public sealed record GetCheckoutStatusQuery(Guid CheckoutId, Guid CompanyId, string OwnerKey)
    : IQuery<CheckoutStatusResult?>;

public sealed record CheckoutStatusResult(Guid OperationId, string Status, DateTimeOffset ExpiresAt,
    DateTimeOffset? DecidedAt, string? DecisionReason);

public sealed class GetCheckoutStatusQueryHandler(ICheckoutIntentRepository intents, ICompanyContext company, IClock clock)
    : IQueryHandler<GetCheckoutStatusQuery, CheckoutStatusResult?>
{
    public async Task<CheckoutStatusResult?> HandleAsync(GetCheckoutStatusQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        RegisterChannelCommandHandler.EnsureCompany(company, query.CompanyId, "checkout");
        CheckoutIntent? intent = await intents.FindAsync(query.CheckoutId, cancellationToken).ConfigureAwait(false);
        if (intent is null || intent.CompanyId != query.CompanyId ||
            !string.Equals(intent.OwnerKey, query.OwnerKey.Trim(), StringComparison.Ordinal))
        {
            return null;
        }
        string status = intent.Status.ToString();
        if (intent.Status == CheckoutIntentStatus.Pending && clock.UtcNow >= intent.ExpiresAtUtc)
        {
            status = CheckoutIntentStatus.Expired.ToString();
        }
        return new CheckoutStatusResult(intent.Id, status, intent.ExpiresAtUtc, intent.DecidedAtUtc, intent.DecisionReason);
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record ConfirmCheckoutCommand(Guid CheckoutId, Guid CompanyId) : ICommand;

public sealed class ConfirmCheckoutCommandHandler(ICheckoutIntentRepository intents, ICompanyContext company, IClock clock)
    : ICommandHandler<ConfirmCheckoutCommand, Unit>
{
    public async Task<Unit> HandleAsync(ConfirmCheckoutCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        RegisterChannelCommandHandler.EnsureCompany(company, command.CompanyId, "checkout");
        CheckoutIntent intent = await intents.FindAsync(command.CheckoutId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Checkout intent not found.");
        if (intent.CompanyId != command.CompanyId)
        {
            throw new InvalidOperationException("Checkout intent is not in the active company.");
        }
        intent.Confirm(clock.UtcNow);
        return Unit.Value;
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record RejectCheckoutCommand(Guid CheckoutId, Guid CompanyId, string Reason) : ICommand;

public sealed class RejectCheckoutCommandHandler(ICheckoutIntentRepository intents, ICompanyContext company, IClock clock)
    : ICommandHandler<RejectCheckoutCommand, Unit>
{
    public async Task<Unit> HandleAsync(RejectCheckoutCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        RegisterChannelCommandHandler.EnsureCompany(company, command.CompanyId, "checkout");
        CheckoutIntent intent = await intents.FindAsync(command.CheckoutId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Checkout intent not found.");
        if (intent.CompanyId != command.CompanyId)
        {
            throw new InvalidOperationException("Checkout intent is not in the active company.");
        }
        intent.Reject(command.Reason, clock.UtcNow);
        return Unit.Value;
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record AddBasketLineCommand(Guid BasketId, Guid CompanyId, string OwnerKey, Guid PublishedProductId,
    decimal Quantity, decimal AdvisoryUnitPrice, string Currency) : ICommand<Guid>;

public sealed class AddBasketLineCommandHandler(
    ICommerceBasketRepository baskets, ICommerceBasketLineRepository lines, IPublishedProductRepository products,
    ITenantContext tenant, ICompanyContext company)
    : ICommandHandler<AddBasketLineCommand, Guid>
{
    public async Task<Guid> HandleAsync(AddBasketLineCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        RegisterChannelCommandHandler.EnsureCompany(company, command.CompanyId, "basket line");
        CommerceBasket basket = await baskets.FindAsync(command.BasketId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Basket not found.");
        if (basket.CompanyId != command.CompanyId || basket.Status != CommerceBasketStatus.Open ||
            !string.Equals(basket.OwnerKey, command.OwnerKey.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Basket is not available to this owner.");
        }
        IReadOnlyList<PublishedProduct> published = await products.ListAsync(basket.ChannelConnectionId, 200, cancellationToken).ConfigureAwait(false);
        PublishedProduct product = published.FirstOrDefault(x => x.Id == command.PublishedProductId)
            ?? throw new InvalidOperationException("Published product not found for this channel.");
        if (!string.Equals(product.Currency, command.Currency.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Basket currency does not match the published product.");
        }
        CommerceBasketLine line = CommerceBasketLine.Add(tenant.TenantId, command.CompanyId, basket.Id, product.Id,
            command.Quantity, command.AdvisoryUnitPrice, product.Price, product.Currency);
        lines.Add(line);
        return line.Id;
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record OpenBasketCommand(Guid ChannelId, Guid CompanyId, string OwnerKey) : ICommand<Guid>;

public sealed class OpenBasketCommandHandler(
    IChannelConnectionRepository channels, ICommerceBasketRepository baskets, ITenantContext tenant, ICompanyContext company, IClock clock)
    : ICommandHandler<OpenBasketCommand, Guid>
{
    public async Task<Guid> HandleAsync(OpenBasketCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        RegisterChannelCommandHandler.EnsureCompany(company, command.CompanyId, "basket");
        ChannelConnection channel = await channels.FindAsync(command.ChannelId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Storefront channel not found.");
        if (channel.CompanyId != command.CompanyId || channel.Status != ChannelConnectionStatus.Active)
        {
            throw new InvalidOperationException("Storefront channel is not active for the selected company.");
        }
        CommerceBasket basket = CommerceBasket.Open(tenant.TenantId, command.CompanyId, channel.Id, command.OwnerKey, clock.UtcNow);
        baskets.Add(basket);
        return basket.Id;
    }
}

/// <summary>Public sell-facing catalogue result; never exposes domain persistence objects.</summary>
public sealed record StorefrontProductResult(Guid Id, string Sku, string Name, string? Description,
    decimal Price, string Currency, decimal Available, DateTimeOffset AvailabilityAsAt, int Version);

public sealed record ListStorefrontProductsQuery(string Host, int Limit = 50)
    : IQuery<IReadOnlyList<StorefrontProductResult>>;

public sealed class ListStorefrontProductsQueryHandler(
    IChannelConnectionRepository channels, IPublishedProductRepository products)
    : IQueryHandler<ListStorefrontProductsQuery, IReadOnlyList<StorefrontProductResult>>
{
    public async Task<IReadOnlyList<StorefrontProductResult>> HandleAsync(
        ListStorefrontProductsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.Host);
        int limit = Math.Clamp(query.Limit, 1, 200);
        ChannelConnection channel = await channels.FindByHostAsync(query.Host, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Storefront channel not found.");
        if (channel.Status != ChannelConnectionStatus.Active)
        {
            throw new InvalidOperationException("Storefront channel is not active.");
        }
        return (await products.ListAsync(channel.Id, limit, cancellationToken).ConfigureAwait(false))
            .Select(x => new StorefrontProductResult(x.Id, x.Sku, x.Name, x.Description, x.Price, x.Currency,
                x.Available, x.AvailabilityAsAt, x.Version)).ToList();
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record RegisterChannelCommand(Guid CompanyId, string Code, string Host) : ICommand<Guid>;

public sealed class RegisterChannelCommandHandler(IChannelConnectionRepository channels, ITenantContext tenant, ICompanyContext company)
    : ICommandHandler<RegisterChannelCommand, Guid>
{
    public Task<Guid> HandleAsync(RegisterChannelCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        EnsureCompany(company, command.CompanyId, "channel");
        ChannelConnection channel = ChannelConnection.Register(tenant.TenantId, command.CompanyId, command.Code, command.Host);
        channel.Activate();
        channels.Add(channel);
        return Task.FromResult(channel.Id);
    }

    internal static void EnsureCompany(ICompanyContext company, Guid expected, string resource)
    {
        if (company.CompanyId is not { } active || active != expected)
        {
            throw new InvalidOperationException($"The {resource} company is not the active company.");
        }
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record PublishProductCommand(Guid ChannelId, Guid CompanyId, Guid? ItemId, Guid? ItemVariantId,
    string Sku, string Name, string? Description, decimal Price, string Currency, decimal Available,
    DateTimeOffset AvailabilityAsAt, int Version) : ICommand<Guid>;

public sealed class PublishProductCommandHandler(
    IChannelConnectionRepository channels, IPublishedProductRepository products, ITenantContext tenant, ICompanyContext company)
    : ICommandHandler<PublishProductCommand, Guid>
{
    public async Task<Guid> HandleAsync(PublishProductCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        RegisterChannelCommandHandler.EnsureCompany(company, command.CompanyId, "product");
        // The repository is tenant-filtered, and the company check prevents a caller from using an
        // otherwise valid channel id while another company is active.
        ChannelConnection channel = await channels.FindAsync(command.ChannelId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Storefront channel not found.");
        if (channel.CompanyId != command.CompanyId || channel.Status != ChannelConnectionStatus.Active)
        {
            throw new InvalidOperationException("Storefront channel is not active for the selected company.");
        }
        PublishedProduct product = PublishedProduct.Publish(tenant.TenantId, command.CompanyId, channel.Id,
            command.ItemId, command.ItemVariantId, command.Sku, command.Name, command.Description, command.Price,
            command.Currency, command.Available, command.AvailabilityAsAt, command.Version);
        products.Add(product);
        return product.Id;
    }
}
