using VumaRetail.Domain.Conversations;

namespace VumaRetail.Application.Conversations;

/// <summary>Tenant-side administration of channel bindings and OTP challenges.</summary>
public interface IContactBindingManagementService
{
    /// <summary>Creates a tenant-owned binding.</summary>
    Task<ContactBinding> CreateAsync(ContactBinding binding, CancellationToken cancellationToken = default);
    /// <summary>Lists tenant-owned bindings with optional filters.</summary>
    Task<IReadOnlyList<ContactBinding>> ListAsync(ConversationChannel? channel = null, Guid? contactId = null, CancellationToken cancellationToken = default);
    /// <summary>Issues a short-lived challenge for a binding.</summary>
    Task<VerificationChallenge> IssueChallengeAsync(Guid bindingId, string otp, CancellationToken cancellationToken = default);
    /// <summary>Consumes a challenge and verifies its binding.</summary>
    Task<bool> VerifyAsync(Guid bindingId, Guid challengeId, string otp, CancellationToken cancellationToken = default);
    /// <summary>Revokes a binding.</summary>
    Task<bool> RevokeAsync(Guid bindingId, CancellationToken cancellationToken = default);
}
