using VumaRetail.Application.Abstractions.Licensing;

namespace VumaRetail.Application.Conversations;

/// <summary>Licence declaration for the conversational commerce boundary (Stage 22b).</summary>
public sealed class ConversationModuleManifest : IModuleManifest
{
    /// <inheritdoc />
    public string Module => "conversations";

    /// <inheritdoc />
    public string LicenceFlag => "conversations";

    /// <inheritdoc />
    public string Description => "Conversational commerce with verified customer bindings and escalation.";

    /// <inheritdoc />
    public bool IsCore => false;
}
