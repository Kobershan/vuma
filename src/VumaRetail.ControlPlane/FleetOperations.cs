namespace VumaRetail.ControlPlane;

public sealed record FleetNode(string NodeId, string TenantId, string Version,
    string Channel, DateTimeOffset LastContactAt, bool BackupVerified);

public sealed record RolloutPlan(Guid Id, string Version, string Channel, int Percentage,
    bool Halted, DateTimeOffset CreatedAt);

public sealed record FleetHealth(string NodeId, string TenantId, TimeSpan OfflineFor,
    bool BackupVerified, bool Offline);

public sealed record RemoteCommand(Guid Id, string NodeId, string Command,
    DateTimeOffset CreatedAt, bool Halted);

/// <summary>Deterministic vendor fleet policy; it never accesses tenant business data.</summary>
public sealed class FleetOperations(IClock clock)
{
    private readonly IClock clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly Dictionary<string, FleetNode> nodes = new(StringComparer.Ordinal);
    private readonly List<RolloutPlan> rollouts = [];
    private readonly List<RemoteCommand> commands = [];

    public IReadOnlyCollection<RolloutPlan> Rollouts => rollouts;
    public IReadOnlyCollection<RemoteCommand> Commands => commands;

    public void Observe(FleetNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentException.ThrowIfNullOrWhiteSpace(node.NodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(node.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(node.Version);
        ArgumentException.ThrowIfNullOrWhiteSpace(node.Channel);
        nodes[node.NodeId] = node;
    }

    public RolloutPlan StartRollout(string version, string channel, int percentage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        if (percentage is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(percentage));
        }

        RolloutPlan plan = new(Guid.NewGuid(), version.Trim(), channel.Trim(), percentage, false, clock.UtcNow);
        rollouts.Add(plan);
        return plan;
    }

    public RolloutPlan HaltRollout(Guid rolloutId)
    {
        int index = rollouts.FindIndex(x => x.Id == rolloutId);
        if (index < 0)
        {
            throw new KeyNotFoundException("Rollout was not found.");
        }

        RolloutPlan halted = rollouts[index] with { Halted = true };
        rollouts[index] = halted;
        return halted;
    }

    public bool IsSelected(RolloutPlan plan, string nodeId)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        if (plan.Halted || plan.Percentage == 0)
        {
            return false;
        }

        if (plan.Percentage == 100)
        {
            return true;
        }

        int bucket = (int)(unchecked((uint)StringComparer.Ordinal.GetHashCode($"{plan.Id:N}:{nodeId}")) % 100);
        return bucket < plan.Percentage;
    }

    public IReadOnlyList<FleetHealth> Health(TimeSpan offlineAfter)
    {
        if (offlineAfter <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(offlineAfter));
        }

        return nodes.Values.OrderBy(x => x.NodeId, StringComparer.Ordinal)
            .Select(x => new FleetHealth(x.NodeId, x.TenantId, clock.UtcNow - x.LastContactAt,
                x.BackupVerified, clock.UtcNow - x.LastContactAt > offlineAfter)).ToArray();
    }

    public RemoteCommand QueueCommand(string nodeId, string command)
    {
        if (!nodes.ContainsKey(nodeId))
        {
            throw new KeyNotFoundException("Fleet node was not found.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        RemoteCommand result = new(Guid.NewGuid(), nodeId, command.Trim(), clock.UtcNow, false);
        commands.Add(result);
        return result;
    }

    public RemoteCommand HaltCommand(Guid commandId)
    {
        int index = commands.FindIndex(x => x.Id == commandId);
        if (index < 0)
        {
            throw new KeyNotFoundException("Remote command was not found.");
        }

        RemoteCommand halted = commands[index] with { Halted = true };
        commands[index] = halted;
        return halted;
    }

    /// <summary>
    /// Selects the newest applicable rollout for a node: same channel, not halted, selected
    /// by the deterministic bucket, and a different version from what the node reports.
    /// Returns null when the node is up to date or no rollout applies.
    /// </summary>
    public RolloutPlan? SelectUpdate(string nodeId, string currentVersion, string channel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        string current = (currentVersion ?? string.Empty).Trim();
        return rollouts
            .Where(p => !p.Halted && string.Equals(p.Channel, channel.Trim(), StringComparison.OrdinalIgnoreCase)
                && !string.Equals(p.Version, current, StringComparison.OrdinalIgnoreCase)
                && IsSelected(p, nodeId))
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefault();
    }

}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
