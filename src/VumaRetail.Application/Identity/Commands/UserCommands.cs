using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Identity;

namespace VumaRetail.Application.Identity.Commands;

/// <summary>Creates a back-office user with a password.</summary>
/// <param name="UserName">The sign-in name, unique within the tenant.</param>
/// <param name="DisplayName">The name shown in the UI.</param>
/// <param name="Password">The initial password.</param>
/// <param name="Email">The user's email address, where they have one.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record CreateUserCommand(string UserName, string DisplayName, string Password, string? Email = null)
    : ICommand<Guid>;

/// <summary>Rejects a create-user command before it reaches the handler.</summary>
public sealed class CreateUserCommandValidator : AbstractValidator<CreateUserCommand>
{
    /// <summary>Builds the rules.</summary>
    public CreateUserCommandValidator()
    {
        RuleFor(command => command.UserName).NotEmpty().MaximumLength(128);
        RuleFor(command => command.DisplayName).NotEmpty().MaximumLength(256);
        RuleFor(command => command.Password).NotEmpty().MinimumLength(CredentialPolicy.MinimumPasswordLength);
        RuleFor(command => command.Email).MaximumLength(256);
    }
}

/// <summary>Creates a user and sets their password.</summary>
/// <param name="users">User lookup and insertion.</param>
/// <param name="hasher">Hashes the password.</param>
/// <param name="tenant">The tenant the user belongs to.</param>
public sealed class CreateUserCommandHandler(
    IUserRepository users,
    IPasswordHasher hasher,
    ITenantContext tenant) : ICommandHandler<CreateUserCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Password.Length < CredentialPolicy.MinimumPasswordLength)
        {
            throw WeakCredentialException.Password();
        }

        if (await users.FindByUserNameAsync(command.UserName, cancellationToken).ConfigureAwait(false) is not null)
        {
            throw IdentityConflictException.UserName(command.UserName);
        }

        User user = User.Create(tenant.TenantId, command.UserName, command.DisplayName);
        user.SetEmail(command.Email);
        user.SetPasswordHash(hasher.Hash(command.Password));

        users.Add(user);

        return user.Id;
    }
}

/// <summary>Sets or replaces a user's POS PIN.</summary>
/// <param name="UserId">The operator.</param>
/// <param name="Pin">The 4–8 digit PIN.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record SetUserPinCommand(Guid UserId, string Pin) : ICommand;

/// <summary>Rejects a malformed PIN before it reaches the handler.</summary>
public sealed class SetUserPinCommandValidator : AbstractValidator<SetUserPinCommand>
{
    /// <summary>Builds the rules.</summary>
    public SetUserPinCommandValidator()
    {
        RuleFor(command => command.UserId).NotEmpty();
        RuleFor(command => command.Pin)
            .NotEmpty()
            .Length(CredentialPolicy.MinimumPinLength, CredentialPolicy.MaximumPinLength)
            .Must(pin => pin.All(char.IsAsciiDigit))
            .WithMessage("A PIN is digits only.");
    }
}

/// <summary>
/// Sets a PIN, refusing one that another live operator already uses.
/// </summary>
/// <remarks>
/// A PIN sign-in resolves the operator from the PIN alone, so two operators sharing one would make
/// the till attribute a sale to whichever row came back first — a bug that shows up in a shift report
/// weeks later as a cashier who cannot account for a transaction. The collision check has to verify
/// the candidate against every stored hash, because hashes are salted and cannot be compared as
/// strings. Retail staff counts make that a few hundred verifications at worst, on a path that runs
/// when a manager sets a PIN rather than on every sale.
/// </remarks>
/// <param name="users">User lookup.</param>
/// <param name="hasher">Hashes and verifies the PIN.</param>
public sealed class SetUserPinCommandHandler(
    IUserRepository users,
    IPasswordHasher hasher) : ICommandHandler<SetUserPinCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(SetUserPinCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!IsWellFormed(command.Pin))
        {
            throw WeakCredentialException.Pin();
        }

        User user = await users.FindAsync(command.UserId, cancellationToken).ConfigureAwait(false)
            ?? throw new IdentityNotFoundException("user", command.UserId);

        IReadOnlyList<User> holders = await users.ListPinHoldersAsync(cancellationToken).ConfigureAwait(false);

        foreach (User holder in holders)
        {
            if (holder.Id != user.Id && holder.PinHash is not null && hasher.Verify(holder.PinHash, command.Pin))
            {
                throw IdentityConflictException.Pin();
            }
        }

        user.SetPinHash(hasher.Hash(command.Pin));

        return Unit.Value;
    }

    private static bool IsWellFormed(string pin)
        => pin.Length >= CredentialPolicy.MinimumPinLength
            && pin.Length <= CredentialPolicy.MaximumPinLength
            && pin.All(char.IsAsciiDigit);
}

/// <summary>Changes a user's password, ending every session they have open.</summary>
/// <param name="UserId">The user.</param>
/// <param name="NewPassword">The new password.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record ChangePasswordCommand(Guid UserId, string NewPassword) : ICommand;

/// <summary>Rejects a password that does not meet the policy.</summary>
public sealed class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    /// <summary>Builds the rules.</summary>
    public ChangePasswordCommandValidator()
    {
        RuleFor(command => command.UserId).NotEmpty();
        RuleFor(command => command.NewPassword).NotEmpty().MinimumLength(CredentialPolicy.MinimumPasswordLength);
    }
}

/// <summary>Changes a password and revokes every refresh token issued under the old one.</summary>
/// <param name="users">User lookup.</param>
/// <param name="tokens">Refresh token store.</param>
/// <param name="hasher">Hashes the password.</param>
/// <param name="clock">The only source of time.</param>
public sealed class ChangePasswordCommandHandler(
    IUserRepository users,
    IRefreshTokenRepository tokens,
    IPasswordHasher hasher,
    IClock clock) : ICommandHandler<ChangePasswordCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(ChangePasswordCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.NewPassword.Length < CredentialPolicy.MinimumPasswordLength)
        {
            throw WeakCredentialException.Password();
        }

        User user = await users.FindAsync(command.UserId, cancellationToken).ConfigureAwait(false)
            ?? throw new IdentityNotFoundException("user", command.UserId);

        // SetPasswordHash rotates the security stamp, which retires every access token already out
        // there. The refresh tokens are revoked explicitly as well so the audit trail records why
        // they died rather than leaving rows that simply stopped working.
        user.SetPasswordHash(hasher.Hash(command.NewPassword));

        IReadOnlyList<RefreshToken> live = await tokens.ListLiveAsync(user.Id, cancellationToken).ConfigureAwait(false);

        foreach (RefreshToken token in live)
        {
            token.Revoke(RefreshTokenRevocation.CredentialChanged, clock.UtcNow);
        }


        return Unit.Value;
    }
}
