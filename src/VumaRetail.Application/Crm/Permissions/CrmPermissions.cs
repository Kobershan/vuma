using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Domain.Identity;

namespace VumaRetail.Application.Crm.Permissions;

/// <summary>What the CRM module lets somebody do (Stage 19).</summary>
/// <remarks>
/// Viewing a relationship is harmless; changing it is not. Consent management is its own grant:
/// whoever may market must not automatically be whoever records the permission to do so.
/// </remarks>
public sealed class CrmPermissions : IModulePermissions
{
    /// <summary>Read leads.</summary>
    public const string LeadView = "crm.lead.view";

    /// <summary>Capture and change leads.</summary>
    public const string LeadManage = "crm.lead.manage";

    /// <summary>Read opportunities.</summary>
    public const string OpportunityView = "crm.opportunity.view";

    /// <summary>Create and move opportunities.</summary>
    public const string OpportunityManage = "crm.opportunity.manage";

    /// <summary>Read activities.</summary>
    public const string ActivityView = "crm.activity.view";

    /// <summary>Log activities.</summary>
    public const string ActivityLog = "crm.activity.log";

    /// <summary>Read segments and membership.</summary>
    public const string SegmentView = "crm.segment.view";

    /// <summary>Create segments and manage membership.</summary>
    public const string SegmentManage = "crm.segment.manage";

    /// <summary>Read consent state.</summary>
    public const string ConsentView = "crm.consent.view";

    /// <summary>Capture and withdraw consent.</summary>
    public const string ConsentManage = "crm.consent.manage";

    /// <summary>Read the 360° customer view.</summary>
    public const string View360 = "crm.view360.view";

    /// <inheritdoc />
    public string Module => "crm";

    /// <inheritdoc />
    public IReadOnlyCollection<PermissionDescriptor> Permissions =>
    [
        new(PermissionKey.Parse(LeadView), "View leads."),
        new(PermissionKey.Parse(LeadManage), "Capture and change leads.", IsHighRisk: true),
        new(PermissionKey.Parse(OpportunityView), "View opportunities."),
        new(PermissionKey.Parse(OpportunityManage), "Create and move opportunities.", IsHighRisk: true),
        new(PermissionKey.Parse(ActivityView), "View activities."),
        new(PermissionKey.Parse(ActivityLog), "Log activities."),
        new(PermissionKey.Parse(SegmentView), "View segments and membership."),
        new(PermissionKey.Parse(SegmentManage), "Manage segments and membership."),
        new(PermissionKey.Parse(ConsentView), "View consent state."),
        new(PermissionKey.Parse(ConsentManage), "Capture and withdraw consent.", IsHighRisk: true),
        new(PermissionKey.Parse(View360), "View the 360° customer view."),
    ];
}
