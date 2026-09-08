#pragma warning disable CS1591
using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.CustomerAccounts;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Abstractions.Sales;
using VumaRetail.Application.Inventory;
using VumaRetail.Application.Pos;
using VumaRetail.Domain.CustomerAccounts;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.CustomerAccounts.Commands.LayBy;

public sealed record LayByLineInput(
    Guid? ItemId,
    Guid? ItemVariantId,
    decimal Quantity,
    string Uom);

[CommandSideEffect(SideEffect.Write)]
public sealed record OpenLayByAgreementCommand(
    Guid PartnerId,
    string Currency,
    IReadOnlyList<LayByLineInput> Lines,
    decimal DepositAmount,
    string DepositChannel,
    int TermMonths,
    string LocationCode,
    Guid? CompanyId = null) : ICommand<Guid>;

public sealed class OpenLayByAgreementCommandValidator : AbstractValidator<OpenLayByAgreementCommand>
{
    public OpenLayByAgreementCommandValidator()
    {
        RuleFor(c => c.PartnerId).NotEmpty();
        RuleFor(c => c.Currency).NotEmpty().Length(3);
        RuleFor(c => c.Lines).NotEmpty();
        RuleFor(c => c.DepositAmount).GreaterThan(0m);
        RuleFor(c => c.DepositChannel).NotEmpty().MaximumLength(64);
        RuleFor(c => c.TermMonths).GreaterThan(0);
        RuleFor(c => c.LocationCode).NotEmpty().MaximumLength(32);
    }
}

public sealed class OpenLayByAgreementCommandHandler(
    ILayByAgreementRepository laybys,
    ICustomerFinanceTermsRepository terms,
    IDocumentNumberSequence numbers,
    ISellableItemResolver catalog,
    IPriceResolver prices,
    ITaxCalculator tax,
    IPackSizeResolver packs,
    IStockLocationRepository locations,
    IReservationService reservations,
    IFinancialEventPoster events,
    ITenantContext tenant,
    ICompanyContext company,
    IClock clock)
    : ICommandHandler<OpenLayByAgreementCommand, Guid>
{
    public async Task<Guid> HandleAsync(OpenLayByAgreementCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var policy = await terms.FindAsync(cancellationToken).ConfigureAwait(false)
            ?? throw LayByExceptions.TermsNotConfigured();
        if (command.TermMonths > policy.LayByMaxTermMonths)
        {
            throw LayByExceptions.TermExceedsMaximum(policy.LayByMaxTermMonths);
        }

        DateTimeOffset now = clock.UtcNow;
        DateOnly today = DateOnly.FromDateTime(now.UtcDateTime);
        TimeOnly nowTime = TimeOnly.FromDateTime(now.UtcDateTime);
        DateTimeOffset expiry = now.AddMonths(command.TermMonths);

        string number = await numbers.NextAsync("LAY", cancellationToken).ConfigureAwait(false);
        var total = Money.Zero(command.Currency);
        var snapshots = new List<LayByLineSnapshot>();
        foreach (var input in command.Lines)
        {
            SellableItem item = await catalog.ResolveAsync(input.ItemId, input.ItemVariantId, cancellationToken).ConfigureAwait(false);
            PriceResolution resolution = await prices.ResolveAsync(
                new PriceResolutionRequest(
                    input.ItemId, input.ItemVariantId, null, input.Quantity,
                    tenant.StoreId, today, nowTime, command.Currency),
                cancellationToken).ConfigureAwait(false);
            TaxCalculation calculation = await tax.CalculateAsync(
                item.TaxClassCode, resolution.NetPayable, today, cancellationToken).ConfigureAwait(false);
            PackSizeSnapshot pack = await packs.ResolveAsync(
                input.ItemId, input.ItemVariantId, input.Uom, input.Quantity, cancellationToken).ConfigureAwait(false);
            var net = resolution.NetPayable;
            snapshots.Add(new LayByLineSnapshot(
                input.ItemId, input.ItemVariantId, input.Quantity, input.Uom,
                resolution.UnitPrice, resolution.DiscountAmount, calculation.TaxAmount,
                pack.Description, resolution.PriceListId));
            total += net + calculation.TaxAmount;
        }

        var deposit = new Money(command.DepositAmount, command.Currency);
        var location = await locations.FindByCodeAsync(command.LocationCode, cancellationToken).ConfigureAwait(false)
            ?? throw LayByExceptions.LocationNotFound(command.LocationCode);

        var agreement = LayByAgreement.Open(
            tenant.TenantId, tenant.StoreId, number, command.PartnerId, total, deposit,
            command.TermMonths, expiry, policy.LayByAdminFee,
            LayByCompanyScope.ResolveCompany(company, command.CompanyId));
        foreach (var snapshot in snapshots)
        {
            agreement.AddLine(LayByAgreementLine.Create(
                tenant.TenantId, tenant.StoreId, agreement.Id,
                snapshot.ItemId, snapshot.ItemVariantId, snapshot.Quantity, snapshot.Uom,
                snapshot.UnitPrice, snapshot.Discount, snapshot.Tax,
                snapshot.PackSize, command.Currency, snapshot.PriceListId));
        }

        agreement.Activate();

        foreach (var line in agreement.Lines)
        {
            ReserveOutcome outcome = await reservations.ReserveAsync(
                location.Id, line.ItemId, line.ItemVariantId,
                new Quantity(line.QuantityValue, line.QuantityUom),
                ReservationSource.LayBy, agreement.Id, agreement.AgreementNumber,
                expiresAt: expiry, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (outcome.Shortfall.Value > 0m)
            {
                throw LayByExceptions.InsufficientStock();
            }
        }

        string receiptReference = await numbers.NextAsync("LAYRCPT", cancellationToken).ConfigureAwait(false);
        var first = LayByInstalment.Record(
            tenant.TenantId, tenant.StoreId, agreement.Id, 1, deposit,
            receiptReference, now, command.DepositChannel);
        agreement.AddInstalment(first);

        await events.PostAsync(
            new Events.LayByDepositReceivedEvent(
                tenant.TenantId, tenant.StoreId, now, number,
                new Dictionary<string, Money> { ["Principal"] = deposit }),
            cancellationToken).ConfigureAwait(false);

        laybys.Add(agreement);
        return agreement.Id;
    }

    private sealed record LayByLineSnapshot(
        Guid? ItemId,
        Guid? ItemVariantId,
        decimal Quantity,
        string Uom,
        Money UnitPrice,
        Money Discount,
        Money Tax,
        string PackSize,
        Guid? PriceListId);
}

[CommandSideEffect(SideEffect.Write)]
public sealed record RecordLayByInstalmentCommand(
    Guid AgreementId,
    decimal Amount,
    string Currency,
    string Channel,
    string ReceiptReference,
    bool TakenOffline = false) : ICommand<Guid>;

public sealed class RecordLayByInstalmentCommandValidator : AbstractValidator<RecordLayByInstalmentCommand>
{
    public RecordLayByInstalmentCommandValidator()
    {
        RuleFor(c => c.AgreementId).NotEmpty();
        RuleFor(c => c.Amount).GreaterThan(0m);
        RuleFor(c => c.Currency).NotEmpty().Length(3);
        RuleFor(c => c.Channel).NotEmpty().MaximumLength(64);
        RuleFor(c => c.ReceiptReference).NotEmpty().MaximumLength(64);
    }
}

public sealed class RecordLayByInstalmentCommandHandler(
    ILayByAgreementRepository laybys,
    IFinancialEventPoster events,
    ITenantContext tenant,
    IClock clock)
    : ICommandHandler<RecordLayByInstalmentCommand, Guid>
{
    public async Task<Guid> HandleAsync(RecordLayByInstalmentCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var agreement = await laybys.FindAsync(command.AgreementId, cancellationToken).ConfigureAwait(false)
            ?? throw new LayByExceptions("LAYBY_NOT_FOUND", $"No lay-by with id {command.AgreementId}.");
        DateTimeOffset now = clock.UtcNow;
        var row = LayByInstalment.Record(
            tenant.TenantId, tenant.StoreId, agreement.Id,
            agreement.Instalments.Count + 1, new Money(command.Amount, command.Currency),
            command.ReceiptReference, now, command.Channel, command.TakenOffline);
        agreement.AddInstalment(row);

        await events.PostAsync(
            new Events.LayByInstalmentReceivedEvent(
                tenant.TenantId, tenant.StoreId, now, agreement.AgreementNumber,
                new Dictionary<string, Money> { ["Principal"] = row.Amount }),
            cancellationToken).ConfigureAwait(false);

        return row.Id;
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record CompleteLayByAgreementCommand(Guid AgreementId, bool CapturedOffline = false) : ICommand;

public sealed class CompleteLayByAgreementCommandValidator : AbstractValidator<CompleteLayByAgreementCommand>
{
    public CompleteLayByAgreementCommandValidator() => RuleFor(c => c.AgreementId).NotEmpty();
}

public sealed class CompleteLayByAgreementCommandHandler(
    ILayByAgreementRepository laybys,
    IStockReservationRepository holds,
    IReservationService reservations,
    IFinancialEventPoster events,
    ITenantContext tenant,
    ICompanyContext company,
    IClock clock)
    : ICommandHandler<CompleteLayByAgreementCommand, Unit>
{
    public async Task<Unit> HandleAsync(CompleteLayByAgreementCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.CapturedOffline)
        {
            throw LayByExceptions.CompletionNeedsConnectivity();
        }

        var agreement = await laybys.FindAsync(command.AgreementId, cancellationToken).ConfigureAwait(false)
            ?? throw new LayByExceptions("LAYBY_NOT_FOUND", $"No lay-by with id {command.AgreementId}.");
        LayByCompanyScope.ResolveCompany(company, agreement.CompanyId);
        DateTimeOffset now = clock.UtcNow;

        IReadOnlyList<StockReservation> open = await holds.ListOpenByGroupRefAsync(
            agreement.AgreementNumber, cancellationToken).ConfigureAwait(false);
        foreach (Guid chainId in open.Select(h => h.ReservationId).Distinct())
        {
            await reservations.ConsumeAsync(chainId, agreement.Id, cancellationToken).ConfigureAwait(false);
        }

        Money net = Money.Zero(agreement.AgreedTotal.Currency);
        Money tax = Money.Zero(agreement.AgreedTotal.Currency);
        foreach (var line in agreement.Lines)
        {
            net += line.Net;
            tax += line.TaxAmount;
        }

        await events.PostAsync(
            new Events.LayByCompletedEvent(
                tenant.TenantId, tenant.StoreId, now, agreement.AgreementNumber,
                new Dictionary<string, Money>
                {
                    ["Principal"] = agreement.AgreedTotal,
                    ["Net"] = net,
                    ["Tax"] = tax,
                }),
            cancellationToken).ConfigureAwait(false);

        agreement.Complete(now);
        return Unit.Value;
    }
}

[CommandSideEffect(SideEffect.Write)]
public sealed record CancelLayByAgreementCommand(Guid AgreementId) : ICommand;

public sealed class CancelLayByAgreementCommandValidator : AbstractValidator<CancelLayByAgreementCommand>
{
    public CancelLayByAgreementCommandValidator() => RuleFor(c => c.AgreementId).NotEmpty();
}

public sealed class CancelLayByAgreementCommandHandler(
    ILayByAgreementRepository laybys,
    IStockReservationRepository holds,
    IReservationService reservations,
    IFinancialEventPoster events,
    ITenantContext tenant,
    ICompanyContext company,
    IClock clock)
    : ICommandHandler<CancelLayByAgreementCommand, Unit>
{
    public async Task<Unit> HandleAsync(CancelLayByAgreementCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var agreement = await laybys.FindAsync(command.AgreementId, cancellationToken).ConfigureAwait(false)
            ?? throw new LayByExceptions("LAYBY_NOT_FOUND", $"No lay-by with id {command.AgreementId}.");
        LayByCompanyScope.ResolveCompany(company, agreement.CompanyId);
        DateTimeOffset now = clock.UtcNow;

        Money fee = agreement.PaidToDate.Amount < agreement.AdminFee.Amount
            ? agreement.PaidToDate
            : agreement.AdminFee;
        Money refund = agreement.PaidToDate - fee;
        agreement.Cancel(now, refund, fee);

        IReadOnlyList<StockReservation> live = await holds.ListOpenByGroupRefAsync(
            agreement.AgreementNumber, cancellationToken).ConfigureAwait(false);
        foreach (Guid chainId in live.Select(h => h.ReservationId).Distinct())
        {
            await reservations.ReleaseAsync(chainId, $"lay-by {agreement.AgreementNumber} cancelled", cancellationToken).ConfigureAwait(false);
        }

        await events.PostAsync(
            new Events.LayByCancelledEvent(
                tenant.TenantId, tenant.StoreId, now, agreement.AgreementNumber,
                new Dictionary<string, Money> { ["Refund"] = refund, ["Fee"] = fee }),
            cancellationToken).ConfigureAwait(false);

        return Unit.Value;
    }
}

internal static class LayByCompanyScope
{
    /// <summary>
    /// Resolves the acting company for a lay-by leg: an explicit request wins, otherwise the
    /// bound scope applies, and a bound scope never silently switches mid-operation (10c's
    /// BindCompany rule). Binds the scope when nothing is bound yet so ambient-only readers
    /// (notably <c>IReservationService</c>) see the same company the rows carry.
    /// </summary>
    internal static Guid ResolveCompany(ICompanyContext company, Guid? requested)
    {
        ArgumentNullException.ThrowIfNull(company);

        if (requested.HasValue && requested.Value != Guid.Empty)
        {
            if (company.CompanyId is { } bound && bound != requested.Value)
            {
                throw LayByExceptions.CompanyMismatch();
            }

            if (company.CompanyId is null)
            {
                company.SetCompany(requested.Value);
            }

            return requested.Value;
        }

        return company.CompanyId ?? throw LayByExceptions.CompanyRequired();
    }
}
