#pragma warning disable CS1591, IDE0011
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Domain.Assets;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.Assets;

[CommandSideEffect(SideEffect.Write)]
public sealed record CreateFixedAssetCommand(Guid CompanyId, string AssetNumber, string Description,
    DateOnly AcquiredOn, decimal Cost, string Currency) : ICommand<Guid>;

public sealed class CreateFixedAssetCommandHandler(IAssetRepository assets, ITenantContext tenant,
    ICompanyContext company) : ICommandHandler<CreateFixedAssetCommand, Guid>
{
    public Task<Guid> HandleAsync(CreateFixedAssetCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        EnsureCompany(company, command.CompanyId);
        FixedAsset asset = FixedAsset.Create(tenant.TenantId, null, command.CompanyId, command.AssetNumber,
            command.Description, command.AcquiredOn, new Money(command.Cost, command.Currency));
        assets.Add(asset);
        return Task.FromResult(asset.Id);
    }

    internal static void EnsureCompany(ICompanyContext company, Guid expected)
    {
        if (company.CompanyId is not { } active || active != expected)
            throw new InvalidOperationException("The asset company is not the active company.");
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record PlaceAssetInServiceCommand(Guid CompanyId, Guid AssetId) : ICommand;

public sealed class PlaceAssetInServiceCommandHandler(IAssetRepository assets, ICompanyContext company)
    : ICommandHandler<PlaceAssetInServiceCommand, Unit>
{
    public async Task<Unit> HandleAsync(PlaceAssetInServiceCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        FixedAsset asset = await assets.FindAssetAsync(command.AssetId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Fixed asset not found.");
        CreateFixedAssetCommandHandler.EnsureCompany(company, command.CompanyId);
        if (asset.CompanyId != command.CompanyId) throw new InvalidOperationException("The asset company is not the active company.");
        asset.PlaceInService();
        return Unit.Value;
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record DisposeFixedAssetCommand(Guid CompanyId, Guid AssetId, DateOnly DisposedOn) : ICommand;

public sealed class DisposeFixedAssetCommandHandler(IAssetRepository assets, ICompanyContext company)
    : ICommandHandler<DisposeFixedAssetCommand, Unit>
{
    public async Task<Unit> HandleAsync(DisposeFixedAssetCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        FixedAsset asset = await assets.FindAssetAsync(command.AssetId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Fixed asset not found.");
        CreateFixedAssetCommandHandler.EnsureCompany(company, command.CompanyId);
        if (asset.CompanyId != command.CompanyId) throw new InvalidOperationException("The asset company is not the active company.");
        asset.Dispose(command.DisposedOn);
        return Unit.Value;
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record CreateAssetBookCommand(Guid CompanyId, Guid AssetId, string BookName, DateOnly InServiceOn,
    decimal ResidualValue, string Currency, int UsefulLifeMonths) : ICommand<Guid>;

public sealed class CreateAssetBookCommandHandler(IAssetRepository assets, ITenantContext tenant,
    ICompanyContext company) : ICommandHandler<CreateAssetBookCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateAssetBookCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        CreateFixedAssetCommandHandler.EnsureCompany(company, command.CompanyId);
        FixedAsset asset = await assets.FindAssetAsync(command.AssetId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Fixed asset not found.");
        if (asset.CompanyId != command.CompanyId) throw new InvalidOperationException("The asset company is not the active company.");
        if (await assets.FindBookAsync(command.AssetId, command.BookName, cancellationToken).ConfigureAwait(false) is not null)
            throw new InvalidOperationException("An asset book with this name already exists.");
        AssetBook book = AssetBook.Create(tenant.TenantId, null, command.CompanyId, command.AssetId, command.BookName,
            command.InServiceOn, new Money(command.ResidualValue, command.Currency), command.UsefulLifeMonths);
        assets.Add(book);
        return book.Id;
    }
}
