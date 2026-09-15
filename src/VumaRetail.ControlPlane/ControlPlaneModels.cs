using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net.Http.Json;

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

public sealed class ControlPlaneStore
{
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
        return response;
    }

    public DeviceResponse Heartbeat(HeartbeatRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateNonNegative(request.TerminalsOnline, nameof(request.TerminalsOnline));
        ValidateNonNegative(request.TerminalsRegistered, nameof(request.TerminalsRegistered));
        ValidateNonNegative(request.SyncLagSeconds, nameof(request.SyncLagSeconds));
        ValidateNonNegative(request.OutboxDepth, nameof(request.OutboxDepth));
        lock (_gate)
        {
            if (!_nodes.ContainsKey(request.NodeId)) throw new KeyNotFoundException("Unknown device node.");
            string fingerprint = JsonSerializer.Serialize(request);
            if (_requests.TryGetValue(request.RequestId, out var replay))
            {
                if (replay.Fingerprint != fingerprint) throw new InvalidOperationException("Request replay content differs.");
                return replay.Response;
            }
            DeviceResponse response = new(request.RequestId.ToString("D"), request.NodeId, null, null, []);
            _requests[request.RequestId] = (fingerprint, response);
            return response;
        }
    }

    public async Task<DeviceResponse> RefreshLeaseAsync(LeaseRequest request, ILicenseSigner signer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(signer);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.NodeId);
        lock (_gate)
        {
            if (!_nodes.ContainsKey(request.NodeId)) throw new KeyNotFoundException("Unknown device node.");
            string fingerprint = JsonSerializer.Serialize(request);
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
            _requests[request.RequestId] = (JsonSerializer.Serialize(request), response);
        }
        return response;
    }

    public void AcceptMetering(MeteringRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateNonNegative(request.Counts.Transactions, nameof(request.Counts.Transactions));
        ValidateNonNegative(request.Counts.ActiveUsers, nameof(request.Counts.ActiveUsers));
        ValidateNonNegative(request.Counts.StorageBytes, nameof(request.Counts.StorageBytes));
        if (request.ModuleUsage.Keys.Any(string.IsNullOrWhiteSpace) || request.ModuleUsage.Values.Any(x => x < 0))
            throw new ArgumentException("Metering module usage is invalid.", nameof(request));
        lock (_gate)
        {
            if (!_nodes.ContainsKey(request.NodeId)) throw new KeyNotFoundException("Unknown device node.");
            string fingerprint = JsonSerializer.Serialize(request);
            if (_meteringRequests.TryGetValue(request.RequestId, out string? existing))
            {
                if (existing != fingerprint) throw new InvalidOperationException("Request replay content differs.");
                return;
            }
            _meteringRequests[request.RequestId] = fingerprint;
        }
    }

    private static string Fingerprint(ActivationRequest request) => JsonSerializer.Serialize(request);
    private static void ValidateNonNegative(long value, string name)
    { if (value < 0) throw new ArgumentOutOfRangeException(name); }
}
