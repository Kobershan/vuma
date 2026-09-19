using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using VumaRetail.Contracts.Sync;

namespace VumaRetail.Desktop.Offline;

/// <summary>Flushes the terminal outbox through the authenticated sync-batch endpoint.</summary>
public sealed class TerminalSyncTransport(HttpClient http, string baseUrl, TerminalSyncOutbox outbox)
{
    /// <summary>Posts one bounded batch and settles only the outcomes the receiver acknowledged.</summary>
    public async Task<SyncBatchResponse?> FlushAsync(
        Guid tenantId,
        Guid? storeId,
        Guid terminalId,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<QueuedOperation> queued = await outbox.PendingAsync(limit, cancellationToken).ConfigureAwait(false);
        if (queued.Count == 0) return null;

        var request = new SyncBatchRequest(
            $"terminal:{terminalId:N}",
            "Terminal",
            tenantId,
            storeId,
            queued.Select(item => item.Operation).ToList());

        using HttpResponseMessage response = await http.PostAsJsonAsync(
            $"{baseUrl.TrimEnd('/')}/sync/batches", request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Sync endpoint returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)}");

        SyncBatchResponse result = await response.Content.ReadFromJsonAsync<SyncBatchResponse>(cancellationToken)
            ?? throw new InvalidOperationException("The sync endpoint returned no acknowledgement.");
        await outbox.SettleAsync(result, cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <summary>Sets the access token on the transport's shared API client.</summary>
    public void SetAccessToken(string token)
        => http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
}
