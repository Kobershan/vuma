using ZenithRetail.Application.Abstractions;

namespace ZenithRetail.IntegrationTests.Harness;

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

    private sealed class Scope(TestTenantContext owner) : IDisposable
    {
        public void Dispose() => owner._bypassDepth--;
    }
}
