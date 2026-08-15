using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Domain.Licensing;

namespace VumaRetail.IntegrationTests.Harness;

/// <summary>
/// A clock the test moves by hand. The reason <see cref="IClock"/> exists at all.
/// </summary>
/// <param name="start">The instant the clock starts at.</param>
public sealed class TestClock(DateTimeOffset? start = null) : IClock
{
    /// <summary>A fixed, obviously-synthetic instant, so a stamped value is recognisable in a failure.</summary>
    public static readonly DateTimeOffset DefaultStart = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);

    /// <inheritdoc />
    public DateTimeOffset UtcNow { get; private set; } = start ?? DefaultStart;

    /// <summary>Moves the clock forward.</summary>
    /// <param name="amount">How far forward.</param>
    public void Advance(TimeSpan amount) => UtcNow = UtcNow.Add(amount);

    /// <summary>Moves the clock to a specific instant, forwards or backwards.</summary>
    /// <param name="instant">Where to move it to.</param>
    public void MoveTo(DateTimeOffset instant) => UtcNow = instant;
}

/// <summary>A principal the test controls.</summary>
/// <param name="principal">The principal string written to the audit columns.</param>
/// <param name="terminalId">The originating terminal, if any.</param>
/// <param name="isSystem">Whether this is the system acting.</param>
public sealed class TestPrincipalAccessor(
    string principal = "user:test",
    Guid? terminalId = null,
    bool isSystem = false) : IPrincipalAccessor
{
    /// <inheritdoc />
    public string Principal { get; } = principal;

    /// <inheritdoc />
    public Guid? TerminalId { get; } = terminalId;

    /// <inheritdoc />
    public bool IsSystem { get; } = isSystem;
}

/// <summary>
/// An entitlement service a test sets by hand, for the harnesses that are not about licensing.
/// </summary>
/// <remarks>
/// Everything is enabled, every limit is unlimited and the tenant is Normal, unless a test says
/// otherwise. Stage 04b's own tests use the <em>real</em> service over a real lease — a licensing test
/// against this double would assert the double — and every other harness uses this so that adding
/// licensing to the pipeline did not change what those tests are about.
/// </remarks>
public sealed class TestEntitlementService : IEntitlementService, IEnforcementStatusReader
{
    /// <summary>Where the tenant sits. Tests move this to exercise the read-only guard.</summary>
    public EnforcementDecision Decision { get; set; } =
        EnforcementDecision.Normal(TestClock.DefaultStart);

    /// <summary>The plan's quantities.</summary>
    public LicenceLimits Limits { get; set; } = LicenceLimits.Unlimited;

    /// <summary>Modules that are switched off. Empty means everything is on.</summary>
    public HashSet<string> DisabledModules { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public Task<bool> IsModuleEnabledAsync(string module, CancellationToken cancellationToken = default)
        => Task.FromResult(!DisabledModules.Contains(module));

    /// <inheritdoc />
    public Task<LimitCheck> CheckLimitAsync(
        LimitKind kind,
        long proposedValue,
        CancellationToken cancellationToken = default)
    {
        long ceiling = Limits.Ceiling(kind);

        return Task.FromResult(new LimitCheck(
            kind,
            ceiling,
            proposedValue,
            LicenceLimits.IsHard(kind),
            proposedValue > ceiling));
    }

    /// <inheritdoc />
    public Task<EnforcementDecision> CurrentLevel(CancellationToken cancellationToken = default)
        => Task.FromResult(Decision);
}

/// <summary>Usage counters a test sets by hand.</summary>
/// <remarks>
/// Zero everywhere by default. The counts only matter to the two hard-limit checks, and a test that
/// cares about those sets them.
/// </remarks>
public sealed class TestUsageCounterSource : IUsageCounterSource
{
    /// <summary>What the configuration counts should say.</summary>
    public ConfigurationCounts Configuration { get; set; }

    /// <summary>What a day's activity should say.</summary>
    public ActivityCounts Activity { get; set; } = new(
        0,
        0,
        new Dictionary<string, long>(StringComparer.Ordinal),
        0,
        0,
        0,
        0,
        0,
        0);

    /// <summary>What the health counts should say.</summary>
    public HealthCounts Health { get; set; }

    /// <inheritdoc />
    public Task<ConfigurationCounts> CountConfigurationAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Configuration);

    /// <inheritdoc />
    public Task<ActivityCounts> CountActivityAsync(
        DateOnly period,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Activity);

    /// <inheritdoc />
    public Task<HealthCounts> CountHealthAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Health);
}

/// <summary>A tenant context the test sets directly, with no request pipeline in the way.</summary>
public sealed class TestTenantContext : ITenantContext
{
    private int _bypassDepth;

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <inheritdoc />
    public Guid? StoreId { get; private set; }

    /// <inheritdoc />
    public bool IsFilterBypassed => _bypassDepth > 0;

    /// <summary>A context scoped to one tenant, which is the normal case.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="storeId">The store, if the caller is acting in one.</param>
    public static TestTenantContext For(Guid tenantId, Guid? storeId = null)
    {
        TestTenantContext context = new();
        context.SetTenant(tenantId, storeId);
        return context;
    }

    /// <summary>A context with the tenant filter permanently bypassed, for arranging test data.</summary>
    public static TestTenantContext Unfiltered()
    {
        TestTenantContext context = new();
        context._bypassDepth = 1;
        return context;
    }

    /// <inheritdoc />
    public void SetTenant(Guid tenantId, Guid? storeId = null)
    {
        TenantId = tenantId;
        StoreId = storeId;
    }

    /// <inheritdoc />
    public IDisposable BypassTenantFilter(string reason)
    {
        _bypassDepth++;
        return new Scope(this);
    }

    /// <summary>
    /// Closes a bypass opened by <see cref="Unfiltered"/>, so a test can arrange data across tenants
    /// and then exercise the code under the filter it actually runs under in production.
    /// </summary>
    public void EndBypass() => _bypassDepth = 0;

    private sealed class Scope(TestTenantContext owner) : IDisposable
    {
        public void Dispose() => owner._bypassDepth--;
    }
}
