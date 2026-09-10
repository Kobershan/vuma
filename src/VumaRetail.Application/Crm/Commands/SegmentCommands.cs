using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Crm;

namespace VumaRetail.Application.Crm.Commands;

/// <summary>Creates a segment.</summary>
/// <param name="CompanyId">The owning company.</param>
/// <param name="Name">Segment name.</param>
/// <param name="Kind">Static or dynamic.</param>
/// <param name="Description">What it is for.</param>
/// <param name="QueryExpression">Filter criteria for dynamic segments.</param>
/// <param name="StoreId">The owning store, if any.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record CreateSegmentCommand(
    Guid CompanyId,
    string Name,
    SegmentKind Kind,
    string? Description = null,
    string? QueryExpression = null,
    Guid? StoreId = null) : ICommand<Guid>;

/// <summary>Rejects a malformed segment.</summary>
public sealed class CreateSegmentCommandValidator : AbstractValidator<CreateSegmentCommand>
{
    /// <summary>Builds the rules.</summary>
    public CreateSegmentCommandValidator()
    {
        RuleFor(command => command.CompanyId).NotEmpty();
        RuleFor(command => command.Name).NotEmpty().MaximumLength(200);
        RuleFor(command => command.Description).MaximumLength(1000);
        RuleFor(command => command.QueryExpression).MaximumLength(4000);
    }
}

/// <summary>Creates a segment.</summary>
/// <param name="segments">Segment persistence.</param>
/// <param name="tenant">The ambient tenant.</param>
public sealed class CreateSegmentCommandHandler(ISegmentRepository segments, ITenantContext tenant)
    : ICommandHandler<CreateSegmentCommand, Guid>
{
    /// <inheritdoc />
    public Task<Guid> HandleAsync(CreateSegmentCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var segment = new Segment(
            tenant.TenantId,
            command.CompanyId,
            command.Name,
            command.Kind,
            command.Description,
            command.QueryExpression,
            command.StoreId);

        segments.Add(segment);
        return Task.FromResult(segment.Id);
    }
}

/// <summary>Adds a member to a static segment.</summary>
/// <param name="SegmentId">The segment.</param>
/// <param name="MemberType">What kind of member.</param>
/// <param name="MemberId">The member.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record AddStaticMemberCommand(Guid SegmentId, MemberType MemberType, Guid MemberId)
    : ICommand;

/// <summary>Rejects a malformed membership.</summary>
public sealed class AddStaticMemberCommandValidator : AbstractValidator<AddStaticMemberCommand>
{
    /// <summary>Builds the rules.</summary>
    public AddStaticMemberCommandValidator()
    {
        RuleFor(command => command.SegmentId).NotEmpty();
        RuleFor(command => command.MemberId).NotEmpty();
    }
}

/// <summary>Adds a static member. Refuses dynamic segments and duplicates.</summary>
/// <param name="segments">Segment persistence.</param>
/// <param name="members">Membership persistence.</param>
/// <param name="principal">Who is acting, for the membership audit.</param>
/// <param name="clock">The only source of time.</param>
public sealed class AddStaticMemberCommandHandler(
    ISegmentRepository segments,
    ISegmentMemberRepository members,
    IPrincipalAccessor principal,
    IClock clock) : ICommandHandler<AddStaticMemberCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(AddStaticMemberCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Segment segment = await segments
            .FindAsync(command.SegmentId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new SegmentNotFoundException();

        if (!segment.IsActive)
        {
            throw new SegmentNotFoundException();
        }

        segment.RefuseMemberWrite();

        if (await members
            .ExistsAsync(command.SegmentId, command.MemberType, command.MemberId, cancellationToken)
            .ConfigureAwait(false))
        {
            return Unit.Value;
        }

        members.Add(new SegmentMember(
            segment.TenantId,
            segment.CompanyId!.Value,
            segment,
            command.MemberType,
            command.MemberId,
            principal.Principal,
            clock.UtcNow,
            segment.StoreId));

        return Unit.Value;
    }
}

/// <summary>Deactivates a segment. It matches nobody until reactivated.</summary>
/// <param name="SegmentId">The segment.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record DeactivateSegmentCommand(Guid SegmentId) : ICommand;

/// <summary>Rejects a malformed deactivation.</summary>
public sealed class DeactivateSegmentCommandValidator : AbstractValidator<DeactivateSegmentCommand>
{
    /// <summary>Builds the rules.</summary>
    public DeactivateSegmentCommandValidator() => RuleFor(command => command.SegmentId).NotEmpty();
}

/// <summary>Deactivates a segment.</summary>
/// <param name="segments">Segment persistence.</param>
public sealed class DeactivateSegmentCommandHandler(ISegmentRepository segments)
    : ICommandHandler<DeactivateSegmentCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(DeactivateSegmentCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Segment segment = await segments
            .FindAsync(command.SegmentId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new SegmentNotFoundException();

        segment.Deactivate();
        return Unit.Value;
    }
}
