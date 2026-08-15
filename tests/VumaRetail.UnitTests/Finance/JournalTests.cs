using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Finance;
using VumaRetail.Domain.Primitives;
using static VumaRetail.UnitTests.Finance.FinanceTestContext;

namespace VumaRetail.UnitTests.Finance;

/// <summary>
/// The general ledger's own invariants (ADR-016, CLAUDE.md §7 rule 7).
/// </summary>
/// <remarks>
/// These are asserted against the entity rather than through EF because they must hold before a
/// journal ever reaches the database — "rejected before it reaches the database" is the acceptance
/// criterion in <c>docs/stages/STAGE-07-finance.md</c>, and a test that needs a database to prove it
/// would be proving a weaker claim.
/// </remarks>
public sealed class JournalTests
{
    private static readonly Guid Cash = UuidV7.NewGuid();
    private static readonly Guid Sales = UuidV7.NewGuid();
    private static readonly Guid PeriodId = UuidV7.NewGuid();

    private static Journal PostBalanced(params JournalLineDraft[] lines)
        => Journal.Post(
            TenantId, StoreId, PeriodId, "JNL-000001", Now, "user:test",
            "manual", "manual", "TEST-1", lines, "A test posting");

    [Fact]
    public void A_journal_whose_debits_and_credits_agree_posts()
    {
        Journal journal = PostBalanced(
            JournalLineDraft.DebitLine(Cash, Rand(115m)),
            JournalLineDraft.CreditLine(Sales, Rand(115m)));

        journal.Lines.Should().HaveCount(2);
        journal.Lines.Sum(line => line.SignedAmount).Should().Be(0m);
    }

    [Fact]
    public void A_journal_whose_debits_and_credits_disagree_is_rejected_not_silently_balanced()
    {
        Action posting = () => PostBalanced(
            JournalLineDraft.DebitLine(Cash, Rand(115m)),
            JournalLineDraft.CreditLine(Sales, Rand(100m)));

        posting.Should().Throw<JournalNotBalancedException>()
            .Which.VariancePerCurrency["ZAR"].Should().Be(15m);
    }

    [Fact]
    public void A_journal_carrying_two_currencies_must_balance_within_each_of_them()
    {
        // Rule 4's consequence: a journal mixing currencies is two balanced sub-journals, not one
        // journal whose currencies net off against each other. These four lines sum to zero if you
        // ignore the currency column, which is exactly the mistake being guarded against.
        Action posting = () => PostBalanced(
            JournalLineDraft.DebitLine(Cash, new Money(100m, "ZAR")),
            JournalLineDraft.CreditLine(Sales, new Money(100m, "USD")));

        posting.Should().Throw<JournalNotBalancedException>()
            .Which.VariancePerCurrency.Should().HaveCount(2);
    }

    [Fact]
    public void A_journal_balanced_separately_in_each_currency_posts()
    {
        Journal journal = PostBalanced(
            JournalLineDraft.DebitLine(Cash, new Money(100m, "ZAR")),
            JournalLineDraft.CreditLine(Sales, new Money(100m, "ZAR")),
            JournalLineDraft.DebitLine(Cash, new Money(50m, "USD")),
            JournalLineDraft.CreditLine(Sales, new Money(50m, "USD")));

        journal.Lines.Should().HaveCount(4);
    }

    [Fact]
    public void A_journal_with_no_lines_is_rejected()
    {
        Action posting = () => PostBalanced();

        posting.Should().Throw<JournalHasNoLinesException>();
    }

    [Fact]
    public void A_line_carrying_neither_a_debit_nor_a_credit_is_rejected_with_a_coded_error()
    {
        // Manual journals arrive from the API, so this has to be a coded domain failure. It was an
        // unhandled InvalidOperationException from inside the balance loop until the side check was
        // moved ahead of the arithmetic — a 500 for what is plainly a caller mistake.
        Action posting = () => PostBalanced(new JournalLineDraft(Cash, null, null));

        posting.Should().Throw<JournalLineSideException>()
            .Which.Code.Should().Be("FINANCE_JOURNAL_LINE_SIDE");
    }

    [Fact]
    public void A_line_carrying_both_a_debit_and_a_credit_is_rejected_for_that_reason()
    {
        // Previously this surfaced as JournalNotBalancedException, because the balance loop counted
        // such a line as a debit and only noticed the totals disagreed. The complaint has to name
        // the actual mistake, or the caller goes looking for a missing counter-entry that isn't the
        // problem.
        Action posting = () => PostBalanced(new JournalLineDraft(Cash, Rand(10m), Rand(10m)));

        posting.Should().Throw<JournalLineSideException>()
            .WithMessage("*both a debit and a credit*");
    }

    [Fact]
    public void A_rejected_line_is_identified_by_its_position_in_the_posting()
    {
        Action posting = () => PostBalanced(
            JournalLineDraft.DebitLine(Cash, Rand(115m)),
            JournalLineDraft.CreditLine(Sales, Rand(115m)),
            new JournalLineDraft(Cash, null, null));

        posting.Should().Throw<JournalLineSideException>().WithMessage("Journal line 3 *");
    }

    [Fact]
    public void Lines_are_numbered_in_the_order_they_were_supplied()
    {
        Journal journal = PostBalanced(
            JournalLineDraft.DebitLine(Cash, Rand(115m)),
            JournalLineDraft.CreditLine(Sales, Rand(115m)));

        journal.Lines.Select(line => line.LineNumber).Should().Equal(1, 2);
    }

    [Fact]
    public void A_debit_line_exposes_its_amount_as_money_and_leaves_the_credit_null()
    {
        // The mapping stores four plain columns (ADR-067); Debit/Credit are computed back from them.
        // If that round trip ever breaks, every balance in the system is wrong, so it is asserted
        // rather than assumed.
        Journal journal = PostBalanced(
            JournalLineDraft.DebitLine(Cash, Rand(115m)),
            JournalLineDraft.CreditLine(Sales, Rand(115m)));

        JournalLine debit = journal.Lines[0];
        debit.Debit.Should().Be(Rand(115m));
        debit.Credit.Should().BeNull();
        debit.DebitAmount.Should().Be(115m);
        debit.DebitCurrency.Should().Be("ZAR");
        debit.CreditAmount.Should().BeNull();
        debit.SignedAmount.Should().Be(115m);

        JournalLine credit = journal.Lines[1];
        credit.Credit.Should().Be(Rand(115m));
        credit.Debit.Should().BeNull();
        credit.SignedAmount.Should().Be(-115m);
    }

    [Fact]
    public void A_reversal_is_an_equal_and_opposite_journal_that_links_back_and_leaves_the_original_untouched()
    {
        Journal original = PostBalanced(
            JournalLineDraft.DebitLine(Cash, Rand(115m)),
            JournalLineDraft.CreditLine(Sales, Rand(115m)));

        Journal reversal = Journal.Post(
            TenantId, StoreId, PeriodId, "JNL-000002", Now, "user:test",
            "manual", "manual", "TEST-1",
            [
                JournalLineDraft.CreditLine(Cash, Rand(115m)),
                JournalLineDraft.DebitLine(Sales, Rand(115m)),
            ],
            "Reversal of JNL-000001",
            reversalOfJournalId: original.Id);

        reversal.ReversalOfJournalId.Should().Be(original.Id);
        reversal.Lines.Sum(line => line.SignedAmount).Should().Be(0m);

        // Equal and opposite: line for line, the reversal's signed amounts negate the original's.
        reversal.Lines.Select(line => line.SignedAmount)
            .Should().Equal([.. original.Lines.Select(line => -line.SignedAmount)]);

        original.ReversalOfJournalId.Should().BeNull();
        original.Lines.Should().HaveCount(2);
        original.Lines[0].SignedAmount.Should().Be(115m);
    }

    [Fact]
    public void A_posted_journal_and_its_lines_are_immutable_records()
    {
        // IImmutableRecord is what the persistence layer's guard keys off to refuse any UPDATE or
        // DELETE; the guard itself is asserted in the integration tests. This asserts the marker is
        // present, which is the half that lives in the domain.
        typeof(Journal).Should().BeAssignableTo<IImmutableRecord>();
        typeof(JournalLine).Should().BeAssignableTo<IImmutableRecord>();
    }

    [Fact]
    public void Analysis_dimensions_ride_on_the_line_the_posting_put_them_on()
    {
        Guid department = UuidV7.NewGuid();
        Guid costCentre = UuidV7.NewGuid();

        Journal journal = PostBalanced(
            new JournalLineDraft(Cash, Rand(115m), null, "in", department, costCentre),
            JournalLineDraft.CreditLine(Sales, Rand(115m)));

        journal.Lines[0].DepartmentId.Should().Be(department);
        journal.Lines[0].CostCentreId.Should().Be(costCentre);
        journal.Lines[1].DepartmentId.Should().BeNull();
    }
}
