#pragma warning disable CS1591
using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.CustomerAccounts;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Inventory;
using VumaRetail.Domain.CustomerAccounts;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.CustomerAccounts.Commands.Stokvels;

public sealed record HamperLineInput(
    Guid? ItemId,
    Guid? ItemVariantId,
    decimal Quantity,
    string Uom,
    Guid? SubstitutionItemId = null,
    Guid? SubstitutionItemVariantId = null);

[CommandSideEffect(SideEffect.Write)]
public sealed record CreateStokvelGroupCommand(
    string Name,
    StokvelType Type,
    string Constitution,
    DateOnly CycleStart,
    DateOnly CycleEnd,
    Guid StoreId,
    Guid? CompanyId = null) : ICommand<Guid>;

public sealed class CreateStokvelGroupCommandValidator : AbstractValidator<CreateStokvelGroupCommand>
{
    public CreateStokvelGroupCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(128);
        RuleFor(c => c.Constitution).NotEmpty().MaximumLength(2000);
        RuleFor(c => c.CycleEnd).GreaterThanOrEqualTo(c => c.CycleStart);
        RuleFor(c => c.StoreId).NotEmpty();
    }
}

public sealed class CreateStokvelGroupCommandHandler(
    IStokvelGroupRepository groups,
    IDocumentNumberSequence numbers,
    ITenantContext tenant,
    ICompanyContext company)
    : ICommandHandler<CreateStokvelGroupCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateStokvelGroupCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        string number = await numbers.NextAsync("STK", cancellationToken).ConfigureAwait(false);
        var group = StokvelGroup.Create(
            tenant.TenantId, tenant.StoreId, number, command.Name, command.Type,
            command.Constitution, command.CycleStart, command.CycleEnd, command.StoreId,
            StokvelCompanyScope.ResolveForCreate(company, command.CompanyId));
        groups.AddGroup(group);
        return group.Id;
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record AddStokvelMemberCommand(
    Guid GroupId,
    Guid PartnerId,
    MemberRole Role,
    decimal ObligationAmount,
    string ObligationCurrency) : ICommand<Guid>;

public sealed class AddStokvelMemberCommandValidator : AbstractValidator<AddStokvelMemberCommand>
{
    public AddStokvelMemberCommandValidator()
    {
        RuleFor(c => c.GroupId).NotEmpty();
        RuleFor(c => c.PartnerId).NotEmpty();
        RuleFor(c => c.ObligationAmount).GreaterThanOrEqualTo(0m);
        RuleFor(c => c.ObligationCurrency).NotEmpty().Length(3);
    }
}

public sealed class AddStokvelMemberCommandHandler(
    IStokvelGroupRepository groups,
    ITenantContext tenant,
    IClock clock)
    : ICommandHandler<AddStokvelMemberCommand, Guid>
{
    public async Task<Guid> HandleAsync(AddStokvelMemberCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var group = await groups.FindAsync(command.GroupId, cancellationToken).ConfigureAwait(false)
            ?? throw StokvelExceptions.GroupNotFound(command.GroupId);
        if (group.Status is StokvelStatus.PayingOut or StokvelStatus.Closed)
        {
            throw StokvelExceptions.UnexpectedStatus(group.Status);
        }

        IReadOnlyList<StokvelMember> existing =
            await groups.ListMembersAsync(command.GroupId, cancellationToken).ConfigureAwait(false);
        if (existing.Any(m => m.PartnerId == command.PartnerId && m.LeftAt is null))
        {
            throw new StokvelExceptions("STOKVEL_ALREADY_MEMBER", "This partner is already an active member of the group.");
        }

        var member = StokvelMember.Join(
            tenant.TenantId, tenant.StoreId, group.Id, command.PartnerId, command.Role,
            new Money(command.ObligationAmount, command.ObligationCurrency), clock.UtcNow);
        groups.AddMember(member);
        return member.Id;
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record RecordContributionCommand(
    Guid GroupId,
    Guid MemberId,
    decimal Amount,
    string Currency,
    string Channel,
    string ReceiptReference,
    bool TakenOffline = false) : ICommand<Guid>;

public sealed class RecordContributionCommandValidator : AbstractValidator<RecordContributionCommand>
{
    public RecordContributionCommandValidator()
    {
        RuleFor(c => c.GroupId).NotEmpty();
        RuleFor(c => c.MemberId).NotEmpty();
        RuleFor(c => c.Amount).GreaterThan(0m);
        RuleFor(c => c.Currency).NotEmpty().Length(3);
        RuleFor(c => c.Channel).NotEmpty().MaximumLength(64);
        RuleFor(c => c.ReceiptReference).NotEmpty().MaximumLength(64);
    }
}

public sealed class RecordContributionCommandHandler(
    IStokvelGroupRepository groups,
    IStokvelContributionRepository ledger,
    IFinancialEventPoster events,
    ITenantContext tenant,
    IClock clock)
    : ICommandHandler<RecordContributionCommand, Guid>
{
    public async Task<Guid> HandleAsync(RecordContributionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var group = await groups.FindAsync(command.GroupId, cancellationToken).ConfigureAwait(false)
            ?? throw StokvelExceptions.GroupNotFound(command.GroupId);
        var member = await groups.FindMemberAsync(command.MemberId, cancellationToken).ConfigureAwait(false)
            ?? throw StokvelExceptions.MemberNotFound(command.MemberId);
        if (member.GroupId != group.Id)
        {
            throw StokvelExceptions.MemberNotFound(command.MemberId);
        }

        if (member.LeftAt is not null)
        {
            throw StokvelExceptions.MemberLeft();
        }

        // Offline replay idempotency at the handler layer; the unique index underneath is the
        // guarantee for a genuine race (same precedent as the sync receiver's inbox key).
        var existing = await ledger.FindByReceiptAsync(
            command.MemberId, command.ReceiptReference.Trim(), cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing.Id;
        }

        DateTimeOffset now = clock.UtcNow;
        var row = StokvelContribution.Record(
            tenant.TenantId, tenant.StoreId, group.Id, member.Id,
            new Money(command.Amount, command.Currency), command.ReceiptReference,
            now, command.Channel, command.TakenOffline);
        ledger.AddContribution(row);

        await events.PostAsync(
            new Events.StokvelContributionReceivedEvent(
                tenant.TenantId, tenant.StoreId, now, group.GroupNumber,
                new Dictionary<string, Money> { ["Principal"] = row.Amount }),
            cancellationToken).ConfigureAwait(false);

        return row.Id;
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record AllocateBenefitsCommand(
    Guid GroupId,
    decimal BonusPoolAmount,
    string BonusPoolCurrency,
    DateTimeOffset? AsAt = null) : ICommand<IReadOnlyList<Guid>>;

public sealed class AllocateBenefitsCommandValidator : AbstractValidator<AllocateBenefitsCommand>
{
    public AllocateBenefitsCommandValidator()
    {
        RuleFor(c => c.GroupId).NotEmpty();
        RuleFor(c => c.BonusPoolAmount).GreaterThanOrEqualTo(0m);
        RuleFor(c => c.BonusPoolCurrency).NotEmpty().Length(3);
    }
}

public sealed class AllocateBenefitsCommandHandler(
    IStokvelGroupRepository groups,
    IStokvelContributionRepository ledger,
    IFinancialEventPoster events,
    ITenantContext tenant,
    IClock clock)
    : ICommandHandler<AllocateBenefitsCommand, IReadOnlyList<Guid>>
{
    public async Task<IReadOnlyList<Guid>> HandleAsync(AllocateBenefitsCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var group = await groups.FindAsync(command.GroupId, cancellationToken).ConfigureAwait(false)
            ?? throw StokvelExceptions.GroupNotFound(command.GroupId);

        DateTimeOffset asAt = command.AsAt ?? clock.UtcNow;
        var pool = new Money(command.BonusPoolAmount, command.BonusPoolCurrency);

        IReadOnlyList<StokvelMember> members =
            await groups.ListMembersAsync(group.Id, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<StokvelContribution> contributions =
            await ledger.ListForGroupAsync(group.Id, cancellationToken).ConfigureAwait(false);

        var inputs = members.Select(member => new StokvelBenefitCalculator.MemberWeightInput(
            member.Id,
            member.JoinedAt,
            member.LeftAt,
            contributions
                .Where(c => c.MemberId == member.Id)
                .Select(c => (c.Amount.Amount, c.PaidAt))
                .ToList())).ToList();

        IReadOnlyDictionary<Guid, Money> shares =
            StokvelBenefitCalculator.Split(pool, inputs, asAt);

        decimal totalWeight = inputs.Sum(i =>
            StokvelBenefitCalculator.Weight(i, asAt));
        var ids = new List<Guid>();
        foreach (var member in members)
        {
            decimal weight = StokvelBenefitCalculator.Weight(
                inputs.First(i => i.MemberId == member.Id), asAt);
            Money share = shares[member.Id];
            var row = StokvelBenefitAllocation.Allocate(
                tenant.TenantId, tenant.StoreId, group.Id, member.Id, share,
                $"time-weighted {group.CycleStart:yyyy} cycle, weight {weight:0}/{totalWeight:0}",
                asAt);
            ledger.AddBenefit(row);
            ids.Add(row.Id);

            if (share.Amount > 0m)
            {
                await events.PostAsync(
                    new Events.StokvelBenefitAllocatedEvent(
                        tenant.TenantId, tenant.StoreId, asAt, group.GroupNumber,
                        new Dictionary<string, Money> { ["Benefit"] = share }),
                    cancellationToken).ConfigureAwait(false);
            }
        }

        return ids;
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record CreateHamperBasketCommand(
    Guid GroupId,
    string Name,
    decimal GroupPriceAmount,
    string GroupPriceCurrency,
    DateOnly ValidFrom,
    DateOnly ValidTo,
    string LocationCode,
    IReadOnlyList<HamperLineInput> Lines,
    Guid? CompanyId = null) : ICommand<Guid>;

public sealed class CreateHamperBasketCommandValidator : AbstractValidator<CreateHamperBasketCommand>
{
    public CreateHamperBasketCommandValidator()
    {
        RuleFor(c => c.GroupId).NotEmpty();
        RuleFor(c => c.Name).NotEmpty().MaximumLength(128);
        RuleFor(c => c.GroupPriceAmount).GreaterThan(0m);
        RuleFor(c => c.GroupPriceCurrency).NotEmpty().Length(3);
        RuleFor(c => c.ValidTo).GreaterThanOrEqualTo(c => c.ValidFrom);
        RuleFor(c => c.LocationCode).NotEmpty().MaximumLength(32);
        RuleFor(c => c.Lines).NotEmpty();
    }
}

public sealed class CreateHamperBasketCommandHandler(
    IStokvelGroupRepository groups,
    IStockLocationRepository locations,
    ITenantContext tenant,
    ICompanyContext company)
    : ICommandHandler<CreateHamperBasketCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateHamperBasketCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var group = await groups.FindAsync(command.GroupId, cancellationToken).ConfigureAwait(false)
            ?? throw StokvelExceptions.GroupNotFound(command.GroupId);
        Guid acting = StokvelCompanyScope.ResolveForCreate(company, command.CompanyId, group.CompanyId);

        var location = await locations.FindByCodeAsync(command.LocationCode, cancellationToken).ConfigureAwait(false)
            ?? throw new StokvelExceptions(
                "STOKVEL_LOCATION_NOT_FOUND", $"No stock location answers to '{command.LocationCode}'.");

        var basket = HamperBasket.Create(
            tenant.TenantId, tenant.StoreId, group.Id, command.Name,
            new Money(command.GroupPriceAmount, command.GroupPriceCurrency),
            command.ValidFrom, command.ValidTo, location.Id, acting);
        foreach (var line in command.Lines)
        {
            basket.AddLine(HamperBasketLine.Create(
                tenant.TenantId, tenant.StoreId, basket.Id,
                line.ItemId, line.ItemVariantId, line.Quantity, line.Uom,
                line.SubstitutionItemId, line.SubstitutionItemVariantId));
        }

        groups.AddBasket(basket);
        return basket.Id;
    }
}

internal static class StokvelCompanyScope
{
    /// <summary>
    /// Resolves the acting company for a creator: an explicit request wins, otherwise the bound
    /// scope applies (10c's BindCompany rule). Read-only: creators stamp the company on their rows
    /// but never bind the scope, so one scope can create for one company and keep reading shared
    /// reference data (locations, catalogue) afterwards. Only the reservation paths bind, because
    /// only <c>IReservationService</c> reads the ambient company.
    /// </summary>
    internal static Guid ResolveForCreate(ICompanyContext company, Guid? requested, Guid? rowCompanyId = null)
    {
        ArgumentNullException.ThrowIfNull(company);

        Guid? effective = requested ?? rowCompanyId;
        if (effective.HasValue && effective.Value != Guid.Empty)
        {
            if (company.CompanyId is { } bound && bound != effective.Value)
            {
                throw StokvelExceptions.CompanyMismatch();
            }

            return effective.Value;
        }

        return company.CompanyId ?? throw StokvelExceptions.CompanyRequired();
    }

    /// <summary>
    /// Binds the scope when nothing is bound yet so ambient-only readers see the same company
    /// the rows carry, and refuses a silent mid-operation switch (10c's BindCompany rule).
    /// Returns the acting company.
    /// </summary>
    internal static Guid ResolveAgainst(ICompanyContext company, Guid? rowCompanyId)
    {
        ArgumentNullException.ThrowIfNull(company);

        if (rowCompanyId.HasValue && rowCompanyId.Value != Guid.Empty)
        {
            if (company.CompanyId is { } bound && bound != rowCompanyId.Value)
            {
                throw StokvelExceptions.CompanyMismatch();
            }

            if (company.CompanyId is null)
            {
                company.SetCompany(rowCompanyId.Value);
            }

            return rowCompanyId.Value;
        }

        return company.CompanyId ?? throw StokvelExceptions.CompanyRequired();
    }
}
