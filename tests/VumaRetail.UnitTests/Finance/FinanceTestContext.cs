using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Domain.Finance;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Finance;

/// <summary>
/// The shared fixtures Stage 07's tests build on: a fixed clock, a fixed tenant, and short
/// constructors for the money and accounts every finance assertion needs.
/// </summary>
/// <remarks>
/// A fixed clock rather than the wall clock throughout. Every question Finance answers — an ageing
/// bucket, a tax rate, whether a period may close — is a question about a date, so a test that
/// cannot pin the date is not testing the rule, it is testing what day it happens to be run on.
/// </remarks>
internal static class FinanceTestContext
{
    /// <summary>The tenant every fixture belongs to.</summary>
    public static readonly Guid TenantId = UuidV7.NewGuid();

    /// <summary>The store every fixture belongs to.</summary>
    public static readonly Guid StoreId = UuidV7.NewGuid();

    /// <summary>The instant the fixed clock reports.</summary>
    public static readonly DateTimeOffset Now = new(2026, 6, 15, 10, 30, 0, TimeSpan.Zero);

    /// <summary><see cref="Now"/> as a date.</summary>
    public static readonly DateOnly Today = DateOnly.FromDateTime(Now.UtcDateTime);

    /// <summary>An amount in rand, the `en-ZA` default currency (CLAUDE.md §9).</summary>
    /// <param name="amount">The amount.</param>
    public static Money Rand(decimal amount) => new(amount, "ZAR");

    /// <summary>An ordinary account.</summary>
    /// <param name="code">The account code.</param>
    /// <param name="type">Which of the five classes it belongs to.</param>
    public static Account Account(string code, AccountType type)
        => Domain.Finance.Account.Open(TenantId, code, $"Account {code}", type, "ZAR");

    /// <summary>An account a sub-ledger reconciles to.</summary>
    /// <param name="code">The account code.</param>
    /// <param name="type">Which of the five classes it belongs to.</param>
    /// <param name="control">The sub-ledger this reconciles to.</param>
    public static Account ControlAccount(string code, AccountType type, ControlAccountType control)
        => Domain.Finance.Account.Open(TenantId, code, $"Account {code}", type, "ZAR", control);

    /// <summary>An open period covering the whole of the fixed clock's month.</summary>
    public static AccountingPeriod OpenPeriod()
        => AccountingPeriod.Open(TenantId, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
}

/// <summary>An <see cref="IClock"/> that reports a fixed instant, and moves only when told to.</summary>
/// <param name="now">The instant to report.</param>
internal sealed class FixedClock(DateTimeOffset now) : IClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow { get; private set; } = now;

    /// <summary>Moves the clock forward.</summary>
    /// <param name="by">How far.</param>
    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}

/// <summary>A tenant context pinned to <see cref="FinanceTestContext.TenantId"/>.</summary>
internal sealed class FixedTenantContext : ITenantContext
{
    /// <inheritdoc />
    public Guid TenantId => FinanceTestContext.TenantId;

    /// <inheritdoc />
    public Guid? StoreId => FinanceTestContext.StoreId;

    /// <inheritdoc />
    public bool IsFilterBypassed => false;

    /// <inheritdoc />
    public void SetTenant(Guid tenantId, Guid? storeId = null)
        => throw new NotSupportedException("The finance tests run under one fixed tenant.");

    /// <inheritdoc />
    public IDisposable BypassTenantFilter(string reason)
        => throw new NotSupportedException("No finance rule needs to cross a tenant boundary.");
}

/// <summary>A principal accessor reporting one known name.</summary>
/// <param name="principal">The name to report.</param>
internal sealed class FixedPrincipal(string principal = "test:accountant") : IPrincipalAccessor
{
    /// <inheritdoc />
    public string Principal => principal;

    /// <inheritdoc />
    public Guid? TerminalId => null;

    /// <inheritdoc />
    public bool IsSystem => principal.StartsWith("system:", StringComparison.Ordinal);
}

/// <summary>A document number sequence issuing predictable, increasing numbers per series.</summary>
internal sealed class CountingDocumentNumbers : IDocumentNumberSequence
{
    private readonly Dictionary<string, int> _counters = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<string> NextAsync(string series, CancellationToken cancellationToken = default)
    {
        int next = _counters.GetValueOrDefault(series) + 1;
        _counters[series] = next;
        return Task.FromResult($"{series}-{next:D6}");
    }
}
