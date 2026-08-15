using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Abstractions.Sync;
using VumaRetail.Domain.Licensing;
using VumaRetail.Domain.Primitives;
using VumaRetail.Licensing.Signing;

namespace VumaRetail.Licensing.Commands;

/// <summary>What an activation produced.</summary>
/// <param name="ActivationId">The local activation row.</param>
/// <param name="TenantId">The tenant the key resolved to.</param>
/// <param name="PlanCode">The plan the customer is on.</param>
/// <param name="LicenceExpiresAt">When the licence stops being current, UTC.</param>
/// <param name="Level">Where the tenant sits on the ladder immediately afterwards.</param>
public sealed record ActivationResult(
    Guid ActivationId,
    Guid TenantId,
    string PlanCode,
    DateTimeOffset LicenceExpiresAt,
    EnforcementLevel Level);

/// <summary>
/// Binds this installation to a licence key and this machine (<c>LICENSING.md</c> §3).
/// </summary>
/// <remarks>
/// <see cref="ReadOnlyExemption.Payment"/>, and it has to be. An unactivated installation is
/// read-only, so a command that could not run while read-only could never run at all — which is the
/// shape of every carve-out in this module: the licence screen must work in the state the licence
/// screen exists to fix.
/// </remarks>
/// <param name="LicenceKey">The key the customer typed.</param>
/// <param name="StoreName">What the customer calls this shop, for the vendor's console.</param>
/// <param name="ContactEmail">Where the vendor sends dunning and card-expiry warnings.</param>
[CommandSideEffect(SideEffect.Write, Exemption = ReadOnlyExemption.Payment)]
public sealed record ActivateInstallationCommand(string LicenceKey, string StoreName, string ContactEmail)
    : ICommand<ActivationResult>;

/// <summary>Rejects an activation before anything reaches the network.</summary>
/// <remarks>
/// The checksum is checked here, in the validator, on purpose: a typo should fail in front of the
/// person who made it rather than after a round trip they have to wait for
/// (<c>LICENSING.md</c> §2).
/// </remarks>
public sealed class ActivateInstallationCommandValidator : AbstractValidator<ActivateInstallationCommand>
{
    /// <summary>Builds the rules.</summary>
    public ActivateInstallationCommandValidator()
    {
        RuleFor(command => command.LicenceKey)
            .NotEmpty()
            .Must(key => LicenceKey.TryParse(key, out _))
            .WithMessage("That licence key is not valid — check it for a typo and try again.");

        RuleFor(command => command.StoreName).NotEmpty().MaximumLength(128);
        RuleFor(command => command.ContactEmail).NotEmpty().EmailAddress().MaximumLength(256);
    }
}

/// <summary>Activates against the control plane and records what came back.</summary>
/// <param name="activations">This installation's binding.</param>
/// <param name="licences">The licences it has been issued.</param>
/// <param name="leases">The leases it has held.</param>
/// <param name="controlPlane">The vendor's device API.</param>
/// <param name="verifier">The pinned public key.</param>
/// <param name="fingerprints">This machine's hardware characteristics.</param>
/// <param name="install">This installation's ids and version.</param>
/// <param name="shadow">The out-of-database copy and its shadow.</param>
/// <param name="tenant">The tenant this host serves.</param>
/// <param name="node">This node's identity.</param>
/// <param name="clock">The only source of time.</param>
public sealed class ActivateInstallationCommandHandler(
    IActivationRepository activations,
    ILicenceRepository licences,
    ILeaseRepository leases,
    IControlPlaneClient controlPlane,
    ILicenceVerifier verifier,
    IHardwareFingerprintProvider fingerprints,
    IInstallIdentity install,
    ILicenceShadowStore shadow,
    ITenantContext tenant,
    INodeIdentity node,
    IClock clock) : ICommandHandler<ActivateInstallationCommand, ActivationResult>
{
    /// <inheritdoc />
    public async Task<ActivationResult> HandleAsync(
        ActivateInstallationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        LicenceKey key = LicenceKey.Parse(command.LicenceKey);

        if (await activations.FindCurrentAsync(cancellationToken).ConfigureAwait(false)
            is { State: ActivationState.Active })
        {
            throw new AlreadyActivatedException();
        }

        IReadOnlyDictionary<FingerprintComponent, string> reading = fingerprints.Read();
        HardwareFingerprint fingerprint = HardwareFingerprint.Capture(HardwareFingerprint.NewSalt(), reading);

        ActivationGrant grant = await controlPlane.ActivateAsync(
            new ActivationRequest(
                key.Value,
                fingerprint.Digest(),
                fingerprint.ComponentHashes,
                install.InstallId,
                node.NodeId,
                command.StoreName,
                command.ContactEmail,
                install.Version),
            cancellationToken).ConfigureAwait(false);

        // The control plane says which tenant the key belongs to; this host says which tenant it
        // serves. A mismatch means somebody has typed another business's key into this server, and
        // proceeding would write one tenant's licence into another tenant's rows.
        if (tenant.TenantId != Guid.Empty && grant.TenantId != tenant.TenantId)
        {
            throw new LicenceTenantMismatchException();
        }

        LicenceDocument licence = verifier.Verify<LicenceDocument>(grant.Licence, SignedDocumentKind.Licence);
        LeaseDocument lease = verifier.Verify<LeaseDocument>(grant.Lease, SignedDocumentKind.Lease);

        DateTimeOffset now = clock.UtcNow;

        Activation activation = Activation.Open(
            grant.TenantId,
            grant.StoreId,
            key,
            install.InstallId,
            node.NodeId,
            fingerprint,
            grant.ActivationReference,
            now);

        activations.Add(activation);

        licences.Add(Licence.Record(
            grant.TenantId,
            grant.StoreId,
            activation.Id,
            grant.Licence,
            licence.PlanCode,
            licence.Entitlements,
            licence.Limits,
            licence.IssuedAt,
            licence.ExpiresAt,
            licence.FingerprintDigest,
            licence.Nonce,
            licence.IssuanceCounter));

        leases.Add(Lease.Record(
            grant.TenantId,
            grant.StoreId,
            activation.Id,
            lease.LeaseId,
            grant.Lease,
            lease.Entitlements,
            lease.Limits,
            lease.EnforcementLevel,
            lease.Reason,
            lease.IssuedAt,
            lease.ExpiresAt,
            lease.IssuanceCounter));

        await shadow.WriteAsync(grant.Lease, cancellationToken).ConfigureAwait(false);

        return new ActivationResult(
            activation.Id,
            grant.TenantId,
            licence.PlanCode,
            licence.ExpiresAt,
            lease.EnforcementLevel);
    }
}

/// <summary>
/// Moves this tenant's binding to different hardware (<c>LICENSING.md</c> §3).
/// </summary>
/// <remarks>
/// <see cref="RebindReason.DisasterRecovery"/> is auto-approved by the control plane, without
/// exception. The Stage 04 restore path issues one of these on the way back up, so a rebuilt store
/// never meets an activation error — if the building has burned down, an activation error is the last
/// thing anybody needs (R4).
/// </remarks>
/// <param name="Reason">Why the binding is moving.</param>
/// <param name="Evidence">Anything supporting it, for a vendor decision.</param>
[CommandSideEffect(SideEffect.Write, Exemption = ReadOnlyExemption.Payment)]
public sealed record RebindActivationCommand(RebindReason Reason, string? Evidence = null)
    : ICommand<ActivationResult>;

/// <summary>Rejects a rebind before it reaches the handler.</summary>
public sealed class RebindActivationCommandValidator : AbstractValidator<RebindActivationCommand>
{
    /// <summary>Builds the rules.</summary>
    public RebindActivationCommandValidator()
    {
        RuleFor(command => command.Reason).IsInEnum();
        RuleFor(command => command.Evidence).MaximumLength(1024);
    }
}

/// <summary>Rebinds against the control plane and records what came back.</summary>
/// <param name="activations">This installation's binding.</param>
/// <param name="licences">The licences it has been issued.</param>
/// <param name="leases">The leases it has held.</param>
/// <param name="controlPlane">The vendor's device API.</param>
/// <param name="verifier">The pinned public key.</param>
/// <param name="fingerprints">This machine's hardware characteristics.</param>
/// <param name="shadow">The out-of-database copy and its shadow.</param>
/// <param name="clock">The only source of time.</param>
public sealed class RebindActivationCommandHandler(
    IActivationRepository activations,
    ILicenceRepository licences,
    ILeaseRepository leases,
    IControlPlaneClient controlPlane,
    ILicenceVerifier verifier,
    IHardwareFingerprintProvider fingerprints,
    ILicenceShadowStore shadow,
    IClock clock) : ICommandHandler<RebindActivationCommand, ActivationResult>
{
    /// <inheritdoc />
    public async Task<ActivationResult> HandleAsync(
        RebindActivationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Activation activation = await activations.FindCurrentAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new NotActivatedException();

        HardwareFingerprint fingerprint = HardwareFingerprint.Capture(
            HardwareFingerprint.NewSalt(),
            fingerprints.Read());

        ActivationGrant grant = await controlPlane.RebindAsync(
            new RebindRequest(
                activation.ActivationReference,
                fingerprint.Digest(),
                fingerprint.ComponentHashes,
                command.Reason,
                command.Evidence),
            cancellationToken).ConfigureAwait(false);

        DateTimeOffset now = clock.UtcNow;

        if (grant.PendingVendorApproval)
        {
            // The store keeps running on what it already holds. A rebind waiting for a person is not
            // a reason to stop anybody trading, and the Path A window is measured from contact, which
            // this was.
            activation.MarkRebindPending();
            activation.RecordContact(now);

            Licence? existing = await licences.FindCurrentAsync(cancellationToken).ConfigureAwait(false);

            return new ActivationResult(
                activation.Id,
                activation.TenantId,
                existing?.PlanCode ?? string.Empty,
                existing?.ExpiresAt ?? now,
                EnforcementLevel.Normal);
        }

        LicenceDocument licence = verifier.Verify<LicenceDocument>(grant.Licence, SignedDocumentKind.Licence);
        LeaseDocument lease = verifier.Verify<LeaseDocument>(grant.Lease, SignedDocumentKind.Lease);

        activation.RebindTo(fingerprint, now);
        activation.RecordContact(now);

        licences.Add(Licence.Record(
            activation.TenantId,
            activation.StoreId,
            activation.Id,
            grant.Licence,
            licence.PlanCode,
            licence.Entitlements,
            licence.Limits,
            licence.IssuedAt,
            licence.ExpiresAt,
            licence.FingerprintDigest,
            licence.Nonce,
            licence.IssuanceCounter));

        leases.Add(Lease.Record(
            activation.TenantId,
            activation.StoreId,
            activation.Id,
            lease.LeaseId,
            grant.Lease,
            lease.Entitlements,
            lease.Limits,
            lease.EnforcementLevel,
            lease.Reason,
            lease.IssuedAt,
            lease.ExpiresAt,
            lease.IssuanceCounter));

        await shadow.WriteAsync(grant.Lease, cancellationToken).ConfigureAwait(false);

        return new ActivationResult(
            activation.Id,
            activation.TenantId,
            licence.PlanCode,
            licence.ExpiresAt,
            lease.EnforcementLevel);
    }
}

/// <summary>This installation is already bound to a licence.</summary>
public sealed class AlreadyActivatedException()
    : DomainException(
        "LICENCE_ALREADY_ACTIVATED",
        "This installation is already activated. To move it to another machine, use the rebind option.",
        DomainProblemKind.Conflict);

/// <summary>Something needed an activation and this installation has never had one.</summary>
public sealed class NotActivatedException()
    : DomainException(
        "LICENCE_NOT_ACTIVATED",
        "This installation has not been activated yet. Enter your licence key to begin.",
        DomainProblemKind.NotFound);

/// <summary>The licence key belongs to a different business than this server serves.</summary>
public sealed class LicenceTenantMismatchException()
    : DomainException(
        "LICENCE_TENANT_MISMATCH",
        "That licence key belongs to a different business. Check that you are entering the key that "
        + "was issued for this store.");
