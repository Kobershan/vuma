using VumaRetail.Domain.FieldSales;

namespace VumaRetail.Application.FieldSales;

/// <summary>
/// Folds pro formas and invoices into one month's figures (Stage 14b).
/// </summary>
/// <remarks>
/// The Scorecard split (entity never queries): this calculator takes plain rows in and returns
/// figures out. Repository-free, clock-free — the caller stamps time and currency.
/// </remarks>
public interface IRepPerformanceCalculator
{
    /// <summary>Computes one rep's figures for one closed month.</summary>
    RepPerformanceFigures Calculate(RepPerformanceInputs inputs);
}

/// <summary>Plain rows in for one rep, one company (or group), one closed month.</summary>
/// <param name="ProFormas">Every pro forma captured in the month (any status).</param>
/// <param name="Invoices">Every invoice posted in the month for converted orders.</param>
/// <param name="CreditNotes">Every applied credit in the month.</param>
/// <param name="IncludeMargin">Whether cost was visible when snapshotting.</param>
public sealed record RepPerformanceInputs(
    IReadOnlyList<ProFormaPerformanceRow> ProFormas,
    IReadOnlyList<InvoicedPerformanceRow> Invoices,
    IReadOnlyList<InvoicedPerformanceRow> CreditNotes,
    bool IncludeMargin);

/// <summary>One pro forma's contribution: status and quoted gross.</summary>
public sealed record ProFormaPerformanceRow(ProFormaStatus Status, decimal Gross, Guid PartnerId);

/// <summary>One invoice's contribution: gross and margin (zero without visibility).</summary>
public sealed record InvoicedPerformanceRow(decimal Gross, decimal Margin);

/// <summary>The Scorecard-shaped fold: sums by status, net as invoiced less credited.</summary>
public sealed class RepPerformanceCalculator : IRepPerformanceCalculator
{
    /// <inheritdoc />
    public RepPerformanceFigures Calculate(RepPerformanceInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        decimal captured = 0m;
        decimal converted = 0m;
        decimal rejected = 0m;
        decimal expired = 0m;
        int count = 0;
        HashSet<Guid> customers = [];

        foreach (ProFormaPerformanceRow row in inputs.ProFormas)
        {
            count++;
            captured += row.Gross;
            customers.Add(row.PartnerId);

            switch (row.Status)
            {
                case ProFormaStatus.Converted:
                    converted += row.Gross;
                    break;
                case ProFormaStatus.Rejected:
                    rejected += row.Gross;
                    break;
                case ProFormaStatus.Expired:
                    expired += row.Gross;
                    break;
                default:
                    break;
            }
        }

        decimal invoiced = inputs.Invoices.Sum(invoice => invoice.Gross);
        decimal credited = inputs.CreditNotes.Sum(note => note.Gross);
        decimal? margin = inputs.IncludeMargin
            ? inputs.Invoices.Sum(invoice => invoice.Margin)
            : null;

        return new RepPerformanceFigures(
            count, captured, converted, rejected, expired,
            invoiced, credited, margin, customers.Count);
    }
}
