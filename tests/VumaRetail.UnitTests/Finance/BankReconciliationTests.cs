using VumaRetail.Domain.Finance;
using VumaRetail.Domain.Primitives;
using static VumaRetail.UnitTests.Finance.FinanceTestContext;

namespace VumaRetail.UnitTests.Finance;

/// <summary>
/// The banking slice's reconciliation primitive: a statement line's matched state.
/// </summary>
/// <remarks>
/// There is deliberately no reconciliation-run entity — matching a line <em>is</em> reconciling it,
/// and the bank control account's variance check reads the sum of matched lines. That design choice
/// is what these tests are really protecting: if a separate run entity ever appears, the reconciled
/// balance has two sources and they will disagree.
/// </remarks>
public sealed class BankReconciliationTests
{
    private static readonly Guid BankAccountId = UuidV7.NewGuid();

    private static BankStatementLine Line(decimal amount, string reference = "TXN-1")
        => BankStatementLine.Import(
            TenantId, BankAccountId, Today, "Card settlement", Rand(amount), reference);

    [Fact]
    public void An_imported_line_starts_unmatched()
    {
        BankStatementLine line = Line(500m);

        line.IsMatched.Should().BeFalse();
        line.MatchedJournalLineId.Should().BeNull();
        line.MatchedAt.Should().BeNull();
        line.MatchedBy.Should().BeNull();
    }

    [Fact]
    public void Matching_a_line_records_who_matched_it_and_when()
    {
        BankStatementLine line = Line(500m);
        Guid journalLineId = UuidV7.NewGuid();

        line.Match(journalLineId, "user:accountant", Now);

        line.IsMatched.Should().BeTrue();
        line.MatchedJournalLineId.Should().Be(journalLineId);
        line.MatchedBy.Should().Be("user:accountant");
        line.MatchedAt.Should().Be(Now);
    }

    [Fact]
    public void Unmatching_a_line_returns_it_to_exactly_its_unmatched_state()
    {
        // Not merely "IsMatched is false again" — a leftover MatchedBy or MatchedAt would make the
        // audit trail claim a reconciliation that has been undone.
        BankStatementLine line = Line(500m);
        line.Match(UuidV7.NewGuid(), "user:accountant", Now);

        line.Unmatch();

        line.IsMatched.Should().BeFalse();
        line.MatchedJournalLineId.Should().BeNull();
        line.MatchedBy.Should().BeNull();
        line.MatchedAt.Should().BeNull();
    }

    [Fact]
    public void A_line_can_be_rematched_to_a_different_journal_line_after_a_wrong_match()
    {
        BankStatementLine line = Line(500m);
        line.Match(UuidV7.NewGuid(), "user:accountant", Now);
        line.Unmatch();
        Guid correct = UuidV7.NewGuid();

        line.Match(correct, "user:supervisor", Now.AddHours(1));

        line.MatchedJournalLineId.Should().Be(correct);
        line.MatchedBy.Should().Be("user:supervisor");
    }

    [Fact]
    public void Money_out_is_a_negative_amount_on_the_same_signed_column()
    {
        // One signed column rather than a debit/credit pair: a bank statement is a single running
        // balance, and splitting it would need a rule for which side a zero goes on.
        BankStatementLine paidOut = Line(-250m, "TXN-OUT");

        paidOut.Amount.Should().Be(Rand(-250m));
        paidOut.Amount.IsNegative.Should().BeTrue();
    }

    [Fact]
    public void A_line_without_the_banks_own_reference_is_refused()
    {
        // The external reference is what stops the same statement being imported twice.
        Action importing = () => BankStatementLine.Import(
            TenantId, BankAccountId, Today, "No reference", Rand(100m), "  ");

        importing.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_bank_account_names_the_gl_account_it_reconciles_to()
    {
        Guid glAccountId = UuidV7.NewGuid();

        BankAccount account = BankAccount.Open(TenantId, glAccountId, "Cheque account", "62001234567", "ZAR");

        account.GlAccountId.Should().Be(glAccountId);
        account.Currency.Should().Be("ZAR");
        account.IsActive.Should().BeTrue();
    }
}
