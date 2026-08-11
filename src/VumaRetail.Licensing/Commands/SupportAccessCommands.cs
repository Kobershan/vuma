using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Domain.Licensing;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Licensing.Commands;

/// <summary>
/// A tenant admin lets Vuma support look at this business's data, for a bounded time.
/// </summary>
/// <remarks>
/// <para>
/// An ordinary write, and deliberately so: a read-only tenant cannot <em>grant</em> new access. That
/// is the safe direction — if billing has broken down, the default should be that fewer people can see
/// the customer's data, not more.
/// </para>
/// <para>
/// While a grant is live the tenant's own UI shows a banner and every vendor action is written to the
/// tenant's audit trail as well as the vendor's (<c>LICENSING.md</c> §9). There is no route to tenant
/// business data without one, and it is built that way now because consent cannot be retrofitted.
/// </para>
/// </remarks>
/// <param name="GrantId">The request being answered.</param>
/// <param name="Duration">How long access lasts. Defaults to the configured four hours.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record ApproveSupportAccessCommand(Guid GrantId, TimeSpan? Duration = null) : ICommand<Unit>;

/// <summary>Rejects an approval before it reaches the handler.</summary>
public sealed class ApproveSupportAccessCommandValidator : AbstractValidator<ApproveSupportAccessCommand>
{
    /// <summary>Builds the rules.</summary>
    public ApproveSupportAccessCommandValidator()
    {
        RuleFor(command => command.GrantId).NotEmpty();

        // A grant nobody remembers giving is the one that matters. A day is the ceiling; the default
        // is four hours, which is a support session rather than a standing arrangement.
        RuleFor(command => command.Duration)
            .Must(duration => duration is null or { TotalMinutes: >= 15 and <= 1440 })
            .WithMessage("Support access lasts between fifteen minutes and a day.");
    }
}

/// <summary>Approves a grant.</summary>
/// <param name="grants">Vendor support-access grants.</param>
/// <param name="principal">Who is approving — recorded on the grant and in the audit trail.</param>
/// <param name="options">The default duration.</param>
/// <param name="clock">The only source of time.</param>
public sealed class ApproveSupportAccessCommandHandler(
    ISupportGrantRepository grants,
    IPrincipalAccessor principal,
    LicensingOptions options,
    IClock clock) : ICommandHandler<ApproveSupportAccessCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(
        ApproveSupportAccessCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        SupportGrant grant = await grants.FindAsync(command.GrantId, cancellationToken).ConfigureAwait(false)
            ?? throw new SupportGrantNotFoundException(command.GrantId);

        grant.Approve(
            principal.Principal,
            clock.UtcNow,
            command.Duration ?? options.DefaultSupportGrantDuration);

        return Unit.Value;
    }
}

/// <summary>A tenant admin says no to a support request.</summary>
/// <param name="GrantId">The request being answered.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record DeclineSupportAccessCommand(Guid GrantId) : ICommand<Unit>;

/// <summary>Rejects a decline before it reaches the handler.</summary>
public sealed class DeclineSupportAccessCommandValidator : AbstractValidator<DeclineSupportAccessCommand>
{
    /// <summary>Builds the rules.</summary>
    public DeclineSupportAccessCommandValidator() => RuleFor(command => command.GrantId).NotEmpty();
}

/// <summary>Declines a grant.</summary>
/// <param name="grants">Vendor support-access grants.</param>
/// <param name="principal">Who is declining.</param>
/// <param name="clock">The only source of time.</param>
public sealed class DeclineSupportAccessCommandHandler(
    ISupportGrantRepository grants,
    IPrincipalAccessor principal,
    IClock clock) : ICommandHandler<DeclineSupportAccessCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(
        DeclineSupportAccessCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        SupportGrant grant = await grants.FindAsync(command.GrantId, cancellationToken).ConfigureAwait(false)
            ?? throw new SupportGrantNotFoundException(command.GrantId);

        grant.Decline(principal.Principal, clock.UtcNow);

        return Unit.Value;
    }
}

/// <summary>
/// A tenant ends a vendor's access early.
/// </summary>
/// <remarks>
/// <see cref="ReadOnlyExemption.Payment"/>, which looks odd on a command that has nothing to do with
/// paying and is the right answer anyway: <b>withdrawal of consent must not be held hostage by
/// billing</b>. A tenant who wants the vendor out of their system must be able to say so whatever they
/// owe, and a carve-out that covered the payment button but not this one would mean the vendor's
/// commercial lever had bought them access the customer no longer wanted them to have. Approving a
/// <em>new</em> grant stays an ordinary write, so the exemption only ever moves in the direction of
/// less access (ADR-052).
/// </remarks>
/// <param name="GrantId">The grant being ended.</param>
[CommandSideEffect(SideEffect.Write, Exemption = ReadOnlyExemption.Payment)]
public sealed record RevokeSupportAccessCommand(Guid GrantId) : ICommand<Unit>;

/// <summary>Rejects a revocation before it reaches the handler.</summary>
public sealed class RevokeSupportAccessCommandValidator : AbstractValidator<RevokeSupportAccessCommand>
{
    /// <summary>Builds the rules.</summary>
    public RevokeSupportAccessCommandValidator() => RuleFor(command => command.GrantId).NotEmpty();
}

/// <summary>Ends a grant.</summary>
/// <param name="grants">Vendor support-access grants.</param>
/// <param name="principal">Who is revoking.</param>
/// <param name="clock">The only source of time.</param>
public sealed class RevokeSupportAccessCommandHandler(
    ISupportGrantRepository grants,
    IPrincipalAccessor principal,
    IClock clock) : ICommandHandler<RevokeSupportAccessCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(
        RevokeSupportAccessCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        SupportGrant grant = await grants.FindAsync(command.GrantId, cancellationToken).ConfigureAwait(false)
            ?? throw new SupportGrantNotFoundException(command.GrantId);

        grant.Revoke(principal.Principal, clock.UtcNow);

        return Unit.Value;
    }
}

/// <summary>No such support grant, or not this tenant's.</summary>
/// <param name="grantId">The grant that was asked for.</param>
public sealed class SupportGrantNotFoundException(Guid grantId)
    : DomainException(
        "LICENCE_SUPPORT_GRANT_NOT_FOUND",
        $"There is no support access request {grantId}.",
        DomainProblemKind.NotFound);
