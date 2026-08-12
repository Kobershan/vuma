using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Domain.Identity;

namespace VumaRetail.Sync.Permissions;

/// <summary>
/// What the <c>sync</c> module lets somebody do.
/// </summary>
/// <remarks>
/// <c>sync.batch.push</c> is the one that matters. It is what a peer node's credential holds and
/// nothing else should: a role that grants it lets its holder write any replicated row on this node,
/// under any conflict policy, without going through a single business rule. It belongs to the
/// terminal and store service accounts and to nobody who signs in with a password.
/// </remarks>
public sealed class SyncPermissions : IModulePermissions
{
    /// <summary>Push a batch of replicated operations to this node.</summary>
    public const string BatchPush = "sync.batch.push";

    /// <summary>See replication health: cursors, outstanding operations, last contact.</summary>
    public const string StatusView = "sync.status.view";

    /// <summary>See the conflict review queue and both versions of each divergence.</summary>
    public const string ConflictView = "sync.conflict.view";

    /// <summary>Settle a conflict by choosing the local or the remote version.</summary>
    public const string ConflictResolve = "sync.conflict.resolve";

    /// <inheritdoc />
    public string Module => "sync";

    /// <inheritdoc />
    public IReadOnlyCollection<PermissionDescriptor> Permissions =>
    [
        new(
            PermissionKey.Parse(BatchPush),
            "Push replicated changes into this node. Held by peer nodes, not by people.",
            IsHighRisk: true),
        new(PermissionKey.Parse(StatusView), "See replication health."),
        new(PermissionKey.Parse(ConflictView), "See the sync conflict queue."),
        new(
            PermissionKey.Parse(ConflictResolve),
            "Choose which version of a diverged record stands.",
            IsHighRisk: true),
    ];
}

/// <summary>
/// What the <c>backup</c> module lets somebody do.
/// </summary>
/// <remarks>
/// <c>backup.snapshot.restore</c> is deliberately its own permission and not folded into
/// <c>manage</c>. A restore replaces a database; it is the single most destructive thing anybody can
/// do to a trading store, and it should be grantable to the person who runs the DR drill without also
/// being grantable to everyone who can press "back up now".
/// </remarks>
public sealed class BackupPermissions : IModulePermissions
{
    /// <summary>See the snapshot ledger and when each was last verified and restored.</summary>
    public const string SnapshotView = "backup.snapshot.view";

    /// <summary>Take a snapshot now.</summary>
    public const string SnapshotCreate = "backup.snapshot.create";

    /// <summary>Read a snapshot back from the vault and confirm its checksum.</summary>
    public const string SnapshotVerify = "backup.snapshot.verify";

    /// <summary>Restore a snapshot into a database. R4's actual capability.</summary>
    public const string SnapshotRestore = "backup.snapshot.restore";

    /// <inheritdoc />
    public string Module => "backup";

    /// <inheritdoc />
    public IReadOnlyCollection<PermissionDescriptor> Permissions =>
    [
        new(PermissionKey.Parse(SnapshotView), "See the backup history."),
        new(PermissionKey.Parse(SnapshotCreate), "Take a backup now."),
        new(PermissionKey.Parse(SnapshotVerify), "Check that a backup can still be read back."),
        new(
            PermissionKey.Parse(SnapshotRestore),
            "Restore a backup over a database. Destructive — grant to whoever runs the drill.",
            IsHighRisk: true),
    ];
}
