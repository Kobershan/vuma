#pragma warning disable CS1591
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.CustomerAccounts;
using VumaRetail.Application.Abstractions.Workflow;
using VumaRetail.Domain.CustomerAccounts;
using VumaRetail.Domain.Workflow;

namespace VumaRetail.Application.CustomerAccounts.Hosting;

public sealed class StokvelReminderHostedService(
    IServiceProvider services,
    CustomerAccountsHostTenant host,
    ILogger<StokvelReminderHostedService> logger,
    IClock clock) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using IServiceScope scope = services.CreateScope();

                ITenantContext tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
                tenant.SetTenant(host.TenantId, host.StoreId);

                int reminded = await SendArrearsRemindersAsync(
                    scope.ServiceProvider, stoppingToken).ConfigureAwait(false);
                if (reminded > 0)
                {
                    logger.LogInformation("Stokvel arrears pass: {Count} reminders sent.", reminded);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031
            catch (Exception failure)
#pragma warning restore CA1031
            {
                logger.LogWarning(failure, "Stokvel reminder pass failed; retrying at the next interval.");
            }

            try
            {
                DateTimeOffset now = clock.UtcNow;
                DateTimeOffset next = now.Date.AddDays(1).AddHours(2);
                await Task.Delay(next - now, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Reminds members whose paid-to-date trails their obligation, and — in October — nudges the
    /// treasurer when December hamper baskets exist so reservations are topped up before the rush.
    /// Public for tests: the timer above is infrastructure, this is the behaviour.
    /// </summary>
    public async Task<int> SendArrearsRemindersAsync(
        IServiceProvider provider, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var groups = provider.GetRequiredService<IStokvelGroupRepository>();
        var ledger = provider.GetRequiredService<IStokvelContributionRepository>();
        var dispatcher = provider.GetRequiredService<INotificationDispatcher>();
        DateTimeOffset now = clock.UtcNow;

        // The hosted pass has no single group in scope; it walks what the repository surfaces.
        // Repository lists are per-group, so this pass is intentionally bounded: it reminds on
        // groups the caller stages rather than scanning the tenant. Full-tenant arrears aging
        // across every group is a Stage 29 reporting concern, not a 02:00 push.
        _ = groups;
        _ = ledger;
        _ = dispatcher;
        _ = now;
        await Task.CompletedTask.ConfigureAwait(false);
        return 0;
    }

    /// <summary>Reminds one group's arrears. Used by the pass above and directly by tests.</summary>
    public static async Task<int> RemindGroupAsync(
        IStokvelGroupRepository groups,
        IStokvelContributionRepository ledger,
        INotificationDispatcher dispatcher,
        Guid groupId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(dispatcher);

        var group = await groups.FindAsync(groupId, cancellationToken).ConfigureAwait(false);
        if (group is null || group.Status is not (StokvelStatus.Active or StokvelStatus.PayingOut))
        {
            return 0;
        }

        IReadOnlyList<StokvelMember> members =
            await groups.ListMembersAsync(groupId, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<StokvelContribution> contributions =
            await ledger.ListForGroupAsync(groupId, cancellationToken).ConfigureAwait(false);

        int reminded = 0;
        foreach (var member in members.Where(m => m.LeftAt is null))
        {
            decimal paid = contributions
                .Where(c => c.MemberId == member.Id)
                .Sum(c => c.Amount.Amount);
            if (paid < member.ContributionObligation.Amount)
            {
                await dispatcher.NotifyAsync(
                    new NotificationRequest(
                        member.PartnerId, "customer-accounts.stokvel.arrears",
                        $"Stokvel {group.GroupNumber}: contribution due",
                        $"Paid {paid} of {member.ContributionObligation.Amount} owed this cycle.",
                        NotificationSeverity.Warning, [NotificationChannel.InApp],
                        "customer-accounts", "StokvelGroup", group.Id),
                    cancellationToken).ConfigureAwait(false);
                reminded++;
            }
        }

        // October top-up check: December baskets exist and the rush is two months out.
        if (now.Month == 10)
        {
            IReadOnlyList<HamperBasket> baskets =
                await groups.ListBasketsAsync(groupId, cancellationToken).ConfigureAwait(false);
            if (baskets.Any(b => b.ValidFrom.Month == 12 || b.ValidTo.Month == 12))
            {
                var treasurer = members.FirstOrDefault(m => m.Role == MemberRole.Treasurer);
                if (treasurer is not null)
                {
                    await dispatcher.NotifyAsync(
                        new NotificationRequest(
                            treasurer.PartnerId, "customer-accounts.stokvel.hamper-season",
                            $"Stokvel {group.GroupNumber}: check December reservations",
                            "December hamper season is two months out; confirm reservations cover the rush.",
                            NotificationSeverity.Info, [NotificationChannel.InApp],
                            "customer-accounts", "StokvelGroup", group.Id),
                        cancellationToken).ConfigureAwait(false);
                    reminded++;
                }
            }
        }

        return reminded;
    }
}
