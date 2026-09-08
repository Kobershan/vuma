#pragma warning disable CS1591
using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.CustomerAccounts;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Abstractions.Workflow;
using VumaRetail.Application.Inventory;
using VumaRetail.Application.Pos;
using VumaRetail.Domain.CustomerAccounts;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Pos;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.CustomerAccounts.Commands.Stokvels;

[CommandSideEffect(SideEffect.Write)]
public sealed record RequestPayoutCommand(
    Guid GroupId,
    Guid MemberId,
    StokvelPayoutKind Kind,
    decimal Amount,
    string Currency,
    Guid? HamperBasketId = null,
    bool CapturedOffline = false) : ICommand<Guid>;

public sealed class RequestPayoutCommandValidator : AbstractValidator<RequestPayoutCommand>
{
    public RequestPayoutCommandValidator()
    {
        RuleFor(c => c.GroupId).NotEmpty();
        RuleFor(c => c.MemberId).NotEmpty();
        RuleFor(c => c.Amount).GreaterThan(0m);
        RuleFor(c => c.Currency).NotEmpty().Length(3);
    }
}

public sealed class RequestPayoutCommandHandler(
    IStokvelGroupRepository groups,
    IStokvelContributionRepository ledger,
    IStokvelPayoutRepository payouts,
    ICustomerFinanceTermsRepository terms,
    IReservationService reservations,
    ITenantContext tenant,
    ICompanyContext company,
    IClock clock)
    : ICommandHandler<RequestPayoutCommand, Guid>
{
    public async Task<Guid> HandleAsync(RequestPayoutCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var group = await groups.FindAsync(command.GroupId, cancellationToken).ConfigureAwait(false)
            ?? throw StokvelExceptions.GroupNotFound(command.GroupId);
        StokvelCompanyScope.ResolveAgainst(company, group.CompanyId);
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

        if (command.Kind is StokvelPayoutKind.Goods or StokvelPayoutKind.Hamper && command.HamperBasketId is null)
        {
            throw StokvelExceptions.BasketRequired();
        }

        HamperBasket? basket = null;
        if (command.HamperBasketId.HasValue)
        {
            basket = await groups.FindBasketAsync(command.HamperBasketId.Value, cancellationToken).ConfigureAwait(false)
                ?? throw StokvelExceptions.HamperNotFound(command.HamperBasketId.Value);
            if (basket.GroupId != group.Id)
            {
                throw StokvelExceptions.HamperNotFound(command.HamperBasketId.Value);
            }

            if (basket.GroupPrice.Amount != command.Amount
                || !string.Equals(basket.GroupPrice.Currency, command.Currency, StringComparison.OrdinalIgnoreCase))
            {
                throw new StokvelExceptions(
                    "STOKVEL_PAYOUT_BASKET_MISMATCH",
                    $"This payout is for basket '{basket.Name}' at {basket.GroupPrice}; request that amount.");
            }
        }

        var amount = new Money(command.Amount, command.Currency);
        Money available = await StokvelBalances.AvailableAsync(
            ledger, payouts, member, command.Currency, cancellationToken).ConfigureAwait(false);
        if (amount.Amount > available.Amount)
        {
            throw StokvelExceptions.InsufficientAvailable();
        }

        if (command.CapturedOffline)
        {
            CustomerFinanceTerms? policy =
                await terms.FindAsync(cancellationToken).ConfigureAwait(false);
            int threshold = policy?.StaleBalanceMinutes ?? 15;
            DateTimeOffset now = clock.UtcNow;
            DateTimeOffset lastActivity = await StokvelBalances.LastActivityAsync(
                ledger, member, cancellationToken).ConfigureAwait(false);
            if ((now - lastActivity).TotalMinutes > threshold)
            {
                throw StokvelExceptions.StaleBalanceNeedsConnectivity();
            }
        }

        DateTimeOffset requestedAt = clock.UtcNow;
        var payout = StokvelPayout.Request(
            tenant.TenantId, tenant.StoreId, group.Id, member.Id,
            command.Kind, amount, command.HamperBasketId, requestedAt);
        payouts.Add(payout);

        // Reservation day is request day: hamper stock stops reading as sellable the moment the
        // member asks for it, not when the back office approves it.
        if (basket is not null)
        {
            foreach (var line in basket.Lines)
            {
                ReserveOutcome outcome = await reservations.ReserveAsync(
                    basket.LocationId, line.ItemId, line.ItemVariantId,
                    new Quantity(line.QuantityValue, line.QuantityUom),
                    ReservationSource.StokvelHamper, payout.Id, group.GroupNumber,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                if (outcome.Shortfall.Value > 0m)
                {
                    throw StokvelExceptions.InsufficientAvailable();
                }
            }
        }

        return payout.Id;
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record ApprovePayoutCommand(Guid PayoutId) : ICommand;

public sealed class ApprovePayoutCommandValidator : AbstractValidator<ApprovePayoutCommand>
{
    public ApprovePayoutCommandValidator() => RuleFor(c => c.PayoutId).NotEmpty();
}

public sealed class ApprovePayoutCommandHandler(
    IStokvelPayoutRepository payouts,
    IApprovalService approvals,
    IClock clock)
    : ICommandHandler<ApprovePayoutCommand, Unit>
{
    public async Task<Unit> HandleAsync(ApprovePayoutCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var payout = await payouts.FindAsync(command.PayoutId, cancellationToken).ConfigureAwait(false)
            ?? throw StokvelExceptions.PayoutNotFound(command.PayoutId);

        var outcome = await approvals.EvaluateAsync(
            new ApprovalContext(
                "customer-accounts", "StokvelPayout", "Approve", payout.Id, payout.Amount),
            cancellationToken).ConfigureAwait(false);
        if (!outcome.MayProceed)
        {
            throw StokvelExceptions.ApprovalRequired();
        }

        payout.Approve(clock.UtcNow);
        return Unit.Value;
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record SettlePayoutCommand(Guid PayoutId, bool CapturedOffline = false) : ICommand<Guid>;

public sealed class SettlePayoutCommandValidator : AbstractValidator<SettlePayoutCommand>
{
    public SettlePayoutCommandValidator() => RuleFor(c => c.PayoutId).NotEmpty();
}

public sealed class SettlePayoutCommandHandler(
    IStokvelGroupRepository groups,
    IStokvelContributionRepository ledger,
    IStokvelPayoutRepository payouts,
    ICustomerFinanceTermsRepository terms,
    IStockReservationRepository holds,
    IReservationService reservations,
    IAvailabilityService availability,
    ISellableItemResolver catalog,
    ITaxCalculator tax,
    ISaleRepository sales,
    IDocumentNumberSequence numbers,
    ISaleCompletionService completion,
    IFinancialEventPoster events,
    ITenantContext tenant,
    ICompanyContext company,
    IClock clock)
    : ICommandHandler<SettlePayoutCommand, Guid>
{
    public async Task<Guid> HandleAsync(SettlePayoutCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var payout = await payouts.FindAsync(command.PayoutId, cancellationToken).ConfigureAwait(false)
            ?? throw StokvelExceptions.PayoutNotFound(command.PayoutId);
        var group = await groups.FindAsync(payout.GroupId, cancellationToken).ConfigureAwait(false)
            ?? throw StokvelExceptions.GroupNotFound(payout.GroupId);
        StokvelCompanyScope.ResolveAgainst(company, group.CompanyId);
        var member = await groups.FindMemberAsync(payout.MemberId, cancellationToken).ConfigureAwait(false)
            ?? throw StokvelExceptions.MemberNotFound(payout.MemberId);

        DateTimeOffset now = clock.UtcNow;

        // Rule 3: availability is evaluated at settle time, not request time.
        Money available = await StokvelBalances.AvailableAsync(
            ledger, payouts, member, payout.Amount.Currency, cancellationToken).ConfigureAwait(false);
        if (payout.Amount.Amount > available.Amount)
        {
            throw StokvelExceptions.InsufficientAvailable();
        }

        if (command.CapturedOffline)
        {
            CustomerFinanceTerms? policy =
                await terms.FindAsync(cancellationToken).ConfigureAwait(false);
            int threshold = policy?.StaleBalanceMinutes ?? 15;
            DateTimeOffset lastActivity = await StokvelBalances.LastActivityAsync(
                ledger, member, cancellationToken).ConfigureAwait(false);
            if ((now - lastActivity).TotalMinutes > threshold)
            {
                throw StokvelExceptions.StaleBalanceNeedsConnectivity();
            }
        }

        if (payout.Kind is StokvelPayoutKind.Cash or StokvelPayoutKind.StoreCredit)
        {
            await events.PostAsync(
                new Events.StokvelPayoutSettledEvent(
                    tenant.TenantId, tenant.StoreId, now, group.GroupNumber,
                    new Dictionary<string, Money>
                    {
                        ["Principal"] = payout.Amount,
                        ["BasketDiscount"] = Money.Zero(payout.Amount.Currency),
                    }),
                cancellationToken).ConfigureAwait(false);
            payout.Settle(now);
            return payout.Id;
        }

        // Goods/hamper: one normal sale — one stock issue set, one revenue recognition.
        if (!payout.HamperBasketId.HasValue)
        {
            throw StokvelExceptions.BasketRequired();
        }

        HamperBasket? basket = await groups.FindBasketAsync(
            payout.HamperBasketId.Value, cancellationToken).ConfigureAwait(false)
            ?? throw StokvelExceptions.HamperNotFound(payout.HamperBasketId.Value);

        Sale sale = await BuildHamperSaleAsync(
            group, member, basket, payout, now, cancellationToken).ConfigureAwait(false);
        sales.Add(sale);
        await completion.CompleteAsync(sale, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<StockReservation> open = await holds.ListOpenByGroupRefAsync(
            group.GroupNumber, cancellationToken).ConfigureAwait(false);
        foreach (Guid chainId in open
            .Where(h => h.SourceDocumentId == payout.Id)
            .Select(h => h.ReservationId)
            .Distinct())
        {
            await reservations.ConsumeAsync(chainId, sale.Id, cancellationToken).ConfigureAwait(false);
        }

        payout.Settle(now, sale.Id);
        return payout.Id;
    }

    private async Task<Sale> BuildHamperSaleAsync(
        StokvelGroup group,
        StokvelMember member,
        HamperBasket basket,
        StokvelPayout payout,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Back-office settlement has no open till session (ADR-143's shape for lay-by completion):
        // the session below is an in-memory anchor for Sale.Open's invariant, never persisted —
        // Sale.TillSessionId is a bare Guid with no foreign key, so nothing dangles.
        Guid terminalId = group.StoreScopeId;
        var session = TillSession.Open(
            tenant.TenantId, tenant.StoreId, terminalId, member.PartnerId,
            Money.Zero(payout.Amount.Currency), now);

        string saleNumber = await numbers.NextAsync("SALE", cancellationToken).ConfigureAwait(false);
        var sale = Sale.Open(
            UuidV7.NewGuid(), tenant.TenantId, tenant.StoreId, saleNumber, session,
            member.PartnerId, basket.LocationId, member.PartnerId,
            payout.Amount.Currency, now);

        // The group price is for the whole basket; apportion by quantity so multi-line baskets
        // still reconcile, with the last line plugging the rounding. Single-line baskets — the
        // seed, the fixture, the December rush — are exact by construction.
        DateOnly today = DateOnly.FromDateTime(now.UtcDateTime);
        decimal totalQty = basket.Lines.Sum(l => l.QuantityValue);
        Money apportioned = Money.Zero(payout.Amount.Currency);
        int index = 0;
        foreach (var line in basket.Lines)
        {
            index++;
            Guid? itemId = line.ItemId;
            Guid? variantId = line.ItemVariantId;

            // Substitution fires only when the line item's available is zero at settle time,
            // priced at the basket's group price — never re-resolved.
            if (line.SubstitutionItemId.HasValue || line.SubstitutionItemVariantId.HasValue)
            {
                LocalAvailability local = await availability.GetLocalAsync(
                    basket.LocationId, itemId, variantId, cancellationToken).ConfigureAwait(false);
                if (local.Promise.Available.Value <= 0m)
                {
                    itemId = line.SubstitutionItemId;
                    variantId = line.SubstitutionItemVariantId;
                }
            }

            SellableItem sellable = await catalog.ResolveAsync(itemId, variantId, cancellationToken)
                .ConfigureAwait(false);

            Money grossShare = index < basket.Lines.Count
                ? new Money(
                    Math.Round(
                        basket.GroupPrice.Amount * (line.QuantityValue / totalQty),
                        2, MidpointRounding.AwayFromZero),
                    payout.Amount.Currency)
                : basket.GroupPrice - apportioned;
            apportioned += grossShare;

            TaxCalculation calculation = await tax.CalculateAsync(
                sellable.TaxClassCode, grossShare, today, cancellationToken).ConfigureAwait(false);

            sale.AddLine(SaleLine.Ring(
                tenant.TenantId, tenant.StoreId, sale.Id, sale.NextLineNumber,
                sellable.ItemId, sellable.ItemVariantId, sellable.Description,
                new Quantity(line.QuantityValue, line.QuantityUom),
                calculation.GrossAmount.Amount == 0m
                    ? Money.Zero(payout.Amount.Currency)
                    : new Money(
                        calculation.GrossAmount.Amount / line.QuantityValue, payout.Amount.Currency),
                Money.Zero(payout.Amount.Currency),
                calculation.TaxCode,
                calculation.NetAmount,
                calculation.TaxAmount,
                calculation.GrossAmount));
        }

        sale.AddTender(SaleTender.Capture(
            tenant.TenantId, tenant.StoreId, sale.Id, TenderType.Voucher,
            sale.Gross, payout.Id.ToString(), now));
        return sale;
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record RemoveMemberCommand(Guid GroupId, Guid MemberId) : ICommand<Money>;

public sealed class RemoveMemberCommandValidator : AbstractValidator<RemoveMemberCommand>
{
    public RemoveMemberCommandValidator()
    {
        RuleFor(c => c.GroupId).NotEmpty();
        RuleFor(c => c.MemberId).NotEmpty();
    }
}

public sealed class RemoveMemberCommandHandler(
    IStokvelGroupRepository groups,
    IStokvelContributionRepository ledger,
    IStokvelPayoutRepository payouts,
    ICustomerFinanceTermsRepository terms,
    IClock clock)
    : ICommandHandler<RemoveMemberCommand, Money>
{
    public async Task<Money> HandleAsync(RemoveMemberCommand command, CancellationToken cancellationToken = default)
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

        IReadOnlyList<StokvelContribution> groupContributions =
            await ledger.ListForGroupAsync(group.Id, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<StokvelPayout> settled = await payouts.ListForGroupAsync(group.Id, cancellationToken)
            .ConfigureAwait(false);

        string currency = groupContributions.FirstOrDefault()?.Amount.Currency
            ?? (await ledger.ListBenefitsForGroupAsync(group.Id, cancellationToken).ConfigureAwait(false))
                .FirstOrDefault()?.Amount.Currency
            ?? settled.FirstOrDefault()?.Amount.Currency
            ?? "ZAR";

        Money paidIn = groupContributions
            .Where(c => c.MemberId == member.Id)
            .Aggregate(Money.Zero(currency), (sum, c) => sum + c.Amount);
        Money totalPaidIn = groupContributions
            .Aggregate(Money.Zero(currency), (sum, c) => sum + c.Amount);
        Money committed = settled
            .Where(p => p.Status == StokvelPayoutStatus.Settled)
            .Aggregate(Money.Zero(currency), (sum, p) => sum + p.Amount);

        CustomerFinanceTerms? policy =
            await terms.FindAsync(cancellationToken).ConfigureAwait(false);
        Money fee = policy?.LayByAdminFee ?? Money.Zero(currency);
        if (!string.Equals(fee.Currency, currency, StringComparison.Ordinal))
        {
            fee = Money.Zero(currency);
        }

        // Cap the fee at what was paid: leaving with nothing still accounts for every cent.
        if (fee.Amount > paidIn.Amount)
        {
            fee = paidIn;
        }

        Money refund = StokvelBenefitCalculator.LeavingRefund(paidIn, totalPaidIn, committed, fee);
        member.Leave(clock.UtcNow);
        return refund;
    }
}

internal static class StokvelBalances
{
    internal static async Task<Money> AvailableAsync(
        IStokvelContributionRepository ledger,
        IStokvelPayoutRepository payouts,
        StokvelMember member,
        string currency,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<StokvelContribution> contributions =
            await ledger.ListForMemberAsync(member.Id, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<StokvelBenefitAllocation> benefits =
            await ledger.ListBenefitsForMemberAsync(member.Id, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<StokvelPayout> history =
            await payouts.ListForMemberAsync(member.Id, cancellationToken).ConfigureAwait(false);

        Money total = Money.Zero(currency);
        foreach (var c in contributions)
        {
            total += c.Amount;
        }

        foreach (var b in benefits)
        {
            total += b.Amount;
        }

        foreach (var p in history.Where(p => p.Status == StokvelPayoutStatus.Settled))
        {
            total -= p.Amount;
        }

        return total;
    }

    internal static async Task<DateTimeOffset> LastActivityAsync(
        IStokvelContributionRepository ledger,
        StokvelMember member,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<StokvelContribution> contributions =
            await ledger.ListForMemberAsync(member.Id, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<StokvelBenefitAllocation> benefits =
            await ledger.ListBenefitsForMemberAsync(member.Id, cancellationToken).ConfigureAwait(false);

        DateTimeOffset last = member.JoinedAt;
        foreach (var c in contributions)
        {
            if (c.PaidAt > last)
            {
                last = c.PaidAt;
            }
        }

        foreach (var b in benefits)
        {
            if (b.AllocatedAt > last)
            {
                last = b.AllocatedAt;
            }
        }

        return last;
    }
}
