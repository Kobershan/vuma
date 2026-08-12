using System.Collections.Frozen;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using VumaRetail.Application.Abstractions.Sync;
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Sync;

namespace VumaRetail.Infrastructure.Sync;

/// <summary>
/// The <c>[Replicated]</c> declarations, read once from the EF model and frozen.
/// </summary>
/// <remarks>
/// <para>
/// Built from the model rather than from an assembly scan, so the registry contains exactly the
/// entities this node persists. A type that carries the attribute but is not mapped is not
/// replicated, because there is nothing to replicate.
/// </para>
/// <para>
/// <see cref="ResolveEntityType"/> answers only from this closed set, and never from
/// <c>Type.GetType</c>. A receiver that materialised whatever type name a peer sent would be a
/// deserialisation gadget with an HTTP endpoint in front of it, which is a well-known way to turn a
/// replication protocol into remote code execution.
/// </para>
/// </remarks>
public sealed class ReplicationRegistry : IReplicationRegistry
{
    private readonly FrozenDictionary<Type, ReplicationDescriptor> _byType;
    private readonly FrozenDictionary<string, Type> _byName;

    /// <summary>Builds the registry from a database context's model.</summary>
    /// <param name="context">The context whose model declares the entities.</param>
    public ReplicationRegistry(DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Dictionary<Type, ReplicationDescriptor> byType = [];
        Dictionary<string, Type> byName = [];

        foreach (IEntityType entityType in context.Model.GetEntityTypes())
        {
            Type clrType = entityType.ClrType;

            if (!typeof(Entity).IsAssignableFrom(clrType) || byType.ContainsKey(clrType))
            {
                continue;
            }

            if (clrType.GetCustomAttribute<ReplicatedAttribute>() is not { } declaration)
            {
                // An architecture test already fails the build on this, so reaching it means the
                // model gained an entity outside the assemblies that test scans. Skipping is the
                // safe answer: an entity with no declared policy must not be replicated by guess.
                continue;
            }

            byType[clrType] = new ReplicationDescriptor(declaration.Scope, declaration.ConflictPolicy);
            byName[clrType.Name] = clrType;
        }

        _byType = byType.ToFrozenDictionary();
        _byName = byName.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>Every replicated entity this node knows, by CLR type name.</summary>
    public IReadOnlyCollection<string> KnownEntityTypes => _byName.Keys;

    /// <inheritdoc />
    public ReplicationDescriptor? Find(Type entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);

        return _byType.TryGetValue(entityType, out ReplicationDescriptor descriptor) ? descriptor : null;
    }

    /// <inheritdoc />
    public ReplicationDescriptor? FindByName(string entityTypeName)
    {
        ArgumentNullException.ThrowIfNull(entityTypeName);

        return _byName.TryGetValue(entityTypeName, out Type? clrType) ? Find(clrType) : null;
    }

    /// <inheritdoc />
    public Type? ResolveEntityType(string entityTypeName)
    {
        ArgumentNullException.ThrowIfNull(entityTypeName);

        return _byName.TryGetValue(entityTypeName, out Type? clrType) ? clrType : null;
    }
}

/// <summary>This node's identity in the terminal → store → cloud chain.</summary>
/// <remarks>
/// Configuration, not derivation. A store server's node id has to survive a restore onto new
/// hardware, so it cannot be a machine fingerprint; it has to survive a database restore, so it
/// cannot be a row; and it has to be different on every node, so it cannot have a default. It is
/// therefore a setting the installer writes once, and the host refuses to start without it.
/// </remarks>
public sealed class NodeIdentityOptions
{
    /// <summary>The configuration section this binds to.</summary>
    public const string SectionName = "Vuma:Sync:Node";

    /// <summary>The node id — <c>store:{id}</c>, <c>cloud</c>, <c>terminal:{id}</c>.</summary>
    public string NodeId { get; set; } = string.Empty;

    /// <summary>What tier this node is.</summary>
    public NodeKind Kind { get; set; } = NodeKind.Store;
}

/// <summary>This node's identity, from configuration.</summary>
/// <param name="options">The configured node id and tier.</param>
public sealed class NodeIdentity(NodeIdentityOptions options) : INodeIdentity
{
    /// <inheritdoc />
    public string NodeId { get; } = Validate(options);

    /// <inheritdoc />
    public NodeKind Kind { get; } = options.Kind;

    private static string Validate(NodeIdentityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.NodeId))
        {
            throw new InvalidOperationException(
                $"{NodeIdentityOptions.SectionName}:NodeId is not configured. Every node needs a "
                + "stable id — it is the final tie-break in every hybrid logical clock stamp "
                + "(ADR-007), so two nodes sharing one produce stamps that cannot be ordered.");
        }

        return options.NodeId.Length > Domain.Primitives.HlcStamp.NodeIdMaxLength
            ? throw new InvalidOperationException(
                $"{NodeIdentityOptions.SectionName}:NodeId is longer than "
                + $"{Domain.Primitives.HlcStamp.NodeIdMaxLength} characters.")
            : options.NodeId;
    }
}

/// <summary>
/// Records which rows the current apply region wrote on a peer's instruction.
/// </summary>
/// <remarks>
/// Scoped, so the record lives exactly as long as the request or background pass that made it. A
/// singleton would be shared by every request on the server, and one sync batch would silently stop
/// the tills capturing their own sales for as long as it ran.
/// </remarks>
public sealed class ReplicationScope : IReplicationScope
{
    private readonly HashSet<Guid> _applied = [];

    /// <inheritdoc />
    public IReadOnlySet<Guid> AppliedRowIds => _applied;

    /// <inheritdoc />
    public IDisposable BeginApply()
    {
        // Cleared on entry rather than on exit. The outbox behaviour reads this *after* the handler
        // has returned and the region has closed (pipeline slot 300 wraps the handler), so clearing
        // on exit would erase the answer a moment before it is asked for.
        _applied.Clear();

        return new Region();
    }

    /// <inheritdoc />
    public void RecordApplied(Guid rowId) => _applied.Add(rowId);

    private sealed class Region : IDisposable
    {
        public void Dispose()
        {
            // Nothing to undo. The region exists to bracket the apply for a reader, and what it
            // recorded has to survive it — see BeginApply.
        }
    }
}
