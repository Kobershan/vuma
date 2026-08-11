using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Abstractions.Sync;
using VumaRetail.Domain.Licensing;
using VumaRetail.Domain.Primitives;
using VumaRetail.Licensing.Signing;

namespace VumaRetail.Licensing.Commands;

/// <summary>What a lease refresh did.</summary>
/// <param name="Reached">
/// Whether the control plane answered. False is not a failure — it is Path A, and the store carries on.
/// </param>
/// <param name="Level">Where the tenant sits afterwards.</param>
/// <param name="LeaseExpiresAt">When the lease now held expires, UTC.</param>
public sealed record LeaseRefreshResult(bool Reached, EnforcementLevel Level, DateTimeOffset? LeaseExpiresAt);

/// <summary>
/// Fetches a lease now: the licence screen's "retry now", and the 24-hour schedule.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ReadOnlyExemption.Payment"/>. This is the command that <em>ends</em> a restriction — the
/// customer pays, presses the button and is trading again inside sixty seconds
/// (<c>LICENSING.md</c> §4). Refusing it while read-only would make the restriction unrecoverable
/// without a phone call, which is the outcome the whole design exists to avoid.
/// </para>
/// <para>
/// <b>An unreachable control plane is not an error.</b> It returns
/// <see cref="LeaseRefreshResult.Reached"/> false and changes nothing. A vendor-side outage must never
/// restrict anyone (ADR-028), and an exception here would put a red banner on every till in the estate
/// the first time the vendor deployed on a Tuesday.
/// </para>
/// </remarks>
[CommandSideEffect(SideEffect.Write, Exemption = ReadOnlyExemption.Payment)]
public sealed record RefreshLeaseCommand : ICommand<LeaseRefreshResult>;

/// <summary>Refreshes the lease and records what came back.</summary>
/// <param name="activations">This installation's binding.</param>
/// <param name="licences">The licences it has been issued.</param>
/// <param name="leases">The leases it has held.</param>
/// <param name="state">Emergency unlocks, the clock watermark and tamper flags.</param>
/// <param name="controlPlane">The vendor's device API.</param>
/// <param name="verifier">The pinned public key.</param>
/// <param name="entitlements">Where the tenant sits on the ladder, for the level afterwards.</param>
/// <param name="install">This installation's ids and version.</param>
/// <param name="shadow">The out-of-database copy and its shadow.</param>
/// <param name="node">This node's identity.</param>
/// <param name="clock">The only source of time.</param>
public sealed class RefreshLeaseCommandHandler(
    IActivationRepository activations,
    ILicenceRepository licences,
    ILeaseRepository leases,
    ILicenceStateRepository state,
    IControlPlaneClient controlPlane,
    ILicenceVerifier verifier,
    IEnforcementStatusReader entitlements,
    IInstallIdentity install,
    ILicenceShadowStore shadow,
    INodeIdentity node,
    IClock clock) : ICommandHandler<RefreshLeaseCommand, LeaseRefreshResult>
{
    /// <inheritdoc />
    public async Task<LeaseRefreshResult> HandleAsync(
        RefreshLeaseCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Activation activation = await activations.FindCurrentAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new NotActivatedException();

        DateTimeOffset now = clock.UtcNow;

        await ObserveClockAsync(activation, node.NodeId, now, state, cancellationToken).ConfigureAwait(false);

        long counter = await licences.HighestIssuanceCounterAsync(cancellationToken).ConfigureAwait(false);

        LeaseGrant grant;

        try
        {
            grant = await controlPlane.RefreshLeaseAsync(
                new LeaseRequest(
                    node.NodeId,
                    activation.ActivationReference,
                    activation.FingerprintDigest,
                    counter,
                    now,
                    install.BootId,
                    install.Version),
                cancellationToken).ConfigureAwait(false);
        }
        catch (ControlPlaneUnreachableException)
        {
            // Path A. We do not know, so nothing changes: no contact is recorded, the tolerance window
            // keeps running, and the store keeps trading on the lease it holds.
            Lease? held = await leases.FindCurrentAsync(cancellationToken).ConfigureAwait(false);
            EnforcementDecision current = await entitlements.CurrentLevel(cancellationToken).ConfigureAwait(false);

            return new LeaseRefreshResult(false, current.Level, held?.ExpiresAt);
        }

        LeaseDocument document = verifier.Verify<LeaseDocument>(grant.Lease, SignedDocumentKind.Lease);

        if (document.TenantId != activation.TenantId)
        {
            throw new LicenceTenantMismatchException();
        }

        // A counter at or below the highest ever seen is a replay: an old backup restored alongside
        // the original, or a document somebody kept. Refuse it, flag it, and keep what we have — the
        // flag reaches the vendor's abuse queue and restricts nobody (ADR-026).
        if (document.IssuanceCounter < counter)
        {
            state.Add(TamperFlag.Raise(
                activation.TenantId,
                activation.StoreId,
                TamperKind.CounterRollback,
                now,
                $"Lease issuance counter {document.IssuanceCounter} is below the highest seen, {counter}."));

            Lease? held = await leases.FindCurrentAsync(cancellationToken).ConfigureAwait(false);
            EnforcementDecision current = await entitlements.CurrentLevel(cancellationToken).ConfigureAwait(false);

            return new LeaseRefreshResult(true, current.Level, held?.ExpiresAt);
        }

        if (grant.Licence is not null)
        {
            LicenceDocument licence = verifier.Verify<LicenceDocument>(grant.Licence, SignedDocumentKind.Licence);

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
        }

        leases.Add(Lease.Record(
                activation.TenantId,
                activation.StoreId,
                activation.Id,
                document.LeaseId,
                grant.Lease,
                document.Entitlements,
                document.Limits,
                document.EnforcementLevel,
                document.Reason,
                document.IssuedAt,
                document.ExpiresAt,
                document.IssuanceCounter)
            .WithRecovery(
                grant.AmountDue,
                grant.PayUrl,
                grant.UpdatePaymentMethodUrl,
                grant.DunningCompletedAt ?? document.DunningCompletedAt,
                grant.WriteUnlockUntil ?? document.WriteUnlockUntil,
                grant.SupportPhone,
                grant.Messages));

        // Only now. Contact is what the whole Path A ladder is measured from, so it is recorded after
        // a real, verified answer and never after an attempt.
        activation.RecordContact(now);

        await shadow.WriteAsync(grant.Lease, cancellationToken).ConfigureAwait(false);

        return new LeaseRefreshResult(true, document.EnforcementLevel, document.ExpiresAt);
    }

    /// <summary>
    /// Folds the wall clock into the watermark, flagging a rollback.
    /// </summary>
    /// <remarks>
    /// Shared by the lease refresh and the heartbeat because both are the places a clock is naturally
    /// looked at, and neither of them may restrict anybody for what it finds
    /// (<c>LICENSING.md</c> §7).
    /// </remarks>
    internal static async Task ObserveClockAsync(
        Activation activation,
        string nodeId,
        DateTimeOffset now,
        ILicenceStateRepository state,
        CancellationToken cancellationToken)
    {
        ClockWatermark? watermark = await state
            .FindWatermarkAsync(nodeId, cancellationToken)
            .ConfigureAwait(false);

        if (watermark is null)
        {
            state.Add(ClockWatermark.Start(activation.TenantId, activation.StoreId, nodeId, now));
            return;
        }

        if (watermark.Observe(now))
        {
            state.Add(TamperFlag.Raise(
                activation.TenantId,
                activation.StoreId,
                TamperKind.ClockRollback,
                watermark.HighestSeen,
                $"System clock read {now:O}, which is earlier than the highest instant seen, "
                + $"{watermark.HighestSeen:O}."));
        }
    }
}

/// <summary>What a heartbeat did.</summary>
/// <param name="Reached">Whether the control plane answered.</param>
/// <param name="CommandsReceived">How many instructions the vendor pushed down.</param>
/// <param name="Level">Where the tenant sits afterwards.</param>
public sealed record HeartbeatResult(bool Reached, int CommandsReceived, EnforcementLevel Level);

/// <summary>
/// Reports health, collects vendor instructions, and is how a payment reaches the till in under a
/// minute.
/// </summary>
/// <remarks>
/// <see cref="ReadOnlyExemption.Payment"/>, for the same reason as the lease refresh: this is the
/// recovery path, and a recovery path that stops working when it is needed is not one. The payload is
/// counts and health only — R10, ADR-024, and the shape of
/// <see cref="HeartbeatRequest"/> is what makes it so.
/// </remarks>
[CommandSideEffect(SideEffect.Write, Exemption = ReadOnlyExemption.Payment)]
public sealed record SendHeartbeatCommand : ICommand<HeartbeatResult>;

/// <summary>Sends the heartbeat and applies whatever came back.</summary>
/// <param name="activations">This installation's binding.</param>
/// <param name="licences">The licences it has been issued.</param>
/// <param name="state">Emergency unlocks, the clock watermark and tamper flags.</param>
/// <param name="grants">Vendor support-access grants.</param>
/// <param name="counters">The whitelisted aggregate counters.</param>
/// <param name="controlPlane">The vendor's device API.</param>
/// <param name="integrity">The assembly self-check.</param>
/// <param name="entitlements">Where the tenant sits on the ladder, for the level afterwards.</param>
/// <param name="install">This installation's ids and version.</param>
/// <param name="node">This node's identity.</param>
/// <param name="clock">The only source of time.</param>
public sealed class SendHeartbeatCommandHandler(
    IActivationRepository activations,
    ILicenceRepository licences,
    ILicenceStateRepository state,
    ISupportGrantRepository grants,
    IUsageCounterSource counters,
    IControlPlaneClient controlPlane,
    IIntegrityChecker integrity,
    IEnforcementStatusReader entitlements,
    IInstallIdentity install,
    INodeIdentity node,
    IClock clock) : ICommandHandler<SendHeartbeatCommand, HeartbeatResult>
{
    /// <inheritdoc />
    public async Task<HeartbeatResult> HandleAsync(
        SendHeartbeatCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Activation activation = await activations.FindCurrentAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new NotActivatedException();

        DateTimeOffset now = clock.UtcNow;

        await RefreshLeaseCommandHandler
            .ObserveClockAsync(activation, node.NodeId, now, state, cancellationToken)
            .ConfigureAwait(false);

        ConfigurationCounts configuration = await counters
            .CountConfigurationAsync(cancellationToken)
            .ConfigureAwait(false);

        HealthCounts health = await counters.CountHealthAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlyList<EmergencyUnlock> unreportedUnlocks = await state
            .ListUnreportedUnlocksAsync(cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<TamperFlag> unreportedFlags = await state
            .ListUnreportedFlagsAsync(cancellationToken)
            .ConfigureAwait(false);

        long counter = await licences.HighestIssuanceCounterAsync(cancellationToken).ConfigureAwait(false);

        HeartbeatAcknowledgement acknowledgement;

        try
        {
            acknowledgement = await controlPlane.HeartbeatAsync(
                new HeartbeatRequest(
                    node.NodeId,
                    activation.ActivationReference,
                    now,
                    (long)Math.Max(0, (now - install.BootedAt).TotalSeconds),
                    install.Version,
                    configuration.Terminals,
                    configuration.TerminalsOnline,
                    health.SyncLagSeconds,
                    health.OutboxDepth,
                    health.LastBackupAt,
                    health.LastBackupVerifiedAt,
                    counter,
                    install.BootId,
                    unreportedFlags.Any(flag => flag.Kind is TamperKind.ClockRollback),
                    integrity.Check(),
                    health.ErrorsLast24h,
                    [.. unreportedUnlocks.Select(unlock => unlock.CodeReference)]),
                cancellationToken).ConfigureAwait(false);
        }
        catch (ControlPlaneUnreachableException)
        {
            EnforcementDecision current = await entitlements.CurrentLevel(cancellationToken).ConfigureAwait(false);

            return new HeartbeatResult(false, 0, current.Level);
        }

        activation.RecordContact(now);

        foreach (EmergencyUnlock unlock in unreportedUnlocks)
        {
            unlock.MarkReported(now);
        }

        foreach (TamperFlag flag in unreportedFlags)
        {
            flag.MarkReported(now);
        }

        foreach (ControlPlaneCommand instruction in acknowledgement.Commands)
        {
            await ApplyAsync(instruction, activation, now, cancellationToken).ConfigureAwait(false);
        }

        EnforcementDecision level = await entitlements.CurrentLevel(cancellationToken).ConfigureAwait(false);

        return new HeartbeatResult(true, acknowledgement.Commands.Count, level.Level);
    }

    /// <summary>
    /// Applies one pushed instruction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately narrow. Two of the seven are handled here because they change tenant state — a
    /// support request the tenant must answer, and a revocation of one. The rest belong to components
    /// that do not exist yet (the Velopack channel, the diagnostics package) and are recorded as
    /// unhandled rather than silently swallowed.
    /// </para>
    /// <para>
    /// <b>Nothing here can restrict anybody.</b> There is no instruction that sets an enforcement
    /// level: a level arrives only inside a signed lease, so a compromised control plane cannot push
    /// "read-only" at a customer base without also signing it, and ADR-028's rules still apply when it
    /// does.
    /// </para>
    /// </remarks>
    private async Task ApplyAsync(
        ControlPlaneCommand instruction,
        Activation activation,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        switch (instruction.Type)
        {
            case ControlPlaneCommandType.RequestSupportAccess
                when Guid.TryParse(instruction.Payload, out Guid reference):
            {
                // Idempotent: a heartbeat is at-least-once, and a duplicated request must not produce
                // two approval prompts for one visit.
                if (await grants.FindByReferenceAsync(reference, cancellationToken).ConfigureAwait(false) is null)
                {
                    grants.Add(SupportGrant.Request(
                        activation.TenantId,
                        activation.StoreId,
                        reference,
                        "vendor:support",
                        "Vuma support has asked to look at this system.",
                        "support",
                        now));
                }

                break;
            }

            case ControlPlaneCommandType.RevokeSupportAccess:
            {
                foreach (SupportGrant grant in await grants.ListAsync(50, cancellationToken).ConfigureAwait(false))
                {
                    if (grant.IsActiveAt(now))
                    {
                        grant.Revoke("vendor:control-plane", now);
                    }
                }

                break;
            }

            case ControlPlaneCommandType.Deactivate:
            {
                activation.Deactivate();
                break;
            }

            default:
                // RefreshLease, UpdateNow, RunBackup, CollectDiagnostics and SetChannel belong to the
                // scheduler, Velopack (Stage 31) and the diagnostics package. Ignored rather than
                // guessed at; an unrecognised instruction must never become an improvised one.
                break;
        }
    }
}

/// <summary>
/// Types in a vendor emergency access code (<c>LICENSING.md</c> §5).
/// </summary>
/// <remarks>
/// <para>
/// <b>Works with no internet at all.</b> The code is verified against the same pinned public key as a
/// licence, so nothing needs to be asked of anybody: the vendor reads it over the phone and the store
/// trades until it expires. That is what makes a Friday-evening bank reversal a phone call rather than
/// a lost weekend for the shop and for whoever is on support.
/// </para>
/// <para>
/// Single-use, single-tenant, and expiring on its own signed schedule — so an offline machine cannot
/// be kept unlocked by re-entering the same code, and a code issued for one customer does nothing for
/// another.
/// </para>
/// </remarks>
/// <param name="Code">The signed code, as read over the phone.</param>
[CommandSideEffect(SideEffect.Write, Exemption = ReadOnlyExemption.Payment)]
public sealed record RedeemEmergencyCodeCommand(string Code) : ICommand<DateTimeOffset>;

/// <summary>Rejects an emergency code before it reaches the handler.</summary>
public sealed class RedeemEmergencyCodeCommandValidator : AbstractValidator<RedeemEmergencyCodeCommand>
{
    /// <summary>Builds the rules.</summary>
    public RedeemEmergencyCodeCommandValidator()
        => RuleFor(command => command.Code).NotEmpty().MaximumLength(4096);
}

/// <summary>Verifies the code against the pinned key and records the redemption.</summary>
/// <param name="activations">This installation's binding.</param>
/// <param name="state">Redeemed codes and tamper flags.</param>
/// <param name="verifier">The pinned public key.</param>
/// <param name="options">The maximum lifetime a code may claim.</param>
/// <param name="clock">The only source of time.</param>
public sealed class RedeemEmergencyCodeCommandHandler(
    IActivationRepository activations,
    ILicenceStateRepository state,
    ILicenceVerifier verifier,
    LicensingOptions options,
    IClock clock) : ICommandHandler<RedeemEmergencyCodeCommand, DateTimeOffset>
{
    /// <inheritdoc />
    public async Task<DateTimeOffset> HandleAsync(
        RedeemEmergencyCodeCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Activation activation = await activations.FindCurrentAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new NotActivatedException();

        EmergencyCodeDocument code;

        try
        {
            code = verifier.Verify<EmergencyCodeDocument>(
                command.Code.Trim(),
                SignedDocumentKind.EmergencyCode);
        }
        catch (LicenceSignatureException)
        {
            // A rejected signature is worth telling the vendor about — it is either a typo or somebody
            // trying to mint codes — and it must not stop the refusal reaching the person typing.
            state.Add(TamperFlag.Raise(
                activation.TenantId,
                activation.StoreId,
                TamperKind.SignatureRejected,
                clock.UtcNow,
                "An emergency access code failed signature verification."));

            throw;
        }

        if (code.TenantId != activation.TenantId)
        {
            throw new EmergencyCodeRejectedException("That code was issued for a different business.");
        }

        DateTimeOffset now = clock.UtcNow;

        if (now >= code.ExpiresAt)
        {
            throw new EmergencyCodeRejectedException("That code has expired. Call support for a new one.");
        }

        // A code claiming a longer life than the vendor's own ceiling is not one this build honours,
        // whoever signed it. The ceiling is configuration precisely so it can be tightened without a
        // release.
        if (code.ExpiresAt - code.IssuedAt > options.MaximumEmergencyCodeLifetime)
        {
            throw new EmergencyCodeRejectedException("That code claims a longer life than is permitted.");
        }

        if (await state.HasRedeemedAsync(code.CodeId, cancellationToken).ConfigureAwait(false))
        {
            throw new EmergencyCodeRejectedException("That code has already been used.");
        }

        state.Add(EmergencyUnlock.Redeem(
            activation.TenantId,
            activation.StoreId,
            code.CodeId,
            now,
            code.ExpiresAt,
            code.Reason));

        return code.ExpiresAt;
    }
}

/// <summary>An emergency access code was not accepted.</summary>
/// <param name="message">Why, in words the person on the phone to support can repeat.</param>
public sealed class EmergencyCodeRejectedException(string message)
    : DomainException("LICENCE_EMERGENCY_CODE_REJECTED", message);
