using Microsoft.Extensions.Caching.Distributed;
using VumaRetail.Application.Abstractions;

namespace VumaRetail.Infrastructure.Security.Identity;

/// <summary>Distributed access-token stamp revocation backed by the host cache.</summary>
public sealed class TokenRevocationCache(
    IDistributedCache cache,
    JwtOptions options) : ITokenRevocationCache
{
    private readonly IDistributedCache _cache = cache;
    private readonly JwtOptions _options = options;

    public Task RevokeStampAsync(Guid userId, string stamp, CancellationToken cancellationToken = default)
        => _cache.SetStringAsync(
            Key(userId, stamp),
            "1",
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = _options.AccessTokenLifetime,
            },
            cancellationToken);

    public async Task<bool> IsStampRevokedAsync(
        Guid userId,
        string stamp,
        CancellationToken cancellationToken = default)
        => await _cache.GetStringAsync(Key(userId, stamp), cancellationToken).ConfigureAwait(false) is not null;

    private static string Key(Guid userId, string stamp) => $"revoked-stamp:{userId}:{stamp}";
}
