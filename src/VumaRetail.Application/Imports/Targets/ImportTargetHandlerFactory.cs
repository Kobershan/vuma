using VumaRetail.Application.Abstractions.Imports;
using VumaRetail.Domain.Imports;

namespace VumaRetail.Application.Imports.Targets;

/// <summary>
/// Finds the handler for a target, and publishes every handler's field catalogue.
/// </summary>
/// <remarks>
/// Built from the registered handlers rather than from a <c>switch</c>, so
/// <c>GET /api/v1/imports/targets</c> serves whatever the host actually wired — which is what lets a
/// screen build its whole mapping UI without a line of hard-coded field names, and what makes adding
/// a sixth target a registration rather than an edit in four places.
/// </remarks>
/// <param name="handlers">Every registered target handler.</param>
public sealed class ImportTargetHandlerFactory(IEnumerable<IImportTargetHandler> handlers)
    : IImportTargetHandlerFactory
{
    private readonly IReadOnlyDictionary<ImportTargetKind, IImportTargetHandler> _handlers =
        handlers.GroupBy(handler => handler.Kind).ToDictionary(group => group.Key, group => group.Last());

    /// <inheritdoc />
    public IReadOnlyList<ImportTargetDescriptor> Descriptors
        => [.. _handlers.Values.OrderBy(handler => handler.Kind).Select(handler => handler.Descriptor)];

    /// <inheritdoc />
    public IImportTargetHandler For(ImportTargetKind kind)
        => _handlers.TryGetValue(kind, out IImportTargetHandler? handler)
            ? handler
            : throw new NotSupportedException($"No import target handler is registered for {kind}.");
}
