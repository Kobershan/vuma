using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Manufacturing;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.Manufacturing;

/// <summary>One component submitted while authoring a BOM.</summary>
public sealed record BillOfMaterialsLineInput(Guid ComponentItemId, Guid? ComponentVariantId, decimal Quantity, string UnitOfMeasure, decimal ScrapPercent = 0m, string? AlternateGroup = null);

/// <summary>Creates a draft BOM definition.</summary>
[CommandSideEffect(SideEffect.Write)]
public sealed record CreateBillOfMaterialsCommand(Guid CompanyId, Guid FinishedItemId, Guid? FinishedVariantId, int Version, string Name, IReadOnlyList<BillOfMaterialsLineInput> Lines) : ICommand<Guid>;

/// <summary>Validates a BOM creation request.</summary>
public sealed class CreateBillOfMaterialsCommandValidator : AbstractValidator<CreateBillOfMaterialsCommand>
{
    /// <summary>Builds validation rules.</summary>
    public CreateBillOfMaterialsCommandValidator()
    {
        RuleFor(command => command.CompanyId).NotEmpty();
        RuleFor(command => command.FinishedItemId).NotEmpty();
        RuleFor(command => command.Version).GreaterThan(0);
        RuleFor(command => command.Name).NotEmpty().MaximumLength(256);
        RuleFor(command => command.Lines).NotEmpty();
        RuleForEach(command => command.Lines).ChildRules(line =>
        {
            line.RuleFor(input => input.ComponentItemId).NotEmpty();
            line.RuleFor(input => input.Quantity).GreaterThan(0m);
            line.RuleFor(input => input.UnitOfMeasure).NotEmpty().MaximumLength(16);
            line.RuleFor(input => input.ScrapPercent).GreaterThanOrEqualTo(0m).LessThan(100m);
        });
    }
}

/// <summary>Creates a draft BOM and rejects a duplicate live version.</summary>
public sealed class CreateBillOfMaterialsCommandHandler(IBillOfMaterialsRepository boms, ITenantContext tenant)
    : ICommandHandler<CreateBillOfMaterialsCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(CreateBillOfMaterialsCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await boms.FindVersionAsync(command.FinishedItemId, command.FinishedVariantId, command.Version, cancellationToken).ConfigureAwait(false) is not null)
        {
            throw ManufacturingRuleException.DuplicateVersion(command.FinishedItemId, command.Version);
        }

        BillOfMaterials bom = BillOfMaterials.Create(tenant.TenantId, command.FinishedItemId, command.Version, command.Name, command.FinishedVariantId);
        bom.AssignCompany(command.CompanyId);
        foreach (BillOfMaterialsLineInput line in command.Lines)
        {
            bom.AddLine(line.ComponentItemId, new Quantity(line.Quantity, line.UnitOfMeasure), line.ScrapPercent, line.AlternateGroup, line.ComponentVariantId);
        }
        boms.Add(bom);
        return bom.Id;
    }
}

/// <summary>Publishes a draft BOM definition.</summary>
[CommandSideEffect(SideEffect.Write)]
public sealed record PublishBillOfMaterialsCommand(Guid BillOfMaterialsId) : ICommand;

/// <summary>Validates a publication request.</summary>
public sealed class PublishBillOfMaterialsCommandValidator : AbstractValidator<PublishBillOfMaterialsCommand>
{
    /// <summary>Builds validation rules.</summary>
    public PublishBillOfMaterialsCommandValidator() => RuleFor(command => command.BillOfMaterialsId).NotEmpty();
}

/// <summary>Publishes one tenant-scoped BOM.</summary>
public sealed class PublishBillOfMaterialsCommandHandler(IBillOfMaterialsRepository boms)
    : ICommandHandler<PublishBillOfMaterialsCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(PublishBillOfMaterialsCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        BillOfMaterials bom = await boms.FindAsync(command.BillOfMaterialsId, cancellationToken).ConfigureAwait(false)
            ?? throw ManufacturingRuleException.NotFound(command.BillOfMaterialsId);
        bom.Publish();
        return Unit.Value;
    }
}

/// <summary>Reads one BOM definition.</summary>
public sealed record GetBillOfMaterialsQuery(Guid BillOfMaterialsId) : IQuery<BillOfMaterials>;

/// <summary>Handles a tenant-scoped BOM read.</summary>
public sealed class GetBillOfMaterialsQueryHandler(IBillOfMaterialsRepository boms)
    : IQueryHandler<GetBillOfMaterialsQuery, BillOfMaterials>
{
    /// <inheritdoc />
    public async Task<BillOfMaterials> HandleAsync(GetBillOfMaterialsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return await boms.FindAsync(query.BillOfMaterialsId, cancellationToken).ConfigureAwait(false)
            ?? throw ManufacturingRuleException.NotFound(query.BillOfMaterialsId);
    }
}

/// <summary>Creates a draft production order with an offline-safe caller identity.</summary>
[CommandSideEffect(SideEffect.Write)]
public sealed record CreateProductionOrderCommand(
    Guid OperationId,
    Guid CompanyId,
    Guid FinishedItemId,
    decimal Quantity,
    string UnitOfMeasure,
    string OrderNumber,
    Guid BillOfMaterialsId) : ICommand<Guid>;

/// <summary>Creates a production order; release-time BOM data is not read until release.</summary>
public sealed class CreateProductionOrderCommandHandler(
    IProductionOrderRepository orders,
    ITenantContext tenant) : ICommandHandler<CreateProductionOrderCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(CreateProductionOrderCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.OperationId == Guid.Empty || command.CompanyId == Guid.Empty || command.FinishedItemId == Guid.Empty)
        {
            throw new ArgumentException("Production order identity and ownership are required.");
        }
        ProductionOrder? existing = await orders.FindAsync(command.OperationId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            if (!existing.MatchesCreateRequest(command.CompanyId, command.FinishedItemId,
                    new Quantity(command.Quantity, command.UnitOfMeasure), command.OrderNumber, command.BillOfMaterialsId))
            {
                throw ManufacturingRuleException.OperationPayloadConflict(command.OperationId);
            }
            return command.OperationId;
        }

        ProductionOrder order = ProductionOrder.Create(
            command.OperationId,
            tenant.TenantId,
            command.CompanyId,
            command.FinishedItemId,
            new Quantity(command.Quantity, command.UnitOfMeasure),
            command.OrderNumber,
            command.BillOfMaterialsId);
        orders.Add(order);
        return order.Id;
    }
}

/// <summary>Releases a production order against a published BOM snapshot.</summary>
[CommandSideEffect(SideEffect.Write)]
public sealed record ReleaseProductionOrderCommand(Guid ProductionOrderId, Guid BillOfMaterialsId) : ICommand;

/// <summary>Loads the order and BOM through tenant-scoped repositories before releasing it.</summary>
public sealed class ReleaseProductionOrderCommandHandler(
    IProductionOrderRepository orders,
    IBillOfMaterialsRepository boms,
    IClock clock) : ICommandHandler<ReleaseProductionOrderCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(ReleaseProductionOrderCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ProductionOrder order = await orders.FindAsync(command.ProductionOrderId, cancellationToken).ConfigureAwait(false)
            ?? throw ManufacturingRuleException.NotFound(command.ProductionOrderId);
        BillOfMaterials bom = await boms.FindAsync(command.BillOfMaterialsId, cancellationToken).ConfigureAwait(false)
            ?? throw ManufacturingRuleException.NotFound(command.BillOfMaterialsId);
        order.Release(bom, clock.UtcNow);
        return Unit.Value;
    }
}

/// <summary>Consumes one release-snapshot material requirement.</summary>
[CommandSideEffect(SideEffect.Write)]
public sealed record IssueProductionMaterialCommand(Guid ProductionOrderId, Guid OperationId, Guid ComponentItemId, Guid? ComponentVariantId, decimal Quantity, string UnitOfMeasure, decimal UnitCost, string Currency) : ICommand;

/// <summary>Handles one idempotent material issue.</summary>
public sealed class IssueProductionMaterialCommandHandler(IProductionOrderRepository orders)
    : ICommandHandler<IssueProductionMaterialCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(IssueProductionMaterialCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ProductionOrder order = await orders.FindAsync(command.ProductionOrderId, cancellationToken).ConfigureAwait(false)
            ?? throw ManufacturingRuleException.NotFound(command.ProductionOrderId);
        order.IssueMaterial(command.OperationId, command.ComponentItemId, command.ComponentVariantId,
            new Quantity(command.Quantity, command.UnitOfMeasure), new Money(command.UnitCost, command.Currency));
        return Unit.Value;
    }
}

/// <summary>Receives finished output against a production order.</summary>
[CommandSideEffect(SideEffect.Write)]
public sealed record ReceiveProductionOutputCommand(Guid ProductionOrderId, Guid OperationId, decimal Quantity, string UnitOfMeasure, decimal UnitCost, string Currency) : ICommand;

/// <summary>Handles one idempotent finished-output receipt.</summary>
public sealed class ReceiveProductionOutputCommandHandler(IProductionOrderRepository orders)
    : ICommandHandler<ReceiveProductionOutputCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(ReceiveProductionOutputCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ProductionOrder order = await orders.FindAsync(command.ProductionOrderId, cancellationToken).ConfigureAwait(false)
            ?? throw ManufacturingRuleException.NotFound(command.ProductionOrderId);
        order.ReceiveOutput(command.OperationId, new Quantity(command.Quantity, command.UnitOfMeasure), new Money(command.UnitCost, command.Currency));
        return Unit.Value;
    }
}

/// <summary>Records scrap against a production order.</summary>
[CommandSideEffect(SideEffect.Write)]
public sealed record RecordProductionScrapCommand(Guid ProductionOrderId, Guid OperationId, decimal Quantity, string UnitOfMeasure, decimal UnitCost, string Currency) : ICommand;

/// <summary>Handles one idempotent scrap record.</summary>
public sealed class RecordProductionScrapCommandHandler(IProductionOrderRepository orders)
    : ICommandHandler<RecordProductionScrapCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(RecordProductionScrapCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ProductionOrder order = await orders.FindAsync(command.ProductionOrderId, cancellationToken).ConfigureAwait(false)
            ?? throw ManufacturingRuleException.NotFound(command.ProductionOrderId);
        order.RecordScrap(command.OperationId, new Quantity(command.Quantity, command.UnitOfMeasure), new Money(command.UnitCost, command.Currency));
        return Unit.Value;
    }
}

/// <summary>Completes and closes a reconciled production order.</summary>
[CommandSideEffect(SideEffect.Write)]
public sealed record CloseProductionOrderCommand(Guid ProductionOrderId) : ICommand;

/// <summary>Handles completion and closure of a production order.</summary>
public sealed class CloseProductionOrderCommandHandler(IProductionOrderRepository orders)
    : ICommandHandler<CloseProductionOrderCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(CloseProductionOrderCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ProductionOrder order = await orders.FindAsync(command.ProductionOrderId, cancellationToken).ConfigureAwait(false)
            ?? throw ManufacturingRuleException.NotFound(command.ProductionOrderId);
        if (order.Status == ProductionOrderStatus.InProgress)
        {
            order.Complete();
        }
        order.Close();
        return Unit.Value;
    }
}
