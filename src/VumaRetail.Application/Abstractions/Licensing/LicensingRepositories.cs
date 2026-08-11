using VumaRetail.Domain.Licensing;

namespace VumaRetail.Application.Abstractions.Licensing;

/// <summary>This installation's binding (<c>LICENSING.md</c> §3).</summary>
public interface IActivationRepository
{
    /// <summary>The activation for this node, or <c>null</c> if the install has never been activated.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<Activation?> FindCurrentAsync(CancellationToken cancellationToken = default);

    /// <summary>Records a new binding.</summary>
    /// <param name="activation">The activation.</param>
    void Add(Activation activation);
}

/// <summary>The signed licences this node has been issued.</summary>
public interface ILicenceRepository
{
    /// <summary>The most recently issued licence, by issuance counter.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<Licence?> FindCurrentAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The highest issuance counter this node has ever stored.
    /// </summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <remarks>
    /// Read before storing anything, and it is what makes a replayed licence useless. A restored old
    /// backup carries an old document; storing it would roll the entitlements back to whatever the
    /// tenant had a month ago, and an attacker who kept a copy could replay it for ever.
    /// </remarks>
    Task<long> HighestIssuanceCounterAsync(CancellationToken cancellationToken = default);

    /// <summary>Records a licence whose signature has been verified.</summary>
    /// <param name="licence">The licence.</param>
    void Add(Licence licence);
}

/// <summary>The leases this node has held.</summary>
public interface ILeaseRepository
{
    /// <summary>The most recently issued lease, or <c>null</c> if there has never been one.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<Lease?> FindCurrentAsync(CancellationToken cancellationToken = default);

    /// <summary>Records a lease whose signature has been verified.</summary>
    /// <param name="lease">The lease.</param>
    void Add(Lease lease);
}

/// <summary>Emergency codes redeemed here, and the clock watermark and tamper flags beside them.</summary>
public interface ILicenceStateRepository
{
    /// <summary>The emergency unlock still in force, if any.</summary>
    /// <param name="at">The instant to test, UTC.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<EmergencyUnlock?> FindActiveUnlockAsync(
        DateTimeOffset at,
        CancellationToken cancellationToken = default);

    /// <summary>True when a code has already been redeemed on this installation.</summary>
    /// <param name="codeReference">The vendor's id for the code.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<bool> HasRedeemedAsync(Guid codeReference, CancellationToken cancellationToken = default);

    /// <summary>Records a redemption.</summary>
    /// <param name="unlock">The redemption.</param>
    void Add(EmergencyUnlock unlock);

    /// <summary>Unlock redemptions that have not yet been reported to the vendor.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<EmergencyUnlock>> ListUnreportedUnlocksAsync(
        CancellationToken cancellationToken = default);

    /// <summary>This node's clock watermark, or <c>null</c> if it has never been written.</summary>
    /// <param name="nodeId">The node.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<ClockWatermark?> FindWatermarkAsync(string nodeId, CancellationToken cancellationToken = default);

    /// <summary>Starts this node's clock watermark.</summary>
    /// <param name="watermark">The watermark.</param>
    void Add(ClockWatermark watermark);

    /// <summary>Raises a tamper flag for the vendor's abuse queue. Restricts nobody.</summary>
    /// <param name="flag">The flag.</param>
    void Add(TamperFlag flag);

    /// <summary>Flags that have not yet been reported.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<TamperFlag>> ListUnreportedFlagsAsync(CancellationToken cancellationToken = default);
}

/// <summary>The daily metering rollups (<c>LICENSING.md</c> §9).</summary>
public interface IMeteringRepository
{
    /// <summary>The rollup for a node and a day, or <c>null</c> if it has not been computed.</summary>
    /// <param name="nodeId">The node.</param>
    /// <param name="period">The day.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<MeteringRecord?> FindAsync(
        string nodeId,
        DateOnly period,
        CancellationToken cancellationToken = default);

    /// <summary>Queues a rollup.</summary>
    /// <param name="record">The rollup.</param>
    void Add(MeteringRecord record);

    /// <summary>Rollups still waiting to reach the control plane, oldest first.</summary>
    /// <param name="limit">How many to take.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<MeteringRecord>> ListPendingAsync(
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>The most recent rollups, for the licence screen.</summary>
    /// <param name="limit">How many to take.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<MeteringRecord>> ListRecentAsync(
        int limit,
        CancellationToken cancellationToken = default);
}

/// <summary>Vendor support-access grants (<c>LICENSING.md</c> §9).</summary>
public interface ISupportGrantRepository
{
    /// <summary>A grant by id, within the current tenant.</summary>
    /// <param name="grantId">The grant.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<SupportGrant?> FindAsync(Guid grantId, CancellationToken cancellationToken = default);

    /// <summary>A grant by the control plane's own reference, so a pushed request is idempotent.</summary>
    /// <param name="reference">The control plane's id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<SupportGrant?> FindByReferenceAsync(Guid reference, CancellationToken cancellationToken = default);

    /// <summary>Every grant, newest first — the tenant's own record of who looked at what.</summary>
    /// <param name="limit">How many to take.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<SupportGrant>> ListAsync(int limit, CancellationToken cancellationToken = default);

    /// <summary>Records a request pushed down from the control plane.</summary>
    /// <param name="grant">The request.</param>
    void Add(SupportGrant grant);
}

/// <summary>
/// Reads this machine's hardware characteristics (<c>LICENSING.md</c> §3).
/// </summary>
/// <remarks>
/// The raw values cross exactly one boundary — from here into
/// <see cref="HardwareFingerprint.Capture"/> — and are never persisted, logged or transmitted. A
/// component this machine cannot report is simply absent from the dictionary, which scores nothing and
/// is the right answer on a virtual machine.
/// </remarks>
public interface IHardwareFingerprintProvider
{
    /// <summary>Reads the machine now.</summary>
    IReadOnlyDictionary<FingerprintComponent, string> Read();
}

/// <summary>
/// This installation's identity: the install id that survives restarts, and the boot id that does not.
/// </summary>
/// <remarks>
/// The pair is what lets the control plane tell a restarted store from a cloned one: one install id
/// heartbeating with two boot ids at once, from two fingerprints, is a clone
/// (<c>LICENSING.md</c> §7).
/// </remarks>
public interface IInstallIdentity
{
    /// <summary>Stable for the life of the installation.</summary>
    Guid InstallId { get; }

    /// <summary>Fresh on every process start.</summary>
    Guid BootId { get; }

    /// <summary>When this process started, UTC. Reported as uptime, which is a health signal.</summary>
    DateTimeOffset BootedAt { get; }

    /// <summary>The running software version, as reported to the vendor.</summary>
    string Version { get; }
}

/// <summary>
/// The out-of-database copy of the licence state, and its shadow (<c>LICENSING.md</c> §7).
/// </summary>
/// <remarks>
/// Two copies, written together and compared on every start. Disagreement is a tamper flag — not a
/// restriction — because the honest explanations (a half-finished restore, a disk that filled) are far
/// more common than the dishonest one, and ADR-026 puts the leverage in vendor-side detection rather
/// than in client-side punishment.
/// </remarks>
public interface ILicenceShadowStore
{
    /// <summary>Writes both copies.</summary>
    /// <param name="document">The signed lease document.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task WriteAsync(string document, CancellationToken cancellationToken = default);

    /// <summary>Reads both copies and reports whether they agree.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<ShadowReadResult> ReadAsync(CancellationToken cancellationToken = default);
}

/// <summary>What the shadow store found.</summary>
/// <param name="Document">The primary copy, or <c>null</c> if there is none.</param>
/// <param name="Agrees">Whether the shadow copy matches. False is a tamper flag, never a refusal.</param>
public readonly record struct ShadowReadResult(string? Document, bool Agrees);

/// <summary>Checks that the licensing assembly is the one the installer recorded.</summary>
public interface IIntegrityChecker
{
    /// <summary>What the check found. <see cref="IntegrityStatus.Unknown"/> until Stage 31 records hashes.</summary>
    IntegrityStatus Check();
}

/// <summary>
/// The aggregate counters the daily metering rollup is built from (R10, ADR-024).
/// </summary>
/// <remarks>
/// <para>
/// <b>This interface is the privacy boundary.</b> Everything it returns is a count. There is no method
/// here that can return a name, a document, a line of a sale or a row of a business table — not
/// because the implementation is careful, but because the shape of the interface gives it nowhere to
/// put one. Building a metering payload from a business table is a bug, and a test catches it
/// (CLAUDE.md §7 rule 16).
/// </para>
/// <para>
/// Module usage is a count of operations per module, which is what tells the vendor a customer is
/// paying for something they never open — a commercially useful signal that discloses nothing about
/// what they did with it.
/// </para>
/// </remarks>
public interface IUsageCounterSource
{
    /// <summary>Counts the tenant's configuration quantities: stores, terminals, users.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<ConfigurationCounts> CountConfigurationAsync(CancellationToken cancellationToken = default);

    /// <summary>Counts a day's activity: transactions by kind, module usage, storage, errors.</summary>
    /// <param name="period">The day.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<ActivityCounts> CountActivityAsync(DateOnly period, CancellationToken cancellationToken = default);

    /// <summary>
    /// The health numbers a heartbeat carries: replication lag, queue depth, backup freshness.
    /// </summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <remarks>
    /// On this interface rather than gathered from four repositories in the heartbeat handler, so that
    /// "everything that leaves a tenant's premises for vendor purposes" has a single type to read and
    /// a single test to point at.
    /// </remarks>
    Task<HealthCounts> CountHealthAsync(CancellationToken cancellationToken = default);
}

/// <summary>The health numbers a heartbeat carries. Counts and timestamps, nothing else.</summary>
/// <param name="SyncLagSeconds">How far behind replication is.</param>
/// <param name="OutboxDepth">Replication operations outstanding.</param>
/// <param name="LastBackupAt">When a snapshot was last taken.</param>
/// <param name="LastBackupVerifiedAt">When one was last read back and checked.</param>
/// <param name="ErrorsLast24h">How many errors were recorded in the last day.</param>
public readonly record struct HealthCounts(
    long SyncLagSeconds,
    int OutboxDepth,
    DateTimeOffset? LastBackupAt,
    DateTimeOffset? LastBackupVerifiedAt,
    int ErrorsLast24h);

/// <summary>Configuration quantities, for the hard limits and the daily rollup.</summary>
/// <param name="Stores">Live trading locations.</param>
/// <param name="Terminals">Enrolled terminals.</param>
/// <param name="TerminalsOnline">Terminals seen in the last hour.</param>
/// <param name="NamedUsers">Live users who can sign in.</param>
public readonly record struct ConfigurationCounts(
    int Stores,
    int Terminals,
    int TerminalsOnline,
    int NamedUsers);

/// <summary>A day's activity, as counts and nothing else.</summary>
/// <param name="ActiveUsers">Distinct users who changed anything.</param>
/// <param name="Writes">Rows written, from the audit trail.</param>
/// <param name="ModuleUsage">Writes per module, keyed by module name.</param>
/// <param name="StorageBytes">The database's size on disk.</param>
/// <param name="OutboxDepth">Replication operations outstanding at the end of the day.</param>
/// <param name="SnapshotsTaken">Backups taken.</param>
/// <param name="SnapshotsVerified">Backups read back and checked.</param>
/// <param name="SyncFailures">Replication attempts that failed.</param>
/// <param name="ConflictsOpen">Divergences waiting for a person.</param>
public readonly record struct ActivityCounts(
    int ActiveUsers,
    long Writes,
    IReadOnlyDictionary<string, long> ModuleUsage,
    long StorageBytes,
    int OutboxDepth,
    int SnapshotsTaken,
    int SnapshotsVerified,
    int SyncFailures,
    int ConflictsOpen);
