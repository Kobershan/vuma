using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Sales;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Sales;

namespace VumaRetail.Application.Sales.Commands;

/// <summary>
/// Creates a special.
/// </summary>
/// <remarks>
/// One command for all five kinds rather than five commands, because the shape of the request is the
/// same and only the reward parameters differ — and because a shop's specials screen is one form with a
/// kind selector, so five endpoints would be five ways to reach the same button. The handler dispatches
/// to the domain factory for the declared kind, which is where the parameter rules are enforced
/// (<c>Promotion.EnsureParametersMatchKind</c>); this command's validator only catches what is
/// malformed before a tenant is even known.
/// </remarks>
/// <param name="Code">The promotion code, unique per tenant. Upper-cased on the way in.</param>
/// <param name="Name">The name, as it should read on a receipt.</param>
/// <param name="Kind">What shape of reward this is.</param>
/// <param name="EffectiveFrom">The first day it runs.</param>
/// <param name="EffectiveTo">The last day, or <c>null</c> for open-ended.</param>
/// <param name="DiscountPercentage">The percentage off, for <see cref="PromotionKind.PercentageOff"/>.</param>
/// <param name="RewardAmount">
/// The amount off, the fixed price, or the bundle price, depending on the kind. Exactly one of the
/// monetary kinds ever reads it.
/// </param>
/// <param name="RequiredQuantity">The 3 in "3 for R50", the X in "buy X get Y free".</param>
/// <param name="FreeQuantity">The Y in "buy X get Y free".</param>
/// <param name="Priority">Which promotion applies first when several match. Higher wins.</param>
/// <param name="IsExclusive">True to stop lower-priority promotions once this one fires.</param>
/// <param name="Days">The days of the week it runs on, or <c>null</c> for every day.</param>
/// <param name="StartsAt">When it starts each day, store-local, or <c>null</c> for all day.</param>
/// <param name="EndsAt">When it stops each day, store-local, or <c>null</c> for all day.</param>
/// <param name="StoreId">The store it runs at, or <c>null</c> for every store.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record CreatePromotionCommand(
    string Code,
    string Name,
    PromotionKind Kind,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo = null,
    decimal? DiscountPercentage = null,
    Money? RewardAmount = null,
    decimal? RequiredQuantity = null,
    decimal? FreeQuantity = null,
    int Priority = 0,
    bool IsExclusive = false,
    PromotionDays? Days = null,
    TimeOnly? StartsAt = null,
    TimeOnly? EndsAt = null,
    Guid? StoreId = null) : ICommand<Guid>;

/// <summary>Rejects a malformed create-promotion command before it reaches the handler.</summary>
public sealed class CreatePromotionCommandValidator : AbstractValidator<CreatePromotionCommand>
{
    /// <summary>Builds the rules.</summary>
    public CreatePromotionCommandValidator()
    {
        RuleFor(command => command.Code).NotEmpty().MaximumLength(32);
        RuleFor(command => command.Name).NotEmpty().MaximumLength(128);
        RuleFor(command => command.Kind).IsInEnum();

        RuleFor(command => command.DiscountPercentage)
            .InclusiveBetween(0m, 100m)
            .When(command => command.DiscountPercentage is not null);

        RuleFor(command => command.RewardAmount!.Value.Amount)
            .GreaterThanOrEqualTo(0m)
            .When(command => command.RewardAmount is not null);

        RuleFor(command => command.RequiredQuantity)
            .GreaterThan(0m)
            .When(command => command.RequiredQuantity is not null);

        RuleFor(command => command.FreeQuantity)
            .GreaterThan(0m)
            .When(command => command.FreeQuantity is not null);
    }
}

/// <summary>Creates the promotion, refusing a code the tenant already uses.</summary>
/// <param name="promotions">Promotion lookup and insertion.</param>
/// <param name="tenant">The ambient tenant and store.</param>
public sealed class CreatePromotionCommandHandler(IPromotionRepository promotions, ITenantContext tenant)
    : ICommandHandler<CreatePromotionCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(
        CreatePromotionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await promotions.CodeExistsAsync(command.Code, cancellationToken).ConfigureAwait(false))
        {
            throw SalesConflictException.PromotionCode(command.Code);
        }

        Promotion created = Build(command, tenant.TenantId);

        if (command.Days is not null || command.StartsAt is not null || command.EndsAt is not null)
        {
            created.RestrictToWindow(command.Days, command.StartsAt, command.EndsAt);
        }

        promotions.Add(created);

        return created.Id;
    }

    private static Promotion Build(CreatePromotionCommand command, Guid tenantId) => command.Kind switch
    {
        PromotionKind.PercentageOff => Promotion.PercentageOff(
            tenantId, command.StoreId, command.Code, command.Name,
            Require(command.DiscountPercentage, command.Kind, "needs a percentage"),
            command.EffectiveFrom, command.EffectiveTo, command.Priority, command.IsExclusive),

        PromotionKind.AmountOff => Promotion.AmountOff(
            tenantId, command.StoreId, command.Code, command.Name,
            Require(command.RewardAmount, command.Kind, "needs an amount off"),
            command.EffectiveFrom, command.EffectiveTo, command.Priority, command.IsExclusive),

        PromotionKind.FixedPrice => Promotion.FixedPrice(
            tenantId, command.StoreId, command.Code, command.Name,
            Require(command.RewardAmount, command.Kind, "needs a fixed unit price"),
            command.EffectiveFrom, command.EffectiveTo, command.Priority, command.IsExclusive),

        PromotionKind.MultibuyForAmount => Promotion.MultibuyForAmount(
            tenantId, command.StoreId, command.Code, command.Name,
            Require(command.RequiredQuantity, command.Kind, "needs a positive bundle quantity"),
            Require(command.RewardAmount, command.Kind, "needs a bundle price"),
            command.EffectiveFrom, command.EffectiveTo, command.Priority, command.IsExclusive),

        PromotionKind.BuyXGetYFree => Promotion.BuyXGetYFree(
            tenantId, command.StoreId, command.Code, command.Name,
            Require(command.RequiredQuantity, command.Kind, "needs a positive required quantity"),
            Require(command.FreeQuantity, command.Kind, "needs a positive free quantity"),
            command.EffectiveFrom, command.EffectiveTo, command.Priority, command.IsExclusive),

        _ => throw SalesRuleException.PromotionParameterMismatch(
            command.Kind, "is not a kind this build knows"),
    };

    /// <summary>
    /// Unwraps a parameter the declared kind needs, failing with the domain's own message rather than
    /// a null-reference somewhere further in.
    /// </summary>
    private static T Require<T>(T? value, PromotionKind kind, string expected)
        where T : struct
        => value ?? throw SalesRuleException.PromotionParameterMismatch(kind, expected);
}

/// <summary>Renames a promotion, re-ranks it and moves its effective window.</summary>
/// <param name="PromotionId">The promotion.</param>
/// <param name="Name">The new name.</param>
/// <param name="Priority">The new priority.</param>
/// <param name="IsExclusive">Whether it stops lower-priority promotions.</param>
/// <param name="EffectiveFrom">The new first day.</param>
/// <param name="EffectiveTo">The new last day, or <c>null</c>.</param>
/// <param name="Days">The days it runs on, or <c>null</c> for every day.</param>
/// <param name="StartsAt">When it starts each day, or <c>null</c> for all day.</param>
/// <param name="EndsAt">When it stops each day, or <c>null</c> for all day.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record AmendPromotionCommand(
    Guid PromotionId,
    string Name,
    int Priority,
    bool IsExclusive,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo = null,
    PromotionDays? Days = null,
    TimeOnly? StartsAt = null,
    TimeOnly? EndsAt = null) : ICommand;

/// <summary>Rejects a malformed amend command before it reaches the handler.</summary>
public sealed class AmendPromotionCommandValidator : AbstractValidator<AmendPromotionCommand>
{
    /// <summary>Builds the rules.</summary>
    public AmendPromotionCommandValidator()
    {
        RuleFor(command => command.PromotionId).NotEmpty();
        RuleFor(command => command.Name).NotEmpty().MaximumLength(128);
    }
}

/// <summary>Amends the promotion.</summary>
/// <param name="promotions">Promotion lookup.</param>
public sealed class AmendPromotionCommandHandler(IPromotionRepository promotions)
    : ICommandHandler<AmendPromotionCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(
        AmendPromotionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Promotion promotion = await promotions.FindAsync(command.PromotionId, cancellationToken).ConfigureAwait(false)
            ?? throw new SalesNotFoundException("promotion", command.PromotionId);

        promotion.Amend(
            command.Name, command.Priority, command.IsExclusive, command.EffectiveFrom, command.EffectiveTo);

        promotion.RestrictToWindow(command.Days, command.StartsAt, command.EndsAt);

        return Unit.Value;
    }
}

/// <summary>Adds something for a promotion to apply to — an item, a variant, or a whole category.</summary>
/// <param name="PromotionId">The promotion.</param>
/// <param name="ItemId">The item, when the promotion targets one.</param>
/// <param name="ItemVariantId">The variant, when it targets one.</param>
/// <param name="CategoryCode">The category, when it targets a whole shelf.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record AddPromotionLineCommand(
    Guid PromotionId,
    Guid? ItemId = null,
    Guid? ItemVariantId = null,
    string? CategoryCode = null) : ICommand<Guid>;

/// <summary>Rejects a malformed add-line command before it reaches the handler.</summary>
public sealed class AddPromotionLineCommandValidator : AbstractValidator<AddPromotionLineCommand>
{
    /// <summary>Builds the rules.</summary>
    public AddPromotionLineCommandValidator()
    {
        RuleFor(command => command.PromotionId).NotEmpty();
        RuleFor(command => command.CategoryCode).MaximumLength(64);
        RuleFor(command => command).Must(HaveExactlyOneTarget)
            .WithMessage("Exactly one of ItemId, ItemVariantId or CategoryCode must be set.");
    }

    private static bool HaveExactlyOneTarget(AddPromotionLineCommand command)
        => (command.ItemId is not null ? 1 : 0)
            + (command.ItemVariantId is not null ? 1 : 0)
            + (string.IsNullOrWhiteSpace(command.CategoryCode) ? 0 : 1) == 1;
}

/// <summary>Adds the target.</summary>
/// <param name="promotions">Promotion lookup.</param>
public sealed class AddPromotionLineCommandHandler(IPromotionRepository promotions)
    : ICommandHandler<AddPromotionLineCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(
        AddPromotionLineCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Promotion promotion = await promotions.FindAsync(command.PromotionId, cancellationToken).ConfigureAwait(false)
            ?? throw new SalesNotFoundException("promotion", command.PromotionId);

        PromotionLine created = promotion.AddLine(
            command.ItemId, command.ItemVariantId, command.CategoryCode);

        return created.Id;
    }
}

/// <summary>Retires a promotion. Nothing is deleted (§7 rule 8).</summary>
/// <param name="PromotionId">The promotion.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record DeactivatePromotionCommand(Guid PromotionId) : ICommand;

/// <summary>Rejects a malformed deactivate command before it reaches the handler.</summary>
public sealed class DeactivatePromotionCommandValidator : AbstractValidator<DeactivatePromotionCommand>
{
    /// <summary>Builds the rules.</summary>
    public DeactivatePromotionCommandValidator() => RuleFor(command => command.PromotionId).NotEmpty();
}

/// <summary>Deactivates the promotion.</summary>
/// <param name="promotions">Promotion lookup.</param>
public sealed class DeactivatePromotionCommandHandler(IPromotionRepository promotions)
    : ICommandHandler<DeactivatePromotionCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(
        DeactivatePromotionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Promotion promotion = await promotions.FindAsync(command.PromotionId, cancellationToken).ConfigureAwait(false)
            ?? throw new SalesNotFoundException("promotion", command.PromotionId);

        promotion.Deactivate();

        return Unit.Value;
    }
}

/// <summary>Brings a retired promotion back into use.</summary>
/// <param name="PromotionId">The promotion.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record ActivatePromotionCommand(Guid PromotionId) : ICommand;

/// <summary>Rejects a malformed activate command before it reaches the handler.</summary>
public sealed class ActivatePromotionCommandValidator : AbstractValidator<ActivatePromotionCommand>
{
    /// <summary>Builds the rules.</summary>
    public ActivatePromotionCommandValidator() => RuleFor(command => command.PromotionId).NotEmpty();
}

/// <summary>Activates the promotion.</summary>
/// <param name="promotions">Promotion lookup.</param>
public sealed class ActivatePromotionCommandHandler(IPromotionRepository promotions)
    : ICommandHandler<ActivatePromotionCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(
        ActivatePromotionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Promotion promotion = await promotions.FindAsync(command.PromotionId, cancellationToken).ConfigureAwait(false)
            ?? throw new SalesNotFoundException("promotion", command.PromotionId);

        promotion.Activate();

        return Unit.Value;
    }
}
