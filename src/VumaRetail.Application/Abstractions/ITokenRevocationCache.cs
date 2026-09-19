namespace VumaRetail.Application.Abstractions;

/// <summary>Short-lived revocation state for access-token security stamps.</summary>
public interface ITokenRevocationCache
{
    /// <summary>Marks a security stamp revoked for the access-token lifetime.</summary>
    Task RevokeStampAsync(Guid userId, string stamp, CancellationToken cancellationToken = default);

    /// <summary>Returns whether the security stamp was revoked.</summary>
    Task<bool> IsStampRevokedAsync(Guid userId, string stamp, CancellationToken cancellationToken = default);
}
