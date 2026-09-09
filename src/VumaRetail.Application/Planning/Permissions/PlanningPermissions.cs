using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Domain.Identity;

namespace VumaRetail.Application.Planning.Permissions;

/// <summary>What the planning module lets somebody do (Stage 15).</summary>
/// <remarks>
/// View and act are separate grants throughout: seeing a forecast, a classification or a
/// suggestion is harmless, while accepting a suggestion commits procurement or stock and
/// approving a markdown commits pricing. Propose/approve stay split so the planner who proposes
/// a markdown is never automatically its approver.
/// </remarks>
public sealed class PlanningPermissions : IModulePermissions
{
    /// <summary>Read demand forecast snapshots.</summary>
    public const string ForecastView = "planning.forecast.view";

    /// <summary>Read aggregated demand history.</summary>
    public const string HistoryView = "planning.history.view";

    /// <summary>Create and change replenishment parameters and open-to-buy budgets.</summary>
    public const string ParametersManage = "planning.parameters.manage";

    /// <summary>Read ABC/XYZ classification snapshots.</summary>
    public const string ClassificationView = "planning.classification.view";

    /// <summary>Read replenishment suggestions.</summary>
    public const string SuggestionView = "planning.suggestion.view";

    /// <summary>Accept, amend-accept or reject a suggestion. Commits procurement or stock.</summary>
    public const string SuggestionAccept = "planning.suggestion.accept";

    /// <summary>Set open-to-buy budgets.</summary>
    public const string OtbManage = "planning.otb.manage";

    /// <summary>Propose a markdown plan.</summary>
    public const string MarkdownPropose = "planning.markdown.propose";

    /// <summary>Approve a markdown plan. Commits pricing through Stage 10.</summary>
    public const string MarkdownApprove = "planning.markdown.approve";

    /// <inheritdoc />
    public string Module => "planning";

    /// <inheritdoc />
    public IReadOnlyCollection<PermissionDescriptor> Permissions =>
    [
        new(PermissionKey.Parse(ForecastView), "View demand forecast snapshots."),
        new(PermissionKey.Parse(HistoryView), "View aggregated demand history."),
        new(PermissionKey.Parse(ParametersManage), "Manage replenishment parameters and budgets."),
        new(PermissionKey.Parse(ClassificationView), "View ABC/XYZ classifications."),
        new(PermissionKey.Parse(SuggestionView), "View replenishment suggestions."),
        new(PermissionKey.Parse(SuggestionAccept), "Accept or reject replenishment suggestions.", IsHighRisk: true),
        new(PermissionKey.Parse(OtbManage), "Set open-to-buy budgets."),
        new(PermissionKey.Parse(MarkdownPropose), "Propose markdown plans."),
        new(PermissionKey.Parse(MarkdownApprove), "Approve markdown plans.", IsHighRisk: true),
    ];
}
