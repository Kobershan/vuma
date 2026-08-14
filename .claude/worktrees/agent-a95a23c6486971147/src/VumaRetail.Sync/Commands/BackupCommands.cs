using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Backup;
using VumaRetail.Domain.Backup;

namespace VumaRetail.Sync.Commands;

/// <summary>
/// Takes a snapshot now: dump, encrypt, upload, checksum, record.
/// </summary>
/// <remarks>
/// <see cref="ReadOnlyExemption.Backup"/>, which is the carve-out ADR-028 wrote for exactly this. A
/// tenant's data protection must never depend on their subscription status — a lapsed tenant whose
/// backups had stopped would be a tenant whose data the vendor had put at risk to collect a debt, and
/// R4's restore path has to stay intact regardless.
/// </remarks>
/// <param name="Kind">What the snapshot covers.</param>
[CommandSideEffect(SideEffect.Write, Exemption = ReadOnlyExemption.Backup)]
public sealed record CreateBackupSnapshotCommand(BackupKind Kind = BackupKind.Full) : ICommand<Guid>;

/// <summary>Rejects a create-snapshot command before anything is dumped.</summary>
public sealed class CreateBackupSnapshotCommandValidator : AbstractValidator<CreateBackupSnapshotCommand>
{
    /// <summary>Builds the rules.</summary>
    public CreateBackupSnapshotCommandValidator()
    {
        // Incremental exists in the enum so that adding WAL shipping in Stage 31 is not a schema
        // change. Nothing implements it, and accepting it would produce a snapshot that claims to be
        // something it is not.
        RuleFor(command => command.Kind)
            .Equal(BackupKind.Full)
            .WithMessage("Only full snapshots are taken today. Incremental is reserved for Stage 31.");
    }
}

/// <summary>Runs the snapshot through the backup service.</summary>
/// <param name="backups">The orchestration.</param>
public sealed class CreateBackupSnapshotCommandHandler(IBackupService backups)
    : ICommandHandler<CreateBackupSnapshotCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(
        CreateBackupSnapshotCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        BackupSnapshot snapshot = await backups
            .CreateSnapshotAsync(command.Kind, cancellationToken)
            .ConfigureAwait(false);

        return snapshot.Id;
    }
}

/// <summary>
/// Reads a snapshot back from the vault and confirms its checksum.
/// </summary>
/// <remarks>
/// The difference between a backup and a hope. A snapshot that has never been read back proves only
/// that a write did not throw, and every backup horror story is the same story: the object was there,
/// and it was not what anybody thought it was.
/// </remarks>
/// <param name="SnapshotId">The snapshot to verify.</param>
[CommandSideEffect(SideEffect.Write, Exemption = ReadOnlyExemption.Backup)]
public sealed record VerifyBackupSnapshotCommand(Guid SnapshotId) : ICommand<Unit>;

/// <summary>Rejects a verify command before it reaches the handler.</summary>
public sealed class VerifyBackupSnapshotCommandValidator : AbstractValidator<VerifyBackupSnapshotCommand>
{
    /// <summary>Builds the rules.</summary>
    public VerifyBackupSnapshotCommandValidator() => RuleFor(command => command.SnapshotId).NotEmpty();
}

/// <summary>Verifies the snapshot and stamps the ledger row.</summary>
/// <param name="backups">The orchestration.</param>
public sealed class VerifyBackupSnapshotCommandHandler(IBackupService backups)
    : ICommandHandler<VerifyBackupSnapshotCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(
        VerifyBackupSnapshotCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        await backups.VerifySnapshotAsync(command.SnapshotId, cancellationToken).ConfigureAwait(false);

        return Unit.Value;
    }
}
