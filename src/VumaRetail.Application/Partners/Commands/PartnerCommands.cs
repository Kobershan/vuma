using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.Partners;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.Partners.Commands;

/// <summary>Creates a new partner — a supplier, a customer, or both.</summary>
/// <param name="Code">The partner's unique code within the tenant, upper-cased at creation.</param>
/// <param name="Name">The partner's trading name.</param>
/// <param name="Type">Customer, supplier, or both. Never neither.</param>
/// <param name="Address">The partner's address, or <c>null</c>.</param>
/// <param name="Email">An email address, or <c>null</c>.</param>
/// <param name="Phone">A phone number, or <c>null</c>.</param>
/// <param name="TaxNumber">A tax registration number, or <c>null</c>.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record CreatePartnerCommand(
    string Code,
    string Name,
    PartnerType Type,
    Address? Address = null,
    string? Email = null,
    string? Phone = null,
    string? TaxNumber = null) : ICommand<Guid>;

/// <summary>Rejects a malformed create-partner command before it reaches the handler.</summary>
public sealed class CreatePartnerCommandValidator : AbstractValidator<CreatePartnerCommand>
{
    /// <summary>Builds the rules.</summary>
    public CreatePartnerCommandValidator()
    {
        RuleFor(command => command.Code).NotEmpty().MaximumLength(32);
        RuleFor(command => command.Name).NotEmpty().MaximumLength(256);
        RuleFor(command => command.Type).NotEqual(PartnerType.None);
        RuleFor(command => command.Email).MaximumLength(256).EmailAddress().When(command => command.Email is not null);
        RuleFor(command => command.Phone).MaximumLength(32);
        RuleFor(command => command.TaxNumber).MaximumLength(32);
    }
}

/// <summary>Creates a partner, refusing a code already used in this tenant.</summary>
/// <param name="partners">Partner lookup and insertion.</param>
/// <param name="tenant">The tenant the partner belongs to.</param>
public sealed class CreatePartnerCommandHandler(IPartnerRepository partners, ITenantContext tenant)
    : ICommandHandler<CreatePartnerCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Guid> HandleAsync(CreatePartnerCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await partners.FindByCodeAsync(command.Code, cancellationToken).ConfigureAwait(false) is not null)
        {
            throw PartnerConflictException.PartnerCode(command.Code);
        }

        Partner partner = Partner.Create(
            tenant.TenantId,
            command.Code,
            command.Name,
            command.Type,
            command.Address,
            command.Email,
            command.Phone,
            command.TaxNumber);

        partners.Add(partner);

        return partner.Id;
    }
}

/// <summary>Updates a partner's descriptive fields.</summary>
/// <param name="PartnerId">The partner.</param>
/// <param name="Name">The new trading name.</param>
/// <param name="Address">The new address, or <c>null</c> to clear it.</param>
/// <param name="Email">The new email, or <c>null</c> to clear it.</param>
/// <param name="Phone">The new phone number, or <c>null</c> to clear it.</param>
/// <param name="TaxNumber">The new tax number, or <c>null</c> to clear it.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record UpdatePartnerDetailsCommand(
    Guid PartnerId,
    string Name,
    Address? Address = null,
    string? Email = null,
    string? Phone = null,
    string? TaxNumber = null) : ICommand;

/// <summary>Rejects a malformed update-partner command before it reaches the handler.</summary>
public sealed class UpdatePartnerDetailsCommandValidator : AbstractValidator<UpdatePartnerDetailsCommand>
{
    /// <summary>Builds the rules.</summary>
    public UpdatePartnerDetailsCommandValidator()
    {
        RuleFor(command => command.PartnerId).NotEmpty();
        RuleFor(command => command.Name).NotEmpty().MaximumLength(256);
        RuleFor(command => command.Email).MaximumLength(256).EmailAddress().When(command => command.Email is not null);
        RuleFor(command => command.Phone).MaximumLength(32);
        RuleFor(command => command.TaxNumber).MaximumLength(32);
    }
}

/// <summary>Updates a partner's details.</summary>
/// <param name="partners">Partner lookup.</param>
public sealed class UpdatePartnerDetailsCommandHandler(IPartnerRepository partners)
    : ICommandHandler<UpdatePartnerDetailsCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(UpdatePartnerDetailsCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Partner partner = await partners.FindAsync(command.PartnerId, cancellationToken).ConfigureAwait(false)
            ?? throw new PartnerNotFoundException("partner", command.PartnerId);

        partner.SetDetails(command.Name, command.Address, command.Email, command.Phone, command.TaxNumber);

        return Unit.Value;
    }
}

/// <summary>Retires a partner from new trading. History is retained; nothing is deleted (§7 rule 8).</summary>
/// <param name="PartnerId">The partner.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record DeactivatePartnerCommand(Guid PartnerId) : ICommand;

/// <summary>Rejects a malformed deactivate-partner command before it reaches the handler.</summary>
public sealed class DeactivatePartnerCommandValidator : AbstractValidator<DeactivatePartnerCommand>
{
    /// <summary>Builds the rules.</summary>
    public DeactivatePartnerCommandValidator() => RuleFor(command => command.PartnerId).NotEmpty();
}

/// <summary>Deactivates a partner.</summary>
/// <param name="partners">Partner lookup.</param>
public sealed class DeactivatePartnerCommandHandler(IPartnerRepository partners)
    : ICommandHandler<DeactivatePartnerCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> HandleAsync(DeactivatePartnerCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Partner partner = await partners.FindAsync(command.PartnerId, cancellationToken).ConfigureAwait(false)
            ?? throw new PartnerNotFoundException("partner", command.PartnerId);

        partner.Deactivate();

        return Unit.Value;
    }
}
