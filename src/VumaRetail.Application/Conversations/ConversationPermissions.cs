using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Domain.Identity;

namespace VumaRetail.Application.Conversations;

/// <summary>Permission surface for conversational commerce.</summary>
public sealed class ConversationPermissions : IModulePermissions
{
    /// <summary>Receive and process inbound messages.</summary>
    public const string Receive = "conversations.message.receive";

    /// <summary>Escalate a conversation to a human operator.</summary>
    public const string Escalate = "conversations.conversation.escalate";

    /// <summary>Manage channel bindings and their verification lifecycle.</summary>
    public const string BindingManage = "conversations.binding.manage";

    /// <inheritdoc />
    public string Module => "conversations";

    /// <inheritdoc />
    public IReadOnlyCollection<PermissionDescriptor> Permissions =>
    [
        new(PermissionKey.Parse(Receive), "Receive and process inbound conversation messages."),
        new(PermissionKey.Parse(Escalate), "Escalate conversations to a human operator."),
        new(PermissionKey.Parse(BindingManage), "Manage and verify conversation channel bindings.", IsHighRisk: true),
    ];
}
