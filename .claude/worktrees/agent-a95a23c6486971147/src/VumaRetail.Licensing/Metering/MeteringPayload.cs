using System.Text.Json;
using System.Text.Json.Serialization;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Abstractions.Sync;

namespace VumaRetail.Licensing.Metering;

/// <summary>
/// Everything a day's metering rollup may contain (R10, ADR-024, <c>LICENSING.md</c> §9).
/// </summary>
/// <remarks>
/// <para>
/// <b>This type is the whitelist.</b> CLAUDE.md §7 rule 16 says telemetry is whitelisted aggregate
/// counters only and that building a payload from a business table is a bug — the way that is made
/// true is that there is nowhere in this record to put one. Every leaf is a number, except three
/// strings that are a node id, a date and a version, and the keys of a module-usage map that come from
/// the registered module manifests.
/// </para>
/// <para>
/// The vendor's customers have to be able to answer their own regulators about what leaves their
/// premises. That answer is this record, and it is short enough to publish.
/// </para>
/// </remarks>
/// <param name="NodeId">The node the counts were taken on. Not a person and not a place.</param>
/// <param name="Period">The day, <c>yyyy-MM-dd</c>.</param>
/// <param name="Version">The software version, so the vendor can target an upgrade campaign.</param>
/// <param name="Counts">Quantities.</param>
/// <param name="ModuleUsage">Writes per module, keyed by module name.</param>
/// <param name="Health">Operational health.</param>
public sealed record MeteringPayload(
    string NodeId,
    string Period,
    string Version,
    MeteringCounts Counts,
    IReadOnlyDictionary<string, long> ModuleUsage,
    MeteringHealth Health)
{
    /// <summary>The serialiser both this side and the control plane use.</summary>
    public static JsonSerializerOptions Json { get; } = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false,
    };

    /// <summary>Serialises the payload for storage and delivery.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>Reads a stored payload back.</summary>
    /// <param name="json">The stored document.</param>
    public static MeteringPayload? FromJson(string json)
        => string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<MeteringPayload>(json, Json);
}

/// <summary>Quantities for a day. Every field is a count.</summary>
/// <param name="Stores">Live trading locations.</param>
/// <param name="Terminals">Enrolled terminals.</param>
/// <param name="TerminalsOnline">Terminals seen recently.</param>
/// <param name="RegisteredUsers">People who can sign in.</param>
/// <param name="ActiveUsers">People who changed something that day.</param>
/// <param name="Writes">Rows written that day, from the audit trail.</param>
/// <param name="StorageBytes">The database's size on disk.</param>
public readonly record struct MeteringCounts(
    int Stores,
    int Terminals,
    int TerminalsOnline,
    int RegisteredUsers,
    int ActiveUsers,
    long Writes,
    long StorageBytes);

/// <summary>Operational health for a day. Every field is a count.</summary>
/// <param name="OutboxDepth">Replication operations outstanding.</param>
/// <param name="SyncFailures">Replication attempts that failed.</param>
/// <param name="ConflictsOpen">Divergences waiting for a person.</param>
/// <param name="SnapshotsTaken">Backups taken.</param>
/// <param name="SnapshotsVerified">Backups read back and checked.</param>
public readonly record struct MeteringHealth(
    int OutboxDepth,
    int SyncFailures,
    int ConflictsOpen,
    int SnapshotsTaken,
    int SnapshotsVerified);

/// <summary>Builds a day's rollup from the whitelisted counters and nothing else.</summary>
/// <param name="counters">The privacy boundary — an interface with nowhere to put a business row.</param>
/// <param name="node">This node's identity.</param>
/// <param name="install">This installation's version.</param>
/// <param name="manifests">The declared modules, which are the only permitted usage keys.</param>
public sealed class MeteringCollector(
    IUsageCounterSource counters,
    INodeIdentity node,
    IInstallIdentity install,
    IEnumerable<IModuleManifest> manifests)
{
    /// <summary>Computes the rollup for a day.</summary>
    /// <param name="period">The day.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    public async Task<MeteringPayload> CollectAsync(
        DateOnly period,
        CancellationToken cancellationToken = default)
    {
        ConfigurationCounts configuration = await counters
            .CountConfigurationAsync(cancellationToken)
            .ConfigureAwait(false);

        ActivityCounts activity = await counters
            .CountActivityAsync(period, cancellationToken)
            .ConfigureAwait(false);

        // Module usage is filtered to declared modules. A key that is not a module name could only
        // have come from somewhere it should not have, and dropping it here means the whitelist holds
        // even if a counter source is later written carelessly.
        HashSet<string> declared = manifests
            .Select(manifest => manifest.Module)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Dictionary<string, long> usage = activity.ModuleUsage
            .Where(entry => declared.Contains(entry.Key))
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);

        return new MeteringPayload(
            node.NodeId,
            period.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            install.Version,
            new MeteringCounts(
                configuration.Stores,
                configuration.Terminals,
                configuration.TerminalsOnline,
                configuration.NamedUsers,
                activity.ActiveUsers,
                activity.Writes,
                activity.StorageBytes),
            usage,
            new MeteringHealth(
                activity.OutboxDepth,
                activity.SyncFailures,
                activity.ConflictsOpen,
                activity.SnapshotsTaken,
                activity.SnapshotsVerified));
    }
}
