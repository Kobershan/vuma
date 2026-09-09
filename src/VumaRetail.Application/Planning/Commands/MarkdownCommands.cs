using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Planning;
using VumaRetail.Application.Abstractions.Workflow;
using VumaRetail.Application.Inventory;
using VumaRetail.Domain.Planning;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.Planning.Commands;

/// <summary>One SKU to mark down.</summary>
/// <param name="ItemId">The item, or <c>null</c> for a variant.</param>
/// <param name="ItemVariantId">The variant, or <c>null</c> for an item.</param>
/// <param name="LocationId">Where the slow stock sits.</param>
/// <param name="ProposedDiscountPercent">The percentage off, 0–100 exclusive of 0.</param>
public sealed record MarkdownLineInput(
    Guid? ItemId,
    Guid? ItemVariantId,
    Guid LocationId,
    decimal ProposedDiscountPercent);

/// <summary>Opens a draft markdown plan. Every line must qualify under the planner.</summary>
[CommandSideEffect(SideEffect.Write)]
public sealed record CreateMarkdownPlanCommand(
    Guid CompanyId,
    string Code,
    string Reason,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    IReadOnlyList<MarkdownLineInput> Lines) : ICommand<Guid>;

/// <summary>Rejects a malformed markdown plan.</summary>
public sealed class CreateMarkdownPlanCommandValidator : AbstractValidator<CreateMarkdownPlanCommand>
{
    /// <summary>Builds the rules.</summary>
    public CreateMarkdownPlanCommandValidator()
    {
        RuleFor(command => command.CompanyId).NotEmpty();
        RuleFor(command => command.Code).NotEmpty().MaximumLength(32);
        RuleFor(command => command.Reason).NotEmpty().MaximumLength(1000);
        RuleFor(command => command.Lines).NotEmpty();
        RuleForEach(command => command.Lines).ChildRules(line =>
        {
            line.RuleFor(entry => entry.LocationId).NotEmpty();
            line.RuleFor(entry => entry).Must(HaveExactlyOneSku).WithMessage("Exactly one of ItemId or ItemVariantId must be set.");
            line.RuleFor(entry => entry.ProposedDiscountPercent).ExclusiveBetween(0m, 100m);
        });
    }

    private static bool HaveExactlyOneSku(MarkdownLineInput line)
        => (line.ItemId is null) != (line.ItemVariantId is null);
}

/// <summary>
/// Authors a draft plan with live decision inputs snapshotted per line: resolved price, average
/// cost basis, classification, sell-through and days of supply. A SKU with a live plan is refused;
/// a SKU the planner does not flag is refused — a markdown is a proposal about slow stock, and
/// the signals are what make it one.
/// </summary>
public sealed class CreateMarkdownPlanCommandHandler(
    IMarkdownPlanRepository plans,
    IDemandHistoryRepository history,
    IAbcXyzClassificationRepository classifications,
    IStockBalanceRepository balances,
    IPlanningPriceReader prices,
    IMarkdownPlanner planner,
    ITenantContext tenant,
    IClock clock) : ICommandHandler<CreateMarkdownPlanCommand, Guid>
{
    /// <summary>Weeks of demand behind sell-through and days of supply.</summary>
    public const int ReviewWeeks = 12;

    /// <inheritdoc />
    public async Task<Guid> HandleAsync(
        CreateMarkdownPlanCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await plans.FindByCodeAsync(command.Code, cancellationToken).ConfigureAwait(false) is not null)
        {
            throw PlanningRuleException.BadInput($"A markdown plan already uses code '{command.Code}'.");
        }

        MarkdownPlan plan = MarkdownPlan.Create(
            tenant.TenantId,
            command.CompanyId,
            command.Code,
            command.Reason,
            command.EffectiveFrom,
            command.EffectiveTo);

        DateOnly today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        DateOnly from = today.AddDays(-7 * ReviewWeeks);

        foreach (MarkdownLineInput input in command.Lines)
        {
            if (await plans
                    .HasLivePlanForSkuAsync(command.CompanyId, input.ItemId, input.ItemVariantId, cancellationToken)
                    .ConfigureAwait(false))
            {
                throw PlanningConflictException.LiveMarkdownPlanExists(SkuLabel(input));
            }

            PriceSnapshot? price = await prices
                .TryReadAsync(input.LocationId, input.ItemId, input.ItemVariantId, tenant.StoreId, today, cancellationToken)
                .ConfigureAwait(false)
                ?? throw PlanningRuleException.BadInput($"Cannot mark down {SkuLabel(input)}: it has no price.");

            AbcXyzClassification? classification = await classifications
                .LatestAsync(command.CompanyId, input.LocationId, input.ItemId, input.ItemVariantId, cancellationToken)
                .ConfigureAwait(false);

            IReadOnlyList<DemandHistory> rows = await history
                .ListSeriesAsync(
                    command.CompanyId, input.LocationId, input.ItemId, input.ItemVariantId, from, today, cancellationToken)
                .ConfigureAwait(false);

            decimal sold = rows.Sum(row => row.TotalQuantity);

            var balance = await balances
                .FindAsync(input.LocationId, input.ItemId, input.ItemVariantId, cancellationToken)
                .ConfigureAwait(false);

            decimal onHand = balance?.QuantityOnHand.Value ?? 0m;
            decimal sellThrough = sold + onHand <= 0m ? 0m : (sold / (sold + onHand)) * 100m;
            decimal averageDaily = sold / (ReviewWeeks * 7m);
            decimal daysOfSupply = averageDaily <= 0m ? 9999m : onHand / averageDaily;

            MarkdownVerdict verdict = planner.Evaluate(new MarkdownSignals(
                classification?.Abc, classification?.Xyz, sellThrough, daysOfSupply));

            if (!verdict.Propose)
            {
                throw PlanningRuleException.BadInput(
                    $"Cannot mark down {SkuLabel(input)}: sell-through {sellThrough:F1}% over {daysOfSupply:F0} days of supply does not qualify.");
            }

            plan.AddLine(
                input.ItemId,
                input.ItemVariantId,
                price.Price,
                input.ProposedDiscountPercent,
                price.Currency,
                classification is null ? null : $"{classification.Abc}/{classification.Xyz}",
                sellThrough,
                daysOfSupply);
        }

        plans.Add(plan);

        return plan.Id;
    }

    private static string SkuLabel(MarkdownLineInput input)
        => (input.ItemId ?? input.ItemVariantId!.Value).ToString("D");
}

/// <summary>Submits a draft plan to Stage 05 approval.</summary>
/// <param name="PlanId">The plan.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record SubmitMarkdownPlanCommand(Guid PlanId) : ICommand<Guid?>;

/// <summary>Rejects a malformed submit.</summary>
public sealed class SubmitMarkdownPlanCommandValidator : AbstractValidator<SubmitMarkdownPlanCommand>
{
    /// <summary>Builds the rules.</summary>
    public SubmitMarkdownPlanCommandValidator()
        => RuleFor(command => command.PlanId).NotEmpty();
}

/// <summary>
/// Submits through <see cref="IApprovalService"/> — planning implements no approval logic of its
/// own. When no policy gates the action the plan is approved immediately; otherwise it waits on
/// the returned request. Either way, nothing is priced yet.
/// </summary>
public sealed class SubmitMarkdownPlanCommandHandler(
    IMarkdownPlanRepository plans,
    IApprovalService approvals,
    ITenantContext tenant) : ICommandHandler<SubmitMarkdownPlanCommand, Guid?>
{
    /// <inheritdoc />
    public async Task<Guid?> HandleAsync(
        SubmitMarkdownPlanCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        MarkdownPlan plan = await plans
            .FindAsync(command.PlanId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new PlanningNotFoundException("markdown plan", command.PlanId);

        // A provisional request id so Submit (which needs one) can run before Evaluate answers.
        Guid provisional = Guid.NewGuid();
        plan.Submit(provisional);

        Money? amount = TotalMarkdownValue(plan);

        ApprovalOutcome outcome = await approvals
            .EvaluateAsync(
                new ApprovalContext(
                    "planning", "markdown-plan", "activate", plan.Id, amount,
                    $"Markdown {plan.Code}: {plan.Lines.Count} lines from {plan.EffectiveFrom:yyyy-MM-dd}.",
                    tenant.StoreId),
                cancellationToken)
            .ConfigureAwait(false);

        if (outcome.MayProceed)
        {
            plan.ApplyApproval(true);

            return null;
        }

        // Replace the provisional request with Stage 05's real one.
        plan.ApplyApproval(false);

        if (outcome.RequestId is not { } requestId)
        {
            throw PlanningRuleException.BadInput("Approval gated the markdown but returned no request.");
        }

        plan.Submit(requestId);

        return requestId;
    }

    private static Money? TotalMarkdownValue(MarkdownPlan plan)
    {
        if (plan.Lines.Count == 0)
        {
            return null;
        }

        string currency = plan.Lines[0].Currency;
        decimal total = plan.Lines
            .Where(line => string.Equals(line.Currency, currency, StringComparison.Ordinal))
            .Sum(line => line.CurrentPrice * line.ProposedDiscountPercent / 100m);

        return new Money(total, currency);
    }
}

/// <summary>Applies Stage 05's verdict to a waiting plan. Approval alone prices nothing.</summary>
/// <param name="PlanId">The plan.</param>
/// <param name="Approved">What Stage 05 decided.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record ApplyMarkdownApprovalCommand(Guid PlanId, bool Approved) : ICommand;

/// <summary>Rejects a malformed approval application.</summary>
public sealed class ApplyMarkdownApprovalCommandValidator : AbstractValidator<ApplyMarkdownApprovalCommand>
{
    /// <summary>Builds the rules.</summary>
    public ApplyMarkdownApprovalCommandValidator()
        => RuleFor(command => command.PlanId).NotEmpty();
}

/// <summary>Applies the verdict.</summary>
public sealed class ApplyMarkdownApprovalCommandHandler(IMarkdownPlanRepository plans)
    : ICommandHandler<ApplyMarkdownApprovalCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(
        ApplyMarkdownApprovalCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        MarkdownPlan plan = await plans
            .FindAsync(command.PlanId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new PlanningNotFoundException("markdown plan", command.PlanId);

        plan.ApplyApproval(command.Approved);

        return Unit.Value;
    }
}

/// <summary>Activates approved steps as Stage 10 promotions.</summary>
public sealed class MarkdownActivator(
    IPlanningPromotionWriter promotions,
    ITenantContext tenant)
{
    /// <summary>Activates every unpromoted line on an approved or active plan.</summary>
    /// <returns>The promotion ids created by this call.</returns>
    public async Task<IReadOnlyList<Guid>> ActivateAsync(MarkdownPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var created = new List<Guid>();
        int index = 0;

        foreach (MarkdownPlanLine line in plan.Lines)
        {
            index++;

            if (line.PromotionId is not null)
            {
                continue;
            }

            Guid promotionId = await promotions
                .CreateSkuPromotionAsync(
                    $"{plan.Code}-{index:00}",
                    $"Markdown {plan.Code}",
                    line.ProposedDiscountPercent,
                    plan.EffectiveFrom,
                    plan.EffectiveTo,
                    tenant.StoreId,
                    line.ItemId,
                    line.ItemVariantId,
                    cancellationToken)
                .ConfigureAwait(false);

            line.MarkPromoted(promotionId);
            created.Add(promotionId);
        }

        if (created.Count > 0)
        {
            plan.RecordActivation(created[0]);
        }

        return created;
    }
}

/// <summary>Activates an approved plan's steps as Stage 10 promotions.</summary>
/// <param name="PlanId">The plan.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record ActivateMarkdownPlanCommand(Guid PlanId) : ICommand<IReadOnlyList<Guid>>;

/// <summary>Rejects a malformed activation.</summary>
public sealed class ActivateMarkdownPlanCommandValidator : AbstractValidator<ActivateMarkdownPlanCommand>
{
    /// <summary>Builds the rules.</summary>
    public ActivateMarkdownPlanCommandValidator()
        => RuleFor(command => command.PlanId).NotEmpty();
}

/// <summary>Activates the plan. Only activation prices anything.</summary>
public sealed class ActivateMarkdownPlanCommandHandler(
    IMarkdownPlanRepository plans,
    MarkdownActivator activator) : ICommandHandler<ActivateMarkdownPlanCommand, IReadOnlyList<Guid>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> HandleAsync(
        ActivateMarkdownPlanCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        MarkdownPlan plan = await plans
            .FindAsync(command.PlanId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new PlanningNotFoundException("markdown plan", command.PlanId);

        return await activator.ActivateAsync(plan, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Amends a live plan by versioning: the old row is kept, a new draft opens with copied lines.</summary>
/// <param name="PlanId">The version being superseded.</param>
/// <param name="NewCode">The new version's code.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record AmendMarkdownPlanCommand(Guid PlanId, string NewCode) : ICommand<Guid>;

/// <summary>Rejects a malformed amendment.</summary>
public sealed class AmendMarkdownPlanCommandValidator : AbstractValidator<AmendMarkdownPlanCommand>
{
    /// <summary>Builds the rules.</summary>
    public AmendMarkdownPlanCommandValidator()
    {
        RuleFor(command => command.PlanId).NotEmpty();
        RuleFor(command => command.NewCode).NotEmpty().MaximumLength(32);
    }
}

/// <summary>Versions the plan.</summary>
public sealed class AmendMarkdownPlanCommandHandler(IMarkdownPlanRepository plans)
    : ICommandHandler<AmendMarkdownPlanCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(
        AmendMarkdownPlanCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        MarkdownPlan current = await plans
            .FindAsync(command.PlanId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new PlanningNotFoundException("markdown plan", command.PlanId);

        if (await plans.FindByCodeAsync(command.NewCode, cancellationToken).ConfigureAwait(false) is not null)
        {
            throw PlanningRuleException.BadInput($"A markdown plan already uses code '{command.NewCode}'.");
        }

        MarkdownPlan next = MarkdownPlan.CreateAmendment(current, command.NewCode);

        foreach (MarkdownPlanLine line in current.Lines)
        {
            next.AddLine(
                line.ItemId,
                line.ItemVariantId,
                line.CurrentPrice,
                line.ProposedDiscountPercent,
                line.Currency,
                line.AbcXyz,
                line.SellThroughPercent,
                line.DaysOfSupply);
        }

        plans.Add(next);

        return next.Id;
    }
}

/// <summary>Cancels a plan, retiring any live promotion through Stage 10 first.</summary>
/// <param name="PlanId">The plan.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record CancelMarkdownPlanCommand(Guid PlanId) : ICommand;

/// <summary>Rejects a malformed cancellation.</summary>
public sealed class CancelMarkdownPlanCommandValidator : AbstractValidator<CancelMarkdownPlanCommand>
{
    /// <summary>Builds the rules.</summary>
    public CancelMarkdownPlanCommandValidator()
        => RuleFor(command => command.PlanId).NotEmpty();
}

/// <summary>Cancels the plan. No orphaned promotions: every live line is deactivated first.</summary>
public sealed class CancelMarkdownPlanCommandHandler(
    IMarkdownPlanRepository plans,
    IPlanningPromotionWriter promotions) : ICommandHandler<CancelMarkdownPlanCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(
        CancelMarkdownPlanCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        MarkdownPlan plan = await plans
            .FindAsync(command.PlanId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new PlanningNotFoundException("markdown plan", command.PlanId);

        foreach (MarkdownPlanLine line in plan.Lines)
        {
            if (line.PromotionId is { } promotionId)
            {
                await promotions
                    .DeactivatePromotionAsync(promotionId, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        plan.Cancel();

        return Unit.Value;
    }
}

/// <summary>Sweeps approved plans whose effective date has arrived — missed dates activate on the next run.</summary>
[CommandSideEffect(SideEffect.Write)]
public sealed record SweepMarkdownActivationsCommand : ICommand<SweepMarkdownActivationsOutcome>;

/// <summary>What a sweep did.</summary>
/// <param name="PlansActivated">How many plans gained promotions.</param>
/// <param name="PromotionsCreated">How many promotions were created.</param>
public sealed record SweepMarkdownActivationsOutcome(int PlansActivated, int PromotionsCreated);

/// <summary>Runs the sweep.</summary>
public sealed class SweepMarkdownActivationsCommandHandler(
    IMarkdownPlanRepository plans,
    MarkdownActivator activator,
    IClock clock) : ICommandHandler<SweepMarkdownActivationsCommand, SweepMarkdownActivationsOutcome>
{
    /// <inheritdoc />
    public async Task<SweepMarkdownActivationsOutcome> HandleAsync(
        SweepMarkdownActivationsCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        DateOnly today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

        IReadOnlyList<MarkdownPlan> due = await plans
            .ListDueForActivationAsync(today, 100, cancellationToken)
            .ConfigureAwait(false);

        int activated = 0;
        int promotions = 0;

        foreach (MarkdownPlan plan in due)
        {
            IReadOnlyList<Guid> created = await activator
                .ActivateAsync(plan, cancellationToken)
                .ConfigureAwait(false);

            if (created.Count > 0)
            {
                activated++;
                promotions += created.Count;
            }
        }

        return new SweepMarkdownActivationsOutcome(activated, promotions);
    }
}
