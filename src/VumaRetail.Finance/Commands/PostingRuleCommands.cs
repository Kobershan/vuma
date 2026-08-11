using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Domain.Finance;

namespace VumaRetail.Finance.Commands;

/// <summary>One posting line to add to a new <see cref="PostingRule"/>.</summary>
/// <param name="AccountId">The GL account this line posts to.</param>
/// <param name="Side">Whether this line debits or credits the account.</param>
/// <param name="AmountKey">Which named amount on the incoming event this line takes its value from.</param>
/// <param name="InheritDimensions">Whether this line copies the event's analysis dimensions.</param>
/// <param name="Description">A line-level narration template.</param>
public sealed record PostingRuleLineInput(
    Guid AccountId,
    NormalBalance Side,
    string AmountKey,
    bool InheritDimensions = true,
    string Description = "");

/// <summary>
/// Configures how a financial event type is turned into a balanced journal (CLAUDE.md §7 rule 12).
/// </summary>
/// <param name="EventType">The financial event type this rule answers.</param>
/// <param name="Lines">The postings the rule produces, in order.</param>
/// <param name="Description">A description for the person configuring it.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record DefinePostingRuleCommand(
    string EventType,
    IReadOnlyList<PostingRuleLineInput> Lines,
    string Description = "") : ICommand<Guid>;

/// <summary>Validates <see cref="DefinePostingRuleCommand"/>.</summary>
public sealed class DefinePostingRuleCommandValidator : AbstractValidator<DefinePostingRuleCommand>
{
    /// <summary>Builds the rules.</summary>
    public DefinePostingRuleCommandValidator()
    {
        RuleFor(command => command.EventType).NotEmpty().MaximumLength(128);
        RuleFor(command => command.Lines).NotEmpty();
        RuleForEach(command => command.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.AmountKey).NotEmpty();
            line.RuleFor(l => l.AccountId).NotEmpty();
        });
    }
}

/// <summary>Handles <see cref="DefinePostingRuleCommand"/>.</summary>
/// <param name="rules">Where posting rules are configured.</param>
/// <param name="accounts">The chart of accounts, to validate every referenced account exists.</param>
/// <param name="tenant">The ambient tenant.</param>
public sealed class DefinePostingRuleCommandHandler(
    IPostingRuleRepository rules, IAccountRepository accounts, ITenantContext tenant)
    : ICommandHandler<DefinePostingRuleCommand, Guid>
{
    /// <inheritdoc />
    /// <exception cref="AccountNotFoundException">A line references an account that does not exist.</exception>
    public async Task<Guid> HandleAsync(DefinePostingRuleCommand command, CancellationToken cancellationToken = default)
    {
        PostingRule rule = PostingRule.Define(tenant.TenantId, command.EventType, command.Description);

        foreach (PostingRuleLineInput line in command.Lines)
        {
            if (await accounts.FindByIdAsync(line.AccountId, cancellationToken).ConfigureAwait(false) is null)
            {
                throw new AccountNotFoundException(line.AccountId);
            }

            rule.AddLine(line.AccountId, line.Side, line.AmountKey, line.InheritDimensions, line.Description);
        }

        rules.Add(rule);

        return rule.Id;
    }
}
