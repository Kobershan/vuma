using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Finance;

/// <summary>
/// One node of the chart of accounts (ADR-016).
/// </summary>
/// <remarks>
/// <para>
/// Tenant-wide — <see cref="Entity.StoreId"/> is always <c>null</c> on an account. A store's own
/// trading is distinguished on a journal <em>line</em> by that line's own <c>StoreId</c>, not by
/// having a different chart of accounts per store; one tenant has one chart.
/// </para>
/// <para>
/// This is the type CLAUDE.md §7 rule 12 forbids any module outside Finance from naming. Nothing
/// about that rule is enforced by keeping this class free of public mutators of its own — it is
/// enforced by <c>FinanceRulesTests</c> refusing any assembly outside the Finance module a type
/// dependency on it at all.
/// </para>
/// </remarks>
[Replicated(ReplicationScope.CloudToStore, ConflictPolicy.CloudWins)]
public sealed class Account : Entity
{
    private Account(Guid tenantId)
        : base(tenantId)
    {
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private Account()
    {
    }

    /// <summary>The account code, unique per tenant — for example <c>1000</c> or <c>4000-JHB</c>.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>The account's name, as it appears on a trial balance.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Which of the five classes this account belongs to.</summary>
    public AccountType Type { get; private set; }

    /// <summary>The parent account, for a hierarchy — or <c>null</c> at the top of one.</summary>
    public Guid? ParentAccountId { get; private set; }

    /// <summary>
    /// Marks this as the one account a sub-ledger reconciles to. See <see cref="ControlAccountType"/>.
    /// </summary>
    public ControlAccountType ControlAccountType { get; private set; }

    /// <summary>The ISO 4217 currency this account is denominated in.</summary>
    public string Currency { get; private set; } = string.Empty;

    /// <summary>False once an account is retired. A retired account may still be read, never posted to.</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>Which side of a journal line increases this account's balance, from <see cref="Type"/>.</summary>
    public NormalBalance NormalBalance => Type is AccountType.Asset or AccountType.Expense
        ? NormalBalance.Debit
        : NormalBalance.Credit;

    /// <summary>Opens a new account.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="code">The account code, unique per tenant.</param>
    /// <param name="name">The account's name.</param>
    /// <param name="type">Which of the five classes it belongs to.</param>
    /// <param name="currency">The ISO 4217 currency it is denominated in.</param>
    /// <param name="controlAccountType">What sub-ledger, if any, this reconciles to.</param>
    /// <param name="parentAccountId">The parent account, for a hierarchy.</param>
    public static Account Open(
        Guid tenantId,
        string code,
        string name,
        AccountType type,
        string currency,
        ControlAccountType controlAccountType = Finance.ControlAccountType.None,
        Guid? parentAccountId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);

        return new Account(tenantId)
        {
            Code = code.Trim(),
            Name = name.Trim(),
            Type = type,
            Currency = currency.Trim().ToUpperInvariant(),
            ControlAccountType = controlAccountType,
            ParentAccountId = parentAccountId,
            IsActive = true,
        };
    }

    /// <summary>Retires the account. Existing history is untouched; nothing may post to it again.</summary>
    public void Deactivate() => IsActive = false;

    /// <summary>Reinstates a retired account.</summary>
    public void Reactivate() => IsActive = true;

    /// <summary>Renames the account. The code, which documents reference, does not change.</summary>
    /// <param name="name">The new name.</param>
    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
    }
}
