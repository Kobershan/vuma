using FluentValidation;
using ZenithRetail.Application.Abstractions;
using ZenithRetail.Domain.Identity;

namespace ZenithRetail.Application.Identity.Commands;

/// <summary>What an enrolment returns: the terminal, and the code the device gets exactly once.</summary>
/// <param name="TerminalId">The enrolled terminal.</param>
/// <param name="EnrolmentCode">The one-time code, in plaintext. Shown once and never stored.</param>
/// <param name="ExpiresAt">When the code stops being accepted.</param>
public sealed record TerminalEnrolment(Guid TerminalId, string EnrolmentCode, DateTimeOffset ExpiresAt);

/// <summary>Enrols a terminal, producing a one-time activation code.</summary>
/// <param name="StoreId">The store the terminal trades in.</param>
/// <param name="Code">The short terminal code, unique within the store.</param>
/// <param name="Name">The terminal's display name.</param>
/// <param name="CodeLifetime">How long the activation code is good for. Defaults to an hour.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record EnrolTerminalCommand(Guid StoreId, string Code, string Name, TimeSpan? CodeLifetime = null)
    : ICommand<TerminalEnrolment>;

/// <summary>Rejects an enrol-terminal command before it reaches the handler.</summary>
public sealed class EnrolTerminalCommandValidator : AbstractValidator<EnrolTerminalCommand>
{
    /// <summary>Builds the rules.</summary>
    public EnrolTerminalCommandValidator()
    {
        RuleFor(command => command.StoreId).NotEmpty();
        RuleFor(command => command.Code).NotEmpty().MaximumLength(16);
        RuleFor(command => command.Name).NotEmpty().MaximumLength(128);
        RuleFor(command => command.CodeLifetime)
            .Must(lifetime => lifetime is null or { TotalMinutes: > 0 and <= 1440 })
            .WithMessage("An activation code lives between a minute and a day.");
    }
}

/// <summary>Enrols a terminal and hands back its one-time code.</summary>
/// <param name="terminals">Terminal lookup and insertion.</param>
/// <param name="hasher">Hashes the enrolment code.</param>
/// <param name="tokens">Generates the code.</param>
/// <param name="tenant">The tenant the terminal belongs to.</param>
/// <param name="clock">The only source of time.</param>
/// <param name="unitOfWork">Commits the write.</param>
public sealed class EnrolTerminalCommandHandler(
    ITerminalRepository terminals,
    IPasswordHasher hasher,
    ITokenHasher tokens,
    ITenantContext tenant,
    IClock clock,
    IUnitOfWork unitOfWork) : ICommandHandler<EnrolTerminalCommand, TerminalEnrolment>
{
    /// <summary>How long an activation code lives if the caller does not say.</summary>
    /// <remarks>
    /// An hour is long enough for a technician to walk to the till and short enough that a code left
    /// on a note by the printer is worthless by the time anyone finds it.
    /// </remarks>
    public static readonly TimeSpan DefaultCodeLifetime = TimeSpan.FromHours(1);

    /// <inheritdoc />
    public async Task<TerminalEnrolment> HandleAsync(
        EnrolTerminalCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        string code = command.Code.Trim().ToUpperInvariant();

        if (await terminals.FindByCodeAsync(command.StoreId, code, cancellationToken).ConfigureAwait(false) is not null)
        {
            throw IdentityConflictException.TerminalCode(code);
        }

        string plaintext = tokens.Generate();
        DateTimeOffset expiresAt = clock.UtcNow + (command.CodeLifetime ?? DefaultCodeLifetime);

        Terminal terminal = Terminal.Enrol(
            tenant.TenantId,
            command.StoreId,
            code,
            command.Name,
            hasher.Hash(plaintext),
            expiresAt);

        terminals.Add(terminal);
        await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new TerminalEnrolment(terminal.Id, plaintext, expiresAt);
    }
}

/// <summary>Activates an enrolled terminal by presenting its code and certificate.</summary>
/// <param name="StoreId">The store the terminal was enrolled in.</param>
/// <param name="EnrolmentCode">The one-time code.</param>
/// <param name="CertificateThumbprint">SHA-256 thumbprint of the device's client certificate.</param>
/// <param name="DeviceFingerprint">The device fingerprint to record.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record ActivateTerminalCommand(
    Guid StoreId,
    string EnrolmentCode,
    string CertificateThumbprint,
    string DeviceFingerprint) : ICommand<Guid>;

/// <summary>Rejects an activate-terminal command before it reaches the handler.</summary>
public sealed class ActivateTerminalCommandValidator : AbstractValidator<ActivateTerminalCommand>
{
    /// <summary>Builds the rules.</summary>
    public ActivateTerminalCommandValidator()
    {
        RuleFor(command => command.StoreId).NotEmpty();
        RuleFor(command => command.EnrolmentCode).NotEmpty();
        // A SHA-256 thumbprint is 64 hex characters. Anything else is not one, and a short "thumbprint"
        // is how a lookup ends up matching more than one certificate.
        RuleFor(command => command.CertificateThumbprint)
            .NotEmpty()
            .Length(64)
            .Must(value => value.All(char.IsAsciiHexDigit))
            .WithMessage("A certificate thumbprint is 64 hexadecimal characters (SHA-256).");
        RuleFor(command => command.DeviceFingerprint).NotEmpty().MaximumLength(256);
    }
}

/// <summary>Matches the code against the store's pending terminals and pins the certificate.</summary>
/// <param name="terminals">Terminal lookup.</param>
/// <param name="hasher">Verifies the enrolment code.</param>
/// <param name="clock">The only source of time.</param>
/// <param name="unitOfWork">Commits the write.</param>
public sealed class ActivateTerminalCommandHandler(
    ITerminalRepository terminals,
    IPasswordHasher hasher,
    IClock clock,
    IUnitOfWork unitOfWork) : ICommandHandler<ActivateTerminalCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(ActivateTerminalCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        IReadOnlyList<Terminal> pending = await terminals
            .ListPendingAsync(command.StoreId, cancellationToken)
            .ConfigureAwait(false);

        Terminal? matched = pending.FirstOrDefault(terminal =>
            terminal.EnrolmentCodeHash is not null
            && hasher.Verify(terminal.EnrolmentCodeHash, command.EnrolmentCode));

        if (matched is null)
        {
            // Same answer for "no such code", "already used" and "expired". A device that can tell
            // them apart can enumerate which codes were ever issued to a store.
            throw new TerminalActivationException("That activation code is not valid for this store.");
        }

        matched.Activate(command.CertificateThumbprint, command.DeviceFingerprint, clock.UtcNow);
        await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        return matched.Id;
    }
}

/// <summary>Revokes a terminal, ending its ability to authenticate.</summary>
/// <param name="TerminalId">The terminal.</param>
/// <param name="Reason">Why, for the audit trail.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record RevokeTerminalCommand(Guid TerminalId, string Reason) : ICommand;

/// <summary>Rejects a revoke-terminal command before it reaches the handler.</summary>
public sealed class RevokeTerminalCommandValidator : AbstractValidator<RevokeTerminalCommand>
{
    /// <summary>Builds the rules.</summary>
    public RevokeTerminalCommandValidator()
    {
        RuleFor(command => command.TerminalId).NotEmpty();
        RuleFor(command => command.Reason).NotEmpty().MaximumLength(256);
    }
}

/// <summary>Revokes a terminal.</summary>
/// <param name="terminals">Terminal lookup.</param>
/// <param name="unitOfWork">Commits the write.</param>
public sealed class RevokeTerminalCommandHandler(
    ITerminalRepository terminals,
    IUnitOfWork unitOfWork) : ICommandHandler<RevokeTerminalCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(RevokeTerminalCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Terminal terminal = await terminals.FindAsync(command.TerminalId, cancellationToken).ConfigureAwait(false)
            ?? throw new IdentityNotFoundException("terminal", command.TerminalId);

        terminal.Revoke(command.Reason);
        await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        return Unit.Value;
    }
}
