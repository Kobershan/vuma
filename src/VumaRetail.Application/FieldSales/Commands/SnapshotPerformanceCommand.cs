using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.FieldSales;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Abstractions.Sales;
using VumaRetail.Application.FieldSales;
using VumaRetail.Domain.FieldSales;

#pragma warning disable CS1591
#pragma warning disable CA1062

namespace VumaRetail.Application.FieldSales.Commands;

/// <summary>Snapshots one closed month for every rep in the acting company.</summary>
[CommandSideEffect(SideEffect.Write)]
public sealed record SnapshotPerformanceCommand(DateOnly PeriodStart) : ICommand<int>;

/// <summary>Validates <see cref="SnapshotPerformanceCommand"/>.</summary>
public sealed class SnapshotPerformanceCommandValidator : AbstractValidator<SnapshotPerformanceCommand>
{
    public SnapshotPerformanceCommandValidator()
    {
        RuleFor(c => c.PeriodStart.Day).Equal(1);
    }
}

/// <summary>Handler for <see cref="SnapshotPerformanceCommand"/>.</summary>
/// <remarks>
/// Company-bound: snapshots one company's books. The group figure is assembled at read time
/// from per-company snapshots, never stored — one writer per database, no cross-company read
/// inside a write (ADR-119).
/// </remarks>
[CommandSideEffect(SideEffect.Write)]
public sealed class SnapshotPerformanceCommandHandler(
    IRepRepository reps,
    IProFormaOrderRepository proFormas,
    IProFormaCreditNoteRepository credits,
    IInvoiceRepository invoices,
    IRepPerformanceRepository snapshots,
    IRepPerformanceCalculator calculator,
    ITenantContext tenant,
    ICompanyContext company,
    IClock clock)
    : ICommandHandler<SnapshotPerformanceCommand, int>
{
    public async Task<int> HandleAsync(SnapshotPerformanceCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Guid companyId = company.RequireCompany();
        DateOnly from = command.PeriodStart;
        DateOnly to = from.AddMonths(1);

        if (to > DateOnly.FromDateTime(clock.UtcNow.UtcDateTime))
        {
            throw new FieldSalesException(
                "PERFORMANCE_PERIOD_OPEN",
                $"Period {from:yyyy-MM} is not closed yet.");
        }

        int snapshotted = 0;
        foreach (Rep rep in await reps.ListAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (rep.TenantId != tenant.TenantId || !rep.MaySellFor(companyId))
            {
                continue;
            }

            List<ProFormaPerformanceRow> rows = [];
            string? currency = null;
            foreach (ProFormaOrder order in await proFormas.ListForRepAsync(rep.Id, cancellationToken).ConfigureAwait(false))
            {
                if (order.CompanyId != companyId
                    || DateOnly.FromDateTime(order.CapturedAt.UtcDateTime) < from
                    || DateOnly.FromDateTime(order.CapturedAt.UtcDateTime) >= to)
                {
                    continue;
                }

                currency ??= order.Currency;
                rows.Add(new ProFormaPerformanceRow(order.Status, order.Gross.Amount, order.PartnerId));
            }

            // Invoices posted in the month for this rep's converted orders: the pro forma
            // number rides the group reference.
            HashSet<string> repNumbers = new(
                (await proFormas.ListForRepAsync(rep.Id, cancellationToken).ConfigureAwait(false))
                    .Where(order => order.CompanyId == companyId && order.Status == ProFormaStatus.Converted)
                    .Select(order => order.ProFormaNumber),
                StringComparer.Ordinal);

            List<InvoicedPerformanceRow> invoiced = [];
            List<InvoicedPerformanceRow> credited = [];
            foreach (Domain.Sales.Invoices.Invoice invoice in await invoices.ListForCompanyAsync(companyId, cancellationToken).ConfigureAwait(false))
            {
                if (invoice.GroupDocumentRef is null || !repNumbers.Contains(invoice.GroupDocumentRef))
                {
                    continue;
                }

                DateTimeOffset posted = invoice.PostedAt ?? invoice.CreatedAt;
                if (DateOnly.FromDateTime(posted.UtcDateTime) < from
                    || DateOnly.FromDateTime(posted.UtcDateTime) >= to)
                {
                    continue;
                }

                currency ??= invoice.Currency;
                invoiced.Add(new InvoicedPerformanceRow(invoice.Gross.Amount, 0m));
            }

            foreach (ProFormaCreditNote note in await credits.ListForRepAsync(rep.Id, cancellationToken).ConfigureAwait(false))
            {
                if (note.CompanyId is null || note.CompanyId.Value != companyId
                    || note.Status is not ProFormaStatus.Converted
                    || DateOnly.FromDateTime(note.CapturedAt.UtcDateTime) < from
                    || DateOnly.FromDateTime(note.CapturedAt.UtcDateTime) >= to)
                {
                    continue;
                }

                currency ??= note.Currency;
                credited.Add(new InvoicedPerformanceRow(note.Gross.Amount, 0m));
            }

            RepPerformanceFigures figures = calculator.Calculate(new RepPerformanceInputs(
                rows, invoiced, credited, IncludeMargin: false));

            int version = (await snapshots.ListVersionsAsync(rep.Id, companyId, from, cancellationToken).ConfigureAwait(false))
                .Select(snapshot => snapshot.Version)
                .DefaultIfEmpty(0)
                .Max() + 1;

            snapshots.Add(RepPerformanceSnapshot.Snapshot(
                tenant.TenantId, tenant.StoreId, rep.Id, companyId, from, figures,
                version, version == 1 ? "scheduled close" : "recomputation",
                clock.UtcNow, currency ?? "ZAR"));
            snapshotted++;
        }

        return snapshotted;
    }
}
