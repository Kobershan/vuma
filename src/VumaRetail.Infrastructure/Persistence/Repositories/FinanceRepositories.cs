using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Domain.Finance;

namespace VumaRetail.Infrastructure.Persistence.Repositories;

/// <summary>EF Core implementation of <see cref="IAccountRepository"/>.</summary>
/// <param name="context">The database context.</param>
public sealed class AccountRepository(VumaRetailDbContext context) : IAccountRepository
{
    /// <inheritdoc />
    public Task<Account?> FindByIdAsync(Guid accountId, CancellationToken cancellationToken = default)
        => context.Accounts.FirstOrDefaultAsync(account => account.Id == accountId, cancellationToken);

    /// <inheritdoc />
    public Task<Account?> FindByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        string normalized = code.Trim();

        return context.Accounts.FirstOrDefaultAsync(account => account.Code == normalized, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Account?> FindControlAccountAsync(
        ControlAccountType controlAccountType, CancellationToken cancellationToken = default)
        => context.Accounts
            .FirstOrDefaultAsync(account => account.ControlAccountType == controlAccountType, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Account>> ListControlAccountsAsync(CancellationToken cancellationToken = default)
        => await context.Accounts
            .AsNoTracking()
            .Where(account => account.ControlAccountType != ControlAccountType.None)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Account>> ListAllAsync(CancellationToken cancellationToken = default)
        => await context.Accounts
            .AsNoTracking()
            .OrderBy(account => account.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(Account account) => context.Accounts.Add(account);
}

/// <summary>EF Core implementation of <see cref="IAccountingPeriodRepository"/>.</summary>
/// <param name="context">The database context.</param>
public sealed class AccountingPeriodRepository(VumaRetailDbContext context) : IAccountingPeriodRepository
{
    /// <inheritdoc />
    public Task<AccountingPeriod?> FindByIdAsync(Guid periodId, CancellationToken cancellationToken = default)
        => context.AccountingPeriods.FirstOrDefaultAsync(period => period.Id == periodId, cancellationToken);

    /// <inheritdoc />
    public Task<AccountingPeriod?> FindOpenPeriodForDateAsync(DateOnly date, CancellationToken cancellationToken = default)
        => context.AccountingPeriods.FirstOrDefaultAsync(
            period => period.Status == PeriodStatus.Open && period.PeriodStart <= date && period.PeriodEnd >= date,
            cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<AccountingPeriod>> ListOpenAsync(CancellationToken cancellationToken = default)
        => await context.AccountingPeriods
            .Where(period => period.Status == PeriodStatus.Open)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(AccountingPeriod period) => context.AccountingPeriods.Add(period);
}

/// <summary>EF Core implementation of <see cref="IJournalRepository"/>.</summary>
/// <param name="context">The database context.</param>
public sealed class JournalRepository(VumaRetailDbContext context) : IJournalRepository
{
    /// <inheritdoc />
    public Task<Journal?> FindByIdAsync(Guid journalId, CancellationToken cancellationToken = default)
        => context.Journals
            .Include(journal => journal.Lines)
            .FirstOrDefaultAsync(journal => journal.Id == journalId, cancellationToken);

    /// <inheritdoc />
    public async Task<decimal> GetAccountBalanceAsync(
        Guid accountId, DateOnly asOf, CancellationToken cancellationToken = default)
    {
        // JournalLine.SignedAmount is a computed CLR property, not a mapped column, so the lines are
        // pulled into memory (a per-account slice, never the whole ledger) and summed there.
        List<JournalLine> lines = await context.JournalLines
            .AsNoTracking()
            .Where(line => line.AccountId == accountId)
            .Join(
                context.Journals.AsNoTracking(),
                line => line.JournalId,
                journal => journal.Id,
                (line, journal) => new { line, journal })
            .Where(joined => DateOnly.FromDateTime(joined.journal.PostedAt.UtcDateTime) <= asOf)
            .Select(joined => joined.line)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return lines.Select(line => line.SignedAmount).DefaultIfEmpty(0m).Sum();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Journal>> ListRecentAsync(int limit, CancellationToken cancellationToken = default)
        => await context.Journals
            .AsNoTracking()
            .Include(journal => journal.Lines)
            .OrderByDescending(journal => journal.PostedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(Journal journal) => context.Journals.Add(journal);
}

/// <summary>EF Core implementation of <see cref="IPostingRuleRepository"/>.</summary>
/// <param name="context">The database context.</param>
public sealed class PostingRuleRepository(VumaRetailDbContext context) : IPostingRuleRepository
{
    /// <inheritdoc />
    public Task<PostingRule?> FindActiveByEventTypeAsync(string eventType, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);

        string normalized = eventType.Trim();

        return context.PostingRules
            .Include(rule => rule.Lines)
            .Where(rule => rule.EventType == normalized && rule.IsActive)
            .OrderByDescending(rule => rule.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public void Add(PostingRule rule) => context.PostingRules.Add(rule);
}

/// <summary>EF Core implementation of <see cref="IArInvoiceRepository"/>.</summary>
/// <param name="context">The database context.</param>
public sealed class ArInvoiceRepository(VumaRetailDbContext context) : IArInvoiceRepository
{
    /// <inheritdoc />
    public Task<ArInvoice?> FindByIdAsync(Guid invoiceId, CancellationToken cancellationToken = default)
        => context.ArInvoices
            .Include(invoice => invoice.Lines)
            .FirstOrDefaultAsync(invoice => invoice.Id == invoiceId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ArInvoice>> ListOpenAsync(CancellationToken cancellationToken = default)
        => await context.ArInvoices
            .Include(invoice => invoice.Lines)
            .Where(invoice => invoice.Status == DocumentStatus.Posted)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(ArInvoice invoice) => context.ArInvoices.Add(invoice);
}

/// <summary>EF Core implementation of <see cref="IArReceiptRepository"/>.</summary>
/// <param name="context">The database context.</param>
public sealed class ArReceiptRepository(VumaRetailDbContext context) : IArReceiptRepository
{
    /// <inheritdoc />
    public Task<ArReceipt?> FindByIdAsync(Guid receiptId, CancellationToken cancellationToken = default)
        => context.ArReceipts
            .Include(receipt => receipt.Allocations)
            .FirstOrDefaultAsync(receipt => receipt.Id == receiptId, cancellationToken);

    /// <inheritdoc />
    public void Add(ArReceipt receipt) => context.ArReceipts.Add(receipt);
}

/// <summary>EF Core implementation of <see cref="IApInvoiceRepository"/>.</summary>
/// <param name="context">The database context.</param>
public sealed class ApInvoiceRepository(VumaRetailDbContext context) : IApInvoiceRepository
{
    /// <inheritdoc />
    public Task<ApInvoice?> FindByIdAsync(Guid invoiceId, CancellationToken cancellationToken = default)
        => context.ApInvoices
            .Include(invoice => invoice.Lines)
            .FirstOrDefaultAsync(invoice => invoice.Id == invoiceId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ApInvoice>> ListOpenAsync(CancellationToken cancellationToken = default)
        => await context.ApInvoices
            .Include(invoice => invoice.Lines)
            .Where(invoice => invoice.Status == DocumentStatus.Posted)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(ApInvoice invoice) => context.ApInvoices.Add(invoice);
}

/// <summary>EF Core implementation of <see cref="IApPaymentRepository"/>.</summary>
/// <param name="context">The database context.</param>
public sealed class ApPaymentRepository(VumaRetailDbContext context) : IApPaymentRepository
{
    /// <inheritdoc />
    public Task<ApPayment?> FindByIdAsync(Guid paymentId, CancellationToken cancellationToken = default)
        => context.ApPayments
            .Include(payment => payment.Allocations)
            .FirstOrDefaultAsync(payment => payment.Id == paymentId, cancellationToken);

    /// <inheritdoc />
    public void Add(ApPayment payment) => context.ApPayments.Add(payment);
}

/// <summary>EF Core implementation of <see cref="IBankAccountRepository"/>.</summary>
/// <param name="context">The database context.</param>
public sealed class BankAccountRepository(VumaRetailDbContext context) : IBankAccountRepository
{
    /// <inheritdoc />
    public Task<BankAccount?> FindByIdAsync(Guid bankAccountId, CancellationToken cancellationToken = default)
        => context.BankAccounts.FirstOrDefaultAsync(account => account.Id == bankAccountId, cancellationToken);

    /// <inheritdoc />
    public Task<BankAccount?> FindByGlAccountIdAsync(Guid glAccountId, CancellationToken cancellationToken = default)
        => context.BankAccounts.FirstOrDefaultAsync(account => account.GlAccountId == glAccountId, cancellationToken);

    /// <inheritdoc />
    public void Add(BankAccount account) => context.BankAccounts.Add(account);
}

/// <summary>EF Core implementation of <see cref="IBankStatementLineRepository"/>.</summary>
/// <param name="context">The database context.</param>
public sealed class BankStatementLineRepository(VumaRetailDbContext context) : IBankStatementLineRepository
{
    /// <inheritdoc />
    public Task<BankStatementLine?> FindByIdAsync(Guid lineId, CancellationToken cancellationToken = default)
        => context.BankStatementLines.FirstOrDefaultAsync(line => line.Id == lineId, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(
        Guid bankAccountId, string externalReference, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalReference);

        string normalized = externalReference.Trim();

        return context.BankStatementLines.AnyAsync(
            line => line.BankAccountId == bankAccountId && line.ExternalReference == normalized,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<decimal> GetReconciledBalanceAsync(
        Guid bankAccountId, CancellationToken cancellationToken = default)
    {
        List<BankStatementLine> lines = await context.BankStatementLines
            .AsNoTracking()
            .Where(line => line.BankAccountId == bankAccountId && line.MatchedJournalLineId != null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return lines.Select(line => line.Amount.Amount).DefaultIfEmpty(0m).Sum();
    }

    /// <inheritdoc />
    public void Add(BankStatementLine line) => context.BankStatementLines.Add(line);
}

/// <summary>EF Core implementation of <see cref="ITaxRuleRepository"/>.</summary>
/// <param name="context">The database context.</param>
public sealed class TaxRuleRepository(VumaRetailDbContext context) : ITaxRuleRepository
{
    /// <inheritdoc />
    public Task<TaxRule?> FindByIdAsync(Guid taxRuleId, CancellationToken cancellationToken = default)
        => context.TaxRules.FirstOrDefaultAsync(rule => rule.Id == taxRuleId, cancellationToken);

    /// <inheritdoc />
    public Task<TaxRule?> FindEffectiveAsync(string code, DateOnly asOf, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        string normalized = code.Trim().ToUpperInvariant();

        return context.TaxRules
            .Where(rule => rule.Code == normalized
                && rule.IsActive
                && rule.EffectiveFrom <= asOf
                && (rule.EffectiveTo == null || rule.EffectiveTo >= asOf))
            .OrderByDescending(rule => rule.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public void Add(TaxRule rule) => context.TaxRules.Add(rule);
}

/// <summary>EF Core implementation of <see cref="IReconciliationVarianceFlagRepository"/>.</summary>
/// <param name="context">The database context.</param>
public sealed class ReconciliationVarianceFlagRepository(VumaRetailDbContext context)
    : IReconciliationVarianceFlagRepository
{
    /// <inheritdoc />
    public void Add(ReconciliationVarianceFlag flag) => context.ReconciliationVarianceFlags.Add(flag);
}

/// <summary>
/// EF Core implementation of <see cref="IDocumentNumberSequence"/> over
/// <see cref="Domain.Finance.DocumentNumberCounter"/>.
/// </summary>
/// <param name="context">The database context.</param>
/// <param name="tenant">The ambient tenant.</param>
/// <remarks>
/// See <see cref="Domain.Finance.DocumentNumberCounter"/>'s remarks for why this locks an existing row
/// with <c>SELECT ... FOR UPDATE</c> inside the pipeline's own transaction rather than opening and
/// committing one of its own — a repository may not call <c>SaveChanges</c> (the architecture rule
/// that keeps every write on the pipeline's commit). The lock is held until the command's own commit,
/// so a second concurrent request for the same series blocks rather than racing, and the whole
/// increment rolls back with the rest of the command if it fails — gap-free on both counts. The first
/// number of a brand-new series is the one gap in that guarantee: two commands racing to create the
/// same series for the first time both insert, and the loser's commit fails on the unique index rather
/// than blocking — a documented, narrow, one-time-per-series edge case (see <c>docs/PROGRESS.md</c> §3)
/// rather than one worth a second round trip on every ordinary posting.
/// </remarks>
public sealed class DocumentNumberSequence(VumaRetailDbContext context, ITenantContext tenant)
    : IDocumentNumberSequence
{
    /// <inheritdoc />
    public async Task<string> NextAsync(string series, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(series);

        string normalized = series.Trim().ToUpperInvariant();

        DocumentNumberCounter? counter = await context.DocumentNumberCounters
            .FromSqlInterpolated(
                $"""
                 SELECT * FROM finance.document_number_counters
                 WHERE tenant_id = {tenant.TenantId} AND series = {normalized}
                 FOR UPDATE
                 """)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (counter is null)
        {
            counter = DocumentNumberCounter.Start(tenant.TenantId, normalized);
            context.DocumentNumberCounters.Add(counter);
        }

        long issued = counter.Advance();

        return $"{normalized}-{issued:D6}";
    }
}
