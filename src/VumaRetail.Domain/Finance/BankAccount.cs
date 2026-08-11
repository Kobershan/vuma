using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Finance;

/// <summary>
/// A bank account the business holds, paired one-to-one with a GL account carrying
/// <see cref="ControlAccountType.Bank"/>.
/// </summary>
/// <remarks>
/// A minimal, real, testable slice (per <c>docs/stages/STAGE-07-finance.md</c>): this module records
/// accounts, imports statement lines and matches them. It does not parse a bank's file format —
/// that is Stage 11's Excel/CSV/PDF ingest pipeline, feeding into
/// <c>ImportBankStatementLinesCommand</c> once a mapping exists.
/// </remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.StoreWins)]
public sealed class BankAccount : Entity
{
    private BankAccount(Guid tenantId)
        : base(tenantId)
    {
    }

    /// <summary>Required by EF Core for materialisation. Do not call from business code.</summary>
    private BankAccount()
    {
    }

    /// <summary>The GL control account this bank account reconciles to.</summary>
    public Guid GlAccountId { get; private set; }

    /// <summary>A label for the account, for example <c>FNB Cheque 62-1234-5678</c>.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>The account number, held as given — never used for authentication.</summary>
    public string AccountNumber { get; private set; } = string.Empty;

    /// <summary>The ISO 4217 currency the account is held in.</summary>
    public string Currency { get; private set; } = string.Empty;

    /// <summary>False once an account is closed. A closed account may still be read, never posted to.</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>Opens a bank account record.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="glAccountId">The GL control account this reconciles to.</param>
    /// <param name="name">A label for the account.</param>
    /// <param name="accountNumber">The account number.</param>
    /// <param name="currency">The ISO 4217 currency it is held in.</param>
    public static BankAccount Open(Guid tenantId, Guid glAccountId, string name, string accountNumber, string currency)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);

        return new BankAccount(tenantId)
        {
            GlAccountId = glAccountId,
            Name = name.Trim(),
            AccountNumber = accountNumber.Trim(),
            Currency = currency.Trim().ToUpperInvariant(),
            IsActive = true,
        };
    }

    /// <summary>Closes the account. Existing history is untouched.</summary>
    public void Close() => IsActive = false;
}
