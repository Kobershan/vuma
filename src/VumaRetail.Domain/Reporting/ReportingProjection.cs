#pragma warning disable CS1591
namespace VumaRetail.Domain.Reporting;

/// <summary>A replayable, company-scoped reporting contribution.</summary>
public sealed record ReportingProjectionEvent(
    Guid EventId,
    Guid CompanyId,
    string Source,
    long Generation,
    string Cursor,
    string Currency,
    IReadOnlyDictionary<string, decimal> Measures);

/// <summary>
/// In-process projection state used by reporting adapters and rebuild tests. Production adapters can
/// persist the same event/checkpoint contract without changing dashboard or export consumers.
/// </summary>
public sealed class ReportingProjection(Guid companyId, string source)
{
    private readonly HashSet<Guid> _eventIds = [];
    private readonly Dictionary<(string Name, string Currency), decimal> _measures = [];
    private long _generation;
    private string _cursor = string.Empty;

    public Guid CompanyId { get; } = companyId == Guid.Empty ? throw new ArgumentException("Company is required.", nameof(companyId)) : companyId;
    public string Source { get; } = string.IsNullOrWhiteSpace(source) ? throw new ArgumentException("Source is required.", nameof(source)) : source.Trim();
    public long Generation => _generation;
    public string Cursor => _cursor;
    public int AppliedEventCount => _eventIds.Count;

    public bool Apply(ReportingProjectionEvent contribution)
    {
        ArgumentNullException.ThrowIfNull(contribution);
        Validate(contribution);
        if (!_eventIds.Add(contribution.EventId))
        {
            return false;
        }

        foreach ((string name, decimal value) in contribution.Measures)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Measure names are required.", nameof(contribution));
            }
            _measures[(name.Trim(), contribution.Currency)] =
                _measures.GetValueOrDefault((name.Trim(), contribution.Currency)) + value;
        }

        _generation = contribution.Generation;
        _cursor = contribution.Cursor;
        return true;
    }

    public void Rebuild(IEnumerable<ReportingProjectionEvent> contributions)
    {
        ArgumentNullException.ThrowIfNull(contributions);
        _eventIds.Clear();
        _measures.Clear();
        _generation = 0;
        _cursor = string.Empty;
        foreach (ReportingProjectionEvent contribution in contributions
            .OrderBy(x => x.Generation).ThenBy(x => x.Cursor, StringComparer.Ordinal).ThenBy(x => x.EventId))
        {
            Apply(contribution);
        }
    }

    public IReadOnlyDictionary<string, decimal> SnapshotMeasures() =>
        _measures.ToDictionary(x => $"{x.Key.Name}|{x.Key.Currency}", x => x.Value, StringComparer.Ordinal);

    private void Validate(ReportingProjectionEvent contribution)
    {
        if (contribution.EventId == Guid.Empty || contribution.CompanyId != CompanyId ||
            !string.Equals(contribution.Source, Source, StringComparison.Ordinal) ||
            contribution.Generation <= 0 || string.IsNullOrWhiteSpace(contribution.Cursor) ||
            string.IsNullOrWhiteSpace(contribution.Currency))
        {
            throw new ArgumentException("The projection contribution is outside this projection scope.", nameof(contribution));
        }
        if (contribution.Generation < _generation ||
            (contribution.Generation == _generation && string.CompareOrdinal(contribution.Cursor, _cursor) < 0))
        {
            throw new InvalidOperationException("A projection cannot move its checkpoint backwards.");
        }
    }
}
