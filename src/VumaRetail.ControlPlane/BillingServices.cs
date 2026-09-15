namespace VumaRetail.ControlPlane;

public sealed record BillingPlan(string Code, decimal MonthlyPrice, long IncludedTransactions,
    decimal TransactionOveragePrice);

public sealed record UsageRollup(string TenantId, string NodeId, DateOnly Period, long Transactions,
    long ActiveUsers, long Terminals, long StorageBytes, IReadOnlyDictionary<string, long> ModuleUsage);

public sealed record BillingUsage(long Transactions, long ActiveUsers, long Terminals, long StorageBytes,
    IReadOnlyDictionary<string, long> ModuleUsage);

public sealed record BillingCharge(decimal BaseAmount, decimal OverageAmount, decimal TotalAmount);

public sealed record PaymentMethodToken(string Id, string Gateway, string Token, DateOnly? ExpiresOn,
    bool IsFallback);

public sealed record DunningNotice(Guid Id, DateOnly DueOn, bool Delivered);

/// <summary>Aggregates vendor usage while rejecting duplicate node-period rollups.</summary>
public sealed class UsageRollupAggregator
{
    private readonly Dictionary<(string NodeId, DateOnly Period), UsageRollup> _rollups = [];

    public bool Add(UsageRollup rollup)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rollup.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(rollup.NodeId);
        if (rollup.Transactions < 0 || rollup.ActiveUsers < 0 || rollup.Terminals < 0 || rollup.StorageBytes < 0
            || rollup.ModuleUsage.Any(x => string.IsNullOrWhiteSpace(x.Key) || x.Value < 0))
            throw new ArgumentOutOfRangeException(nameof(rollup));
        return _rollups.TryAdd((rollup.NodeId, rollup.Period), rollup);
    }

    public BillingUsage ForTenant(string tenantId, DateOnly from, DateOnly through)
    {
        UsageRollup[] rows = _rollups.Values.Where(x => x.TenantId == tenantId && x.Period >= from && x.Period <= through).ToArray();
        Dictionary<string, long> modules = new(StringComparer.Ordinal);
        foreach (UsageRollup row in rows)
            foreach ((string module, long count) in row.ModuleUsage)
                modules[module] = modules.GetValueOrDefault(module) + count;
        return new BillingUsage(rows.Sum(x => x.Transactions), rows.Sum(x => x.ActiveUsers), rows.Sum(x => x.Terminals),
            rows.Sum(x => x.StorageBytes), modules);
    }
}

public static class BillingCalculator
{
    public static BillingCharge Calculate(BillingPlan plan, BillingUsage usage)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(usage);
        long excess = Math.Max(0, usage.Transactions - plan.IncludedTransactions);
        decimal overage = excess * plan.TransactionOveragePrice;
        return new(plan.MonthlyPrice, overage, plan.MonthlyPrice + overage);
    }

    public static decimal Prorate(decimal amount, DateOnly cycleStart, DateOnly cycleEnd, DateOnly changeDate)
    {
        if (cycleEnd <= cycleStart || changeDate < cycleStart || changeDate > cycleEnd)
            throw new ArgumentOutOfRangeException(nameof(changeDate));
        int totalDays = cycleEnd.DayNumber - cycleStart.DayNumber;
        int remainingDays = cycleEnd.DayNumber - changeDate.DayNumber;
        return decimal.Round(amount * remainingDays / totalDays, 2, MidpointRounding.AwayFromZero);
    }
}

public sealed class DunningTracker
{
    private readonly List<DunningNotice> _notices = [];

    public IReadOnlyList<DunningNotice> Notices => _notices;
    public bool IsPaused => _notices.Any(x => !x.Delivered);

    public void RecordNotice(DunningNotice notice)
    {
        if (_notices.Any(x => x.Id == notice.Id)) return;
        _notices.Add(notice);
    }

    public bool CanAdvance(DateOnly today) => !IsPaused && _notices.Any(x => x.DueOn <= today);
}
