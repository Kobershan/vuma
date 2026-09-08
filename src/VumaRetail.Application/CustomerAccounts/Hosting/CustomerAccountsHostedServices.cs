#pragma warning disable CS1591
using Microsoft.Extensions.Hosting;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.CustomerAccounts;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Abstractions.Workflow;
using VumaRetail.Application.Inventory;
using VumaRetail.Domain.CustomerAccounts;
using VumaRetail.Domain.Finance;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Workflow;

namespace VumaRetail.Application.CustomerAccounts.Hosting;

public sealed class LayByExpiryHostedService(
    ILayByAgreementRepository laybys,
    IStockReservationRepository holds,
    IReservationService reservations,
    INotificationDispatcher notifications,
    IClock clock) : BackgroundService
{
    public async Task<int> ExpireDueAsync(CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = clock.UtcNow;
        IReadOnlyList<LayByAgreement> due =
            await laybys.ListExpiringAsync(now, cancellationToken).ConfigureAwait(false);
        int expired = 0;
        foreach (var agreement in due)
        {
            if (agreement.Status != LayByStatus.Active)
            {
                continue;
            }

            agreement.Expire();
            IReadOnlyList<StockReservation> live = await holds.ListOpenByGroupRefAsync(
                agreement.AgreementNumber, cancellationToken).ConfigureAwait(false);
            foreach (Guid chainId in live.Select(h => h.ReservationId).Distinct())
            {
                await reservations.ReleaseAsync(chainId, $"lay-by {agreement.AgreementNumber} expired", cancellationToken).ConfigureAwait(false);
            }

            expired++;
        }

        return expired;
    }

    public async Task<int> RemindUpcomingAsync(CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = clock.UtcNow;
        IReadOnlyList<LayByAgreement> upcoming =
            await laybys.ListExpiringAsync(now.AddDays(14), cancellationToken).ConfigureAwait(false);
        int reminded = 0;
        foreach (var agreement in upcoming)
        {
            if (agreement.Status != LayByStatus.Active || agreement.ExpiryDate <= now)
            {
                continue;
            }

            int days = (int)(agreement.ExpiryDate - now).TotalDays;
            if (days is not (1 or 7 or 14))
            {
                continue;
            }

            await notifications.NotifyAsync(
                new NotificationRequest(
                    agreement.PartnerId, "customer-accounts.layby.expiring",
                    $"Lay-by {agreement.AgreementNumber} expires in {days} day(s)",
                    $"R{agreement.Remaining.Amount} still owed. Pay before {agreement.ExpiryDate:yyyy-MM-dd}.",
                    NotificationSeverity.Warning, [NotificationChannel.InApp],
                    "customer-accounts", "LayByAgreement", agreement.Id),
                cancellationToken).ConfigureAwait(false);
            reminded++;
        }

        return reminded;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            DateTimeOffset now = clock.UtcNow;
            DateTimeOffset next = now.Date.AddDays(1).AddHours(2);
            await Task.Delay(next - now, stoppingToken).ConfigureAwait(false);
            await ExpireDueAsync(stoppingToken).ConfigureAwait(false);
            await RemindUpcomingAsync(stoppingToken).ConfigureAwait(false);
        }
    }
}

public sealed class AccountInterestHostedService(
    IArInvoiceRepository arInvoices,
    ICustomerFinanceTermsRepository terms,
    IFinancialEventPoster events,
    IDocumentNumberSequence numbers,
    INotificationDispatcher notifications,
    ITenantContext tenant,
    IClock clock) : BackgroundService
{
    public async Task<int> AccrueInterestAsync(CancellationToken cancellationToken = default)
    {
        CustomerFinanceTerms? policy =
            await terms.FindAsync(cancellationToken).ConfigureAwait(false);
        if (policy is null || policy.InterestMonthlyRate <= 0m)
        {
            return 0;
        }

        DateTimeOffset now = clock.UtcNow;
        DateOnly today = DateOnly.FromDateTime(now.UtcDateTime);
        IReadOnlyList<ArInvoice> open =
            await arInvoices.ListOpenAsync(cancellationToken).ConfigureAwait(false);

        int raised = 0;
        foreach (var group in open
            .Where(i => (today.DayNumber - i.DueDate.DayNumber) > 30 && i.OutstandingBalance.Amount > 0m)
            .GroupBy(i => i.PartnerId.Value))
        {
            Money interest = group.Aggregate(
                Money.Zero(group.First().OutstandingBalance.Currency),
                (sum, i) => sum + new Money(i.OutstandingBalance.Amount * policy.InterestMonthlyRate, i.OutstandingBalance.Currency));
            if (interest.Amount <= 0m)
            {
                continue;
            }

            string number = await numbers.NextAsync("ARINV", cancellationToken).ConfigureAwait(false);
            var invoice = ArInvoice.Draft(
                tenant.TenantId, tenant.StoreId, new PartnerId(group.Key), number,
                today, today.AddDays(30), interest.Currency);
            invoice.AddLine("Overdue interest", interest, string.Empty, Money.Zero(interest.Currency));
            Guid journalId = await events.PostAsync(
                new Events.AccountInterestRaisedEvent(
                    tenant.TenantId, tenant.StoreId, now, number,
                    new Dictionary<string, Money> { ["Interest"] = interest }),
                cancellationToken).ConfigureAwait(false);
            invoice.Post(journalId);
            arInvoices.Add(invoice);
            await notifications.NotifyAsync(
                new NotificationRequest(
                    group.Key, "customer-accounts.account.dunning",
                    $"Overdue balance R{interest.Amount}",
                    $"Interest of R{interest.Amount} raised on the overdue balance.",
                    NotificationSeverity.Warning, [NotificationChannel.InApp],
                    "customer-accounts", "ArInvoice", invoice.Id),
                cancellationToken).ConfigureAwait(false);
            raised++;
        }

        return raised;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            DateTimeOffset now = clock.UtcNow;
            var first = new DateTimeOffset(now.Year, now.Month, 1, 2, 0, 0, TimeSpan.Zero).AddMonths(1);
            await Task.Delay(first - now, stoppingToken).ConfigureAwait(false);
            await AccrueInterestAsync(stoppingToken).ConfigureAwait(false);
        }
    }
}
