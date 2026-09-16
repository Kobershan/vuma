using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;

namespace VumaRetail.ControlPlane;

public sealed record ActivationRequest(Guid RequestId, string LicenseKey, string InstallId, string Fingerprint,
    string Version);
public sealed record LeaseRequest(Guid RequestId, string NodeId, string CurrentLeaseId, string Fingerprint,
    long MonotonicCounter, DateTimeOffset WallClock, string BootId, string Version);
public sealed record HeartbeatRequest(Guid RequestId, string NodeId, DateTimeOffset At, long UptimeSeconds,
    string Version, int TerminalsOnline, int TerminalsRegistered, long SyncLagSeconds, int OutboxDepth,
    DateTimeOffset? LastBackupAt, DateTimeOffset? LastBackupVerifiedAt, long MonotonicCounter, string BootId,
    bool ClockRollbackDetected, string IntegrityCheck, int ErrorsLast24Hours);
public sealed record MeteringRequest(Guid RequestId, string NodeId, DateOnly Period, MeteringCounts Counts,
    IReadOnlyDictionary<string, long> ModuleUsage, MeteringHealth Health);
public sealed record MeteringCounts(long Transactions, long ActiveUsers, long RegisteredUsers, long Terminals,
    long Stores, long StorageBytes, long ApiCalls, long DocumentsGenerated, long ImportsRun);
public sealed record MeteringHealth(long Crashes, long SyncFailures, long PrinterErrors, long OfflineMinutes);
public sealed record DeviceResponse(string RequestId, string NodeId, string? LeaseId, string? Signature,
    IReadOnlyList<string> Commands);

public interface ILicenseSigner
{
    Task<string> SignAsync(string payload, CancellationToken cancellationToken = default);
}

public sealed class ExternalLicenseSigner(HttpClient client, IConfiguration configuration) : ILicenseSigner
{
    public async Task<string> SignAsync(string payload, CancellationToken cancellationToken = default)
    {
        string endpoint = configuration["ControlPlane:SignerEndpoint"]?.Trim() ?? string.Empty;
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("No HTTPS external licence signer is configured.");
        using HttpResponseMessage response = await client.PostAsJsonAsync(uri, new { payload }, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("The external licence signer is unavailable.");
        SignResponse? result = await response.Content.ReadFromJsonAsync<SignResponse>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return !string.IsNullOrWhiteSpace(result?.Signature)
            ? result.Signature
            : throw new InvalidOperationException("The external licence signer returned no signature.");
    }

    private sealed record SignResponse(string Signature);
}

public sealed class ControlPlaneStore(ControlPlaneDbContext? database = null, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, (string Fingerprint, DeviceResponse Response)> _requests = [];
    private readonly Dictionary<Guid, string> _meteringRequests = [];
    private readonly Dictionary<string, string> _nodes = new(StringComparer.Ordinal);

    public async Task<DeviceResponse> ActivateAsync(ActivationRequest request, ILicenseSigner signer,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.LicenseKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.InstallId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Fingerprint);
        string fingerprint = Fingerprint(request);
        if (database is not null)
        {
            ControlPlaneRequest? persisted = await database.Requests.FindAsync([request.RequestId], cancellationToken).ConfigureAwait(false);
            if (persisted is not null)
            {
                if (persisted.Fingerprint != fingerprint) throw new InvalidOperationException("Request replay content differs.");
                return JsonSerializer.Deserialize<DeviceResponse>(persisted.ResponseJson)
                    ?? throw new InvalidOperationException("Persisted activation response is invalid.");
            }
        }
        lock (_gate)
        {
            if (_requests.TryGetValue(request.RequestId, out var replay))
            {
                if (replay.Fingerprint != fingerprint) throw new InvalidOperationException("Request replay content differs.");
                return replay.Response;
            }
        }
        string nodeId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.InstallId))).ToLowerInvariant()[..32];
        string leaseId = Guid.NewGuid().ToString("N");
        string payload = $"{nodeId}|{request.InstallId}|{request.Version}";
        string signature = await signer.SignAsync(payload, cancellationToken).ConfigureAwait(false);
        DeviceResponse response = new(request.RequestId.ToString("D"), nodeId, leaseId, signature, []);
        lock (_gate)
        {
            _nodes[nodeId] = request.InstallId;
            _requests[request.RequestId] = (fingerprint, response);
        }
        if (database is not null)
        {
            database.Devices.Add(new ControlPlaneDevice { NodeId = nodeId, InstallId = request.InstallId,
                Fingerprint = request.Fingerprint, LeaseId = leaseId, ActivatedAtUtc = _timeProvider.GetUtcNow() });
            database.Requests.Add(new ControlPlaneRequest { RequestId = request.RequestId, Fingerprint = fingerprint,
                ResponseJson = JsonSerializer.Serialize(response) });
            database.AuditEntries.Add(new ControlPlaneAuditEntry { Id = Guid.NewGuid(), OccurredAtUtc = _timeProvider.GetUtcNow(),
                Action = "device.activation", NodeId = nodeId, RequestId = request.RequestId.ToString("D") });
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        return response;
    }

    public async Task<DeviceResponse> HeartbeatAsync(HeartbeatRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateNonNegative(request.TerminalsOnline, nameof(request.TerminalsOnline));
        ValidateNonNegative(request.TerminalsRegistered, nameof(request.TerminalsRegistered));
        ValidateNonNegative(request.SyncLagSeconds, nameof(request.SyncLagSeconds));
        ValidateNonNegative(request.OutboxDepth, nameof(request.OutboxDepth));
        bool known = _nodes.ContainsKey(request.NodeId)
            || database is not null && await database.Devices.FindAsync([request.NodeId]).ConfigureAwait(false) is not null;
        string fingerprint = JsonSerializer.Serialize(request);
        DeviceResponse? persistedResponse = await ReadPersistedResponseAsync(request.RequestId, fingerprint).ConfigureAwait(false);
        if (persistedResponse is not null) return persistedResponse;
        DeviceResponse response;
        lock (_gate)
        {
            if (!known) throw new KeyNotFoundException("Unknown device node.");
            if (_requests.TryGetValue(request.RequestId, out var replay))
            {
                if (replay.Fingerprint != fingerprint) throw new InvalidOperationException("Request replay content differs.");
                return replay.Response;
            }
            response = new(request.RequestId.ToString("D"), request.NodeId, null, null, []);
            _requests[request.RequestId] = (fingerprint, response);
        }
        await PersistResponseAsync(request.RequestId, fingerprint, response, "device.heartbeat", request.NodeId).ConfigureAwait(false);
        return response;
    }

    public async Task<DeviceResponse> RefreshLeaseAsync(LeaseRequest request, ILicenseSigner signer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(signer);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.NodeId);
        bool known = _nodes.ContainsKey(request.NodeId)
            || database is not null && await database.Devices.FindAsync([request.NodeId]).ConfigureAwait(false) is not null;
        string fingerprint = JsonSerializer.Serialize(request);
        DeviceResponse? persistedResponse = await ReadPersistedResponseAsync(request.RequestId, fingerprint).ConfigureAwait(false);
        if (persistedResponse is not null) return persistedResponse;
        lock (_gate)
        {
            if (!known) throw new KeyNotFoundException("Unknown device node.");
            if (_requests.TryGetValue(request.RequestId, out var replay))
            {
                if (replay.Fingerprint != fingerprint) throw new InvalidOperationException("Request replay content differs.");
                return replay.Response;
            }
        }
        string leaseId = Guid.NewGuid().ToString("N");
        string signature = await signer.SignAsync($"{request.NodeId}|{leaseId}|{request.Version}", cancellationToken)
            .ConfigureAwait(false);
        DeviceResponse response = new(request.RequestId.ToString("D"), request.NodeId, leaseId, signature, []);
        lock (_gate)
        {
            _requests[request.RequestId] = (fingerprint, response);
        }
        await PersistResponseAsync(request.RequestId, fingerprint, response, "device.lease", request.NodeId).ConfigureAwait(false);
        if (database is not null)
        {
            ControlPlaneDevice? device = await database.Devices.FindAsync([request.NodeId], cancellationToken).ConfigureAwait(false);
            if (device is not null) device.LeaseId = leaseId;
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        return response;
    }

    public async Task AcceptMeteringAsync(MeteringRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateNonNegative(request.Counts.Transactions, nameof(request.Counts.Transactions));
        ValidateNonNegative(request.Counts.ActiveUsers, nameof(request.Counts.ActiveUsers));
        ValidateNonNegative(request.Counts.StorageBytes, nameof(request.Counts.StorageBytes));
        if (request.ModuleUsage.Keys.Any(string.IsNullOrWhiteSpace) || request.ModuleUsage.Values.Any(x => x < 0))
            throw new ArgumentException("Metering module usage is invalid.", nameof(request));
        bool known = _nodes.ContainsKey(request.NodeId)
            || database is not null && await database.Devices.FindAsync([request.NodeId]).ConfigureAwait(false) is not null;
        string fingerprint = JsonSerializer.Serialize(request);
        if (database is not null)
        {
            ControlPlaneMeteringReceipt? persisted = await database.MeteringReceipts
                .SingleOrDefaultAsync(x => x.RequestId == request.RequestId ||
                    x.NodeId == request.NodeId && x.Period == request.Period).ConfigureAwait(false);
            if (persisted is not null)
            {
                if (persisted.Fingerprint != fingerprint) throw new InvalidOperationException("Metering replay content differs.");
                return;
            }
        }
        lock (_gate)
        {
            if (!known) throw new KeyNotFoundException("Unknown device node.");
            if (_meteringRequests.TryGetValue(request.RequestId, out string? existing))
            {
                if (existing != fingerprint) throw new InvalidOperationException("Request replay content differs.");
                return;
            }
            _meteringRequests[request.RequestId] = fingerprint;
        }
        if (database is not null)
        {
            database.MeteringReceipts.Add(new ControlPlaneMeteringReceipt { RequestId = request.RequestId,
                NodeId = request.NodeId, Period = request.Period, Fingerprint = fingerprint,
                ReceivedAtUtc = _timeProvider.GetUtcNow() });
            database.AuditEntries.Add(new ControlPlaneAuditEntry { Id = Guid.NewGuid(), OccurredAtUtc = _timeProvider.GetUtcNow(),
                Action = "device.metering", NodeId = request.NodeId, RequestId = request.RequestId.ToString("D") });
            await database.SaveChangesAsync().ConfigureAwait(false);
        }
    }

    private async Task<DeviceResponse?> ReadPersistedResponseAsync(Guid requestId, string fingerprint)
    {
        if (database is null) return null;
        ControlPlaneRequest? persisted = await database.Requests.FindAsync([requestId]).ConfigureAwait(false);
        if (persisted is null) return null;
        if (persisted.Fingerprint != fingerprint) throw new InvalidOperationException("Request replay content differs.");
        return JsonSerializer.Deserialize<DeviceResponse>(persisted.ResponseJson)
            ?? throw new InvalidOperationException("Persisted control-plane response is invalid.");
    }

    private async Task PersistResponseAsync(Guid requestId, string fingerprint, DeviceResponse response,
        string action, string nodeId)
    {
        if (database is null || await database.Requests.FindAsync([requestId]).ConfigureAwait(false) is not null) return;
        database.Requests.Add(new ControlPlaneRequest { RequestId = requestId, Fingerprint = fingerprint,
            ResponseJson = JsonSerializer.Serialize(response) });
        database.AuditEntries.Add(new ControlPlaneAuditEntry { Id = Guid.NewGuid(), OccurredAtUtc = _timeProvider.GetUtcNow(),
            Action = action, NodeId = nodeId, RequestId = requestId.ToString("D") });
        await database.SaveChangesAsync().ConfigureAwait(false);
    }

    private static string Fingerprint(ActivationRequest request) => JsonSerializer.Serialize(request);
    private static void ValidateNonNegative(long value, string name)
    { if (value < 0) throw new ArgumentOutOfRangeException(name); }
}
