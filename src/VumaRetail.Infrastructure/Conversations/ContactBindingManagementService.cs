using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Conversations;
using VumaRetail.Domain.Conversations;
using VumaRetail.Infrastructure.Persistence;

namespace VumaRetail.Infrastructure.Conversations;

/// <summary>Persists registry bindings and challenges through the registry transaction boundary.</summary>
public sealed class ContactBindingManagementService(
    VumaRegistryDbContext registry,
    IClock clock,
    IVerificationService verification) : IContactBindingManagementService
{
    public async Task<ContactBinding> CreateAsync(ContactBinding binding, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);
        registry.ContactBindings.Add(binding);
        await registry.CommitAsync(cancellationToken).ConfigureAwait(false);
        return binding;
    }

    public async Task<IReadOnlyList<ContactBinding>> ListAsync(ConversationChannel? channel = null, Guid? contactId = null, CancellationToken cancellationToken = default)
    {
        IQueryable<ContactBinding> query = registry.ContactBindings.AsNoTracking();
        if (channel is not null) query = query.Where(x => x.Channel == channel);
        if (contactId is not null) query = query.Where(x => x.ContactId == contactId);
        return await query.OrderBy(x => x.Address).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<VerificationChallenge> IssueChallengeAsync(Guid bindingId, string otp, CancellationToken cancellationToken = default)
    {
        ContactBinding binding = await RequireBindingAsync(bindingId, cancellationToken).ConfigureAwait(false);
        VerificationChallenge challenge = verification.Issue(binding, otp, clock.UtcNow);
        registry.VerificationChallenges.Add(challenge);
        await registry.CommitAsync(cancellationToken).ConfigureAwait(false);
        return challenge;
    }

    public async Task<bool> VerifyAsync(Guid bindingId, Guid challengeId, string otp, CancellationToken cancellationToken = default)
    {
        ContactBinding binding = await RequireBindingAsync(bindingId, cancellationToken).ConfigureAwait(false);
        VerificationChallenge challenge = await registry.Set<VerificationChallenge>()
            .SingleOrDefaultAsync(x => x.Id == challengeId && x.BindingId == bindingId, cancellationToken)
            ?? throw new InvalidOperationException("Verification challenge not found.");
        bool verified = verification.Verify(binding, challenge, otp, clock.UtcNow);
        await registry.CommitAsync(cancellationToken).ConfigureAwait(false);
        return verified;
    }

    public async Task<bool> RevokeAsync(Guid bindingId, CancellationToken cancellationToken = default)
    {
        ContactBinding? binding = await registry.ContactBindings.SingleOrDefaultAsync(x => x.Id == bindingId, cancellationToken);
        if (binding is null) return false;
        binding.Revoke();
        await registry.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> SetConsentAsync(Guid bindingId, bool granted, CancellationToken cancellationToken = default)
    {
        ContactBinding? binding = await registry.ContactBindings.SingleOrDefaultAsync(x => x.Id == bindingId, cancellationToken);
        if (binding is null) return false;
        if (granted) binding.GrantConsent(); else binding.WithdrawConsent();
        await registry.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<ContactBinding> RequireBindingAsync(Guid bindingId, CancellationToken cancellationToken)
        => await registry.ContactBindings.SingleOrDefaultAsync(x => x.Id == bindingId, cancellationToken)
            .ConfigureAwait(false) ?? throw new InvalidOperationException("Contact binding not found.");
}
