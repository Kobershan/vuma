using NSubstitute;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Domain.Finance;
using VumaRetail.Domain.Primitives;
using VumaRetail.Finance.Posting;
using static VumaRetail.UnitTests.Finance.FinanceTestContext;

namespace VumaRetail.UnitTests.Finance;

/// <summary>
/// The posting rules engine — the mechanism CLAUDE.md §7 rule 12 names (ADR-016).
/// </summary>
/// <remarks>
/// Two realistic event types are exercised, as <c>docs/stages/STAGE-07-finance.md</c> requires:
/// <c>ar.invoice.posted</c>, which Stage 07 raises itself, and <c>pos.sale.tendered</c>, which stands
/// in for the Stage 09 event that does not exist yet. The second one matters more than it looks: it
/// is posted here without a single line of POS code existing, which is the whole claim rule 12 makes
/// — a new event type is rule data, not a code change in Finance or in the producer.
/// </remarks>
public sealed class PostingRuleEngineTests
{
    private readonly IPostingRuleRepository _rules = Substitute.For<IPostingRuleRepository>();
    private readonly IAccountingPeriodRepository _periods = Substitute.For<IAccountingPeriodRepository>();
    private readonly IJournalRepository _journals = Substitute.For<IJournalRepository>();
    private readonly FixedClock _clock = new(Now);
    private readonly AccountingPeriod _period = OpenPeriod();

    private static readonly Guid Debtors = UuidV7.NewGuid();
    private static readonly Guid SalesRevenue = UuidV7.NewGuid();
    private static readonly Guid VatOutput = UuidV7.NewGuid();
    private static readonly Guid CashOnHand = UuidV7.NewGuid();

    private PostingRuleEngine Engine => new(_rules, _periods, _journals, new CountingDocumentNumbers(), _clock);

    private void Rule(PostingRule rule)
        => _rules.FindActiveByEventTypeAsync(rule.EventType, Arg.Any<CancellationToken>()).Returns(rule);

    private void PeriodIsOpen()
        => _periods.FindOpenPeriodForDateAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(_period);

    private Journal? Posted()
        => _journals.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IJournalRepository.Add))
            .Select(call => (Journal)call.GetArguments()[0]!)
            .LastOrDefault();

    private static FinancialEvent ArInvoicePosted() => new(
        "ar.invoice.posted", TenantId, StoreId, Now, "INV-000001",
        new Dictionary<string, Money>(StringComparer.Ordinal)
        {
            ["Net"] = Rand(100m),
            ["Tax"] = Rand(15m),
            ["Gross"] = Rand(115m),
        });

    [Fact]
    public async Task An_ar_invoice_event_resolves_to_a_balanced_journal_from_rule_data()
    {
        PeriodIsOpen();
        Rule(PostingRule.Define(TenantId, "ar.invoice.posted", "Customer invoice")
            .AddLine(Debtors, NormalBalance.Debit, "Gross")
            .AddLine(SalesRevenue, NormalBalance.Credit, "Net")
            .AddLine(VatOutput, NormalBalance.Credit, "Tax"));

        await Engine.PostAsync(ArInvoicePosted());

        Journal journal = Posted()!;
        journal.Lines.Should().HaveCount(3);
        journal.Lines.Sum(line => line.SignedAmount).Should().Be(0m);
        journal.Lines.Single(line => line.AccountId == Debtors).Debit.Should().Be(Rand(115m));
        journal.Lines.Single(line => line.AccountId == SalesRevenue).Credit.Should().Be(Rand(100m));
        journal.Lines.Single(line => line.AccountId == VatOutput).Credit.Should().Be(Rand(15m));
    }

    [Fact]
    public async Task A_pos_sale_event_posts_from_rule_data_alone_with_no_pos_module_in_existence()
    {
        PeriodIsOpen();
        Rule(PostingRule.Define(TenantId, "pos.sale.tendered", "Cash sale at the till")
            .AddLine(CashOnHand, NormalBalance.Debit, "Gross")
            .AddLine(SalesRevenue, NormalBalance.Credit, "Net")
            .AddLine(VatOutput, NormalBalance.Credit, "Tax"));

        await Engine.PostAsync(new FinancialEvent(
            "pos.sale.tendered", TenantId, StoreId, Now, "SALE-42",
            new Dictionary<string, Money>(StringComparer.Ordinal)
            {
                ["Net"] = Rand(200m),
                ["Tax"] = Rand(30m),
                ["Gross"] = Rand(230m),
            }));

        Journal journal = Posted()!;
        journal.Lines.Sum(line => line.SignedAmount).Should().Be(0m);
        journal.SourceModule.Should().Be("pos");
        journal.SourceEventType.Should().Be("pos.sale.tendered");
        journal.SourceReference.Should().Be("SALE-42");
    }

    [Fact]
    public async Task An_event_with_no_matching_rule_is_refused_with_a_stable_error_not_silently_dropped()
    {
        PeriodIsOpen();
        _rules.FindActiveByEventTypeAsync("nobody.configured.this", Arg.Any<CancellationToken>())
            .Returns((PostingRule?)null);

        Func<Task> posting = () => Engine.PostAsync(new FinancialEvent(
            "nobody.configured.this", TenantId, StoreId, Now, "X-1",
            new Dictionary<string, Money>(StringComparer.Ordinal) { ["Gross"] = Rand(10m) }));

        (await posting.Should().ThrowAsync<PostingRuleNotFoundException>())
            .Which.Code.Should().Be("FINANCE_POSTING_RULE_NOT_FOUND");

        _journals.DidNotReceive().Add(Arg.Any<Journal>());
    }

    [Fact]
    public async Task A_rule_naming_an_amount_the_event_does_not_carry_is_refused()
    {
        // The rule asks for "Freight"; the event carries Net/Tax/Gross. Posting a journal with that
        // line simply missing would produce a silently unbalanced ledger.
        PeriodIsOpen();
        Rule(PostingRule.Define(TenantId, "ar.invoice.posted", "Mis-configured")
            .AddLine(Debtors, NormalBalance.Debit, "Gross")
            .AddLine(SalesRevenue, NormalBalance.Credit, "Freight"));

        Func<Task> posting = () => Engine.PostAsync(ArInvoicePosted());

        (await posting.Should().ThrowAsync<UnknownFinancialEventAmountException>())
            .Which.Code.Should().Be("FINANCE_UNKNOWN_EVENT_AMOUNT");

        _journals.DidNotReceive().Add(Arg.Any<Journal>());
    }

    [Fact]
    public async Task An_event_with_no_open_period_to_post_into_is_refused()
    {
        Rule(PostingRule.Define(TenantId, "ar.invoice.posted", "Customer invoice")
            .AddLine(Debtors, NormalBalance.Debit, "Gross")
            .AddLine(SalesRevenue, NormalBalance.Credit, "Net")
            .AddLine(VatOutput, NormalBalance.Credit, "Tax"));
        _periods.FindOpenPeriodForDateAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns((AccountingPeriod?)null);

        Func<Task> posting = () => Engine.PostAsync(ArInvoicePosted());

        await posting.Should().ThrowAsync<NoOpenPeriodException>();
        _journals.DidNotReceive().Add(Arg.Any<Journal>());
    }

    [Fact]
    public async Task A_rule_whose_lines_do_not_balance_is_refused_by_the_ledger_itself()
    {
        // Nothing validates a rule's arithmetic when it is configured — a rule can name any amounts
        // in any combination. The ledger's own balance check is the backstop, and this proves the
        // engine does not bypass it.
        PeriodIsOpen();
        Rule(PostingRule.Define(TenantId, "ar.invoice.posted", "Unbalanced by construction")
            .AddLine(Debtors, NormalBalance.Debit, "Gross")
            .AddLine(SalesRevenue, NormalBalance.Credit, "Net"));

        Func<Task> posting = () => Engine.PostAsync(ArInvoicePosted());

        await posting.Should().ThrowAsync<JournalNotBalancedException>();
    }

    [Fact]
    public async Task A_line_that_inherits_dimensions_carries_the_events_and_one_that_does_not_carries_none()
    {
        Guid department = UuidV7.NewGuid();
        Guid channel = UuidV7.NewGuid();
        PeriodIsOpen();
        Rule(PostingRule.Define(TenantId, "ar.invoice.posted", "Customer invoice")
            .AddLine(Debtors, NormalBalance.Debit, "Gross", inheritDimensions: false)
            .AddLine(SalesRevenue, NormalBalance.Credit, "Net", inheritDimensions: true)
            .AddLine(VatOutput, NormalBalance.Credit, "Tax", inheritDimensions: true));

        await Engine.PostAsync(new FinancialEvent(
            "ar.invoice.posted", TenantId, StoreId, Now, "INV-000001",
            new Dictionary<string, Money>(StringComparer.Ordinal)
            {
                ["Net"] = Rand(100m),
                ["Tax"] = Rand(15m),
                ["Gross"] = Rand(115m),
            },
            DepartmentId: department,
            ChannelId: channel));

        Journal journal = Posted()!;
        JournalLine control = journal.Lines.Single(line => line.AccountId == Debtors);
        JournalLine revenue = journal.Lines.Single(line => line.AccountId == SalesRevenue);

        // The control account is the tenant's single AR balance; tagging it with one invoice's
        // department would make the control account itself analysable by dimension, which it is not.
        control.DepartmentId.Should().BeNull();
        control.ChannelId.Should().BeNull();
        revenue.DepartmentId.Should().Be(department);
        revenue.ChannelId.Should().Be(channel);
    }

    [Fact]
    public async Task The_posted_journal_records_the_engine_as_its_author_and_the_clocks_instant()
    {
        PeriodIsOpen();
        Rule(PostingRule.Define(TenantId, "ar.invoice.posted", "Customer invoice")
            .AddLine(Debtors, NormalBalance.Debit, "Gross")
            .AddLine(SalesRevenue, NormalBalance.Credit, "Net")
            .AddLine(VatOutput, NormalBalance.Credit, "Tax"));

        await Engine.PostAsync(ArInvoicePosted());

        Journal journal = Posted()!;
        journal.PostedBy.Should().Be("system:posting-engine");
        journal.PostedAt.Should().Be(Now);
        journal.AccountingPeriodId.Should().Be(_period.Id);
        journal.JournalNumber.Should().Be("JNL-000001");
    }

    [Fact]
    public void The_financial_event_contract_cannot_name_a_gl_account()
    {
        // Rule 12 "true by construction, not by convention": there is no property on the contract an
        // implementer could put an account id in. This asserts the shape of the contract itself,
        // which is what makes the architecture test's job possible.
        string[] names = [.. typeof(IFinancialEvent).GetProperties().Select(property => property.Name)];

        names.Should().NotContain(name => name.Contains("Account", StringComparison.OrdinalIgnoreCase));
        names.Should().BeEquivalentTo(
            "EventType", "TenantId", "StoreId", "OccurredAt", "SourceReference", "Amounts",
            "DepartmentId", "CostCentreId", "ProjectId", "ChannelId", "EmployeeId");
    }
}
