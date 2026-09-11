#pragma warning disable CS1591, IDE0011, CA1062
using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Connect;

namespace VumaRetail.Application.Connect;

[CommandSideEffect(SideEffect.Write)]
public sealed record IssueConnectionCodeCommand(string Code, int Uses, DateTimeOffset ExpiresAt, string? PriceTier, string? Territory, bool GrantsPortalAccess) : ICommand<ConnectCodeResult>;

public sealed class IssueConnectionCodeCommandValidator : AbstractValidator<IssueConnectionCodeCommand>
{
    public IssueConnectionCodeCommandValidator() { RuleFor(x => x.Code).NotEmpty().MaximumLength(64); RuleFor(x => x.Uses).GreaterThan(0); RuleFor(x => x.ExpiresAt).GreaterThan(DateTimeOffset.UtcNow); }
}

public sealed class IssueConnectionCodeCommandHandler(IConnectionCodeRepository codes, ITenantContext tenant, IClock clock) : ICommandHandler<IssueConnectionCodeCommand, ConnectCodeResult>
{
    public Task<ConnectCodeResult> HandleAsync(IssueConnectionCodeCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ConnectionCode code = ConnectionCode.Issue(tenant.TenantId, command.Code, command.Uses, command.ExpiresAt, command.PriceTier, command.Territory, command.GrantsPortalAccess, clock.UtcNow);
        codes.Add(code);
        return Task.FromResult(new ConnectCodeResult(code.Id, code.Code, code.ExpiresAt, code.MaxUses, code.PriceTier, code.Territory, code.GrantsPortalAccess));
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record RedeemConnectionCodeCommand(string Code, string? RetailerAccountReference) : ICommand<ConnectConnectionResult>;

public sealed class RedeemConnectionCodeCommandValidator : AbstractValidator<RedeemConnectionCodeCommand>
{
    public RedeemConnectionCodeCommandValidator() => RuleFor(x => x.Code).NotEmpty().MaximumLength(64);
}

public sealed class RedeemConnectionCodeCommandHandler(IConnectionCodeRepository codes, ITradingConnectionRepository connections, ITenantContext tenant, IClock clock) : ICommandHandler<RedeemConnectionCodeCommand, ConnectConnectionResult>
{
    public async Task<ConnectConnectionResult> HandleAsync(RedeemConnectionCodeCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ConnectionCode code = await codes.FindByCodeAsync(command.Code, cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("Connection code not found.");
        code.Redeem(clock.UtcNow);
        TradingConnection connection = TradingConnection.Request(code.TenantId, tenant.TenantId, null, command.RetailerAccountReference, clock.UtcNow);
        connections.Add(connection);
        return ToResult(connection);
    }
    internal static ConnectConnectionResult ToResult(TradingConnection x) => new(x.Id, x.SupplierTenantId, x.RetailerTenantId, x.Status, x.Currency, x.CreditLimit, x.LeadTimeDays, x.MinimumOrderValue);
}

[CommandSideEffect(SideEffect.Write)]
public sealed record AcceptTradingConnectionCommand(Guid ConnectionId, string Currency, decimal CreditLimit, int LeadTimeDays, decimal MinimumOrderValue) : ICommand<ConnectConnectionResult>;

public sealed class AcceptTradingConnectionCommandHandler(ITradingConnectionRepository connections, ITenantContext tenant, IClock clock) : ICommandHandler<AcceptTradingConnectionCommand, ConnectConnectionResult>
{
    public async Task<ConnectConnectionResult> HandleAsync(AcceptTradingConnectionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        TradingConnection connection = await connections.FindForTenantAsync(command.ConnectionId, tenant.TenantId, cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("Connection not found.");
        if (connection.RetailerTenantId != tenant.TenantId)
        {
            throw new UnauthorizedAccessException("Only the connected retailer can accept this connection.");
        }
        connection.Accept(command.Currency, command.CreditLimit, command.LeadTimeDays, command.MinimumOrderValue, clock.UtcNow);
        return RedeemConnectionCodeCommandHandler.ToResult(connection);
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record SuspendTradingConnectionCommand(Guid ConnectionId) : ICommand;
public sealed class SuspendTradingConnectionCommandHandler(ITradingConnectionRepository connections, ITenantContext tenant, IClock clock) : ICommandHandler<SuspendTradingConnectionCommand, Unit>
{
    public async Task<Unit> HandleAsync(SuspendTradingConnectionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        TradingConnection c = await connections.FindForTenantAsync(command.ConnectionId, tenant.TenantId, cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("Connection not found.");
        c.Suspend(clock.UtcNow);
        return Unit.Value;
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record EndTradingConnectionCommand(Guid ConnectionId) : ICommand;
public sealed class EndTradingConnectionCommandHandler(ITradingConnectionRepository connections, ITenantContext tenant, IClock clock) : ICommandHandler<EndTradingConnectionCommand, Unit>
{
    public async Task<Unit> HandleAsync(EndTradingConnectionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        TradingConnection c = await connections.FindForTenantAsync(command.ConnectionId, tenant.TenantId, cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("Connection not found.");
        c.End(clock.UtcNow);
        return Unit.Value;
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record PublishCatalogueCommand(Guid ConnectionId, int Version, DateTimeOffset EffectiveFrom, string? VersionNote, IReadOnlyList<PublishCatalogueLine> Lines) : ICommand<Guid>;
public sealed record PublishCatalogueLine(string SupplierSku, string Description, string? Barcode, int PackSize, decimal? MinimumOrderQuantity, int? LeadTimeDays);
public sealed class PublishCatalogueCommandHandler(ICataloguePublicationRepository publications, ITradingConnectionRepository connections, ITenantContext tenant) : ICommandHandler<PublishCatalogueCommand, Guid>
{
    public async Task<Guid> HandleAsync(PublishCatalogueCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        TradingConnection c = await connections.FindForTenantAsync(command.ConnectionId, tenant.TenantId, cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("Connection not found.");
        if (c.SupplierTenantId != tenant.TenantId || c.Status != TradingConnectionStatus.Active)
        {
            throw new UnauthorizedAccessException("Only the active supplier can publish.");
        }
        CataloguePublication p = CataloguePublication.Publish(tenant.TenantId, c.Id, command.Version, command.EffectiveFrom, command.VersionNote);
        foreach (PublishCatalogueLine l in command.Lines)
        {
            p.AddLine(l.SupplierSku, l.Description, l.Barcode, l.PackSize, l.MinimumOrderQuantity, l.LeadTimeDays);
        }
        publications.Add(p);
        return p.Id;
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record PublishPriceProposalCommand(Guid ConnectionId, DateTimeOffset EffectiveFrom, DateTimeOffset? ExpiresAt, string? VersionNote, IReadOnlyList<PublishPriceLine> Lines) : ICommand<Guid>;
public sealed record PublishPriceLine(string SupplierSku, decimal UnitPrice, string Currency, decimal? MinimumOrderQuantity, int? LeadTimeDays);
public sealed class PublishPriceProposalCommandHandler(IPriceProposalRepository proposals, ITradingConnectionRepository connections, ITenantContext tenant) : ICommandHandler<PublishPriceProposalCommand, Guid>
{
    public async Task<Guid> HandleAsync(PublishPriceProposalCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        TradingConnection c = await connections.FindForTenantAsync(command.ConnectionId, tenant.TenantId, cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("Connection not found.");
        if (c.SupplierTenantId != tenant.TenantId || c.Status != TradingConnectionStatus.Active)
        {
            throw new UnauthorizedAccessException("Only the active supplier can publish.");
        }
        PriceProposal p = PriceProposal.Publish(tenant.TenantId, c.Id, command.EffectiveFrom, command.ExpiresAt, command.VersionNote);
        foreach (PublishPriceLine l in command.Lines)
        {
            p.AddLine(l.SupplierSku, l.UnitPrice, l.Currency, l.MinimumOrderQuantity, l.LeadTimeDays);
        }
        proposals.Add(p);
        return p.Id;
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record DecidePriceProposalCommand(Guid ProposalId, bool Accept, IReadOnlyCollection<Guid>? LineIds, string? Reason) : ICommand;
public sealed class DecidePriceProposalCommandHandler(IPriceProposalRepository proposals, ITradingConnectionRepository connections, ITenantContext tenant, IClock clock) : ICommandHandler<DecidePriceProposalCommand, Unit>
{
    public async Task<Unit> HandleAsync(DecidePriceProposalCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        PriceProposal p = await proposals.FindAsync(command.ProposalId, cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("Price proposal not found.");
        TradingConnection? c = await connections.FindForTenantAsync(p.ConnectionId, tenant.TenantId, cancellationToken).ConfigureAwait(false);
        if (c is null || c.RetailerTenantId != tenant.TenantId || c.Status != TradingConnectionStatus.Active)
        {
            throw new UnauthorizedAccessException("Only the connected retailer can decide a proposal.");
        }
        if (command.Accept)
        {
            p.Accept(command.LineIds, clock.UtcNow);
        }
        else
        {
            p.Reject(clock.UtcNow);
        }
        return Unit.Value;
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record RollbackPriceProposalCommand(Guid ProposalId) : ICommand;
public sealed class RollbackPriceProposalCommandHandler(IPriceProposalRepository proposals, ITenantContext tenant, IClock clock) : ICommandHandler<RollbackPriceProposalCommand, Unit>
{
    public async Task<Unit> HandleAsync(RollbackPriceProposalCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        PriceProposal p = await proposals.FindAsync(command.ProposalId, cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("Price proposal not found.");
        if (p.TenantId != tenant.TenantId)
        {
            throw new UnauthorizedAccessException("Only the supplier can roll back a proposal.");
        }
        p.Rollback(clock.UtcNow);
        return Unit.Value;
    }
}
