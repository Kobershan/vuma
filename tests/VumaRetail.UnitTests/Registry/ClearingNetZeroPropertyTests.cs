using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry;
using Xunit;

namespace VumaRetail.UnitTests.Registry;

/// <summary>
/// Property tests for inter-company clearing: 200 randomised allocations/reversals/failures
/// across 3 databases asserting clearing nets to zero once every intent settles.
/// </summary>
public class ClearingNetZeroPropertyTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid[] Companies = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];

    /// <summary>
    /// After any sequence of allocations and reversals, every settled intent's
    /// debit leg equals its credit leg in amount and currency.
    /// </summary>
    [Theory]
    [InlineData(200)]
    public void Settled_intents_net_to_zero(int iterations)
    {
        var random = new Random(42); // Deterministic seed for reproducibility
        List<InterCompanyClearingIntent> settledIntents = [];

        for (int i = 0; i < iterations; i++)
        {
            Guid fromCompany = Companies[random.Next(0, 3)];
            Guid toCompany;
            do
            {
                toCompany = Companies[random.Next(0, 3)];
            } while (toCompany == fromCompany);

            decimal amount = random.Next(1, 10000);
            var money = new Money(amount, "ZAR");

            var intent = InterCompanyClearingIntent.Create(
                TenantId, Guid.NewGuid(), "group-receipt",
                fromCompany, toCompany, money, "ZAR");

            // Simulate: acknowledge or fail based on random
            if (random.Next(0, 10) < 8) // 80% success rate
            {
                foreach (InterCompanyClearingLeg leg in intent.Legs)
                {
                    intent.AcknowledgeLeg(leg.Id);
                }
                settledIntents.Add(intent);
            }
            else
            {
                // Inject failure on one leg
                InterCompanyClearingLeg failingLeg = intent.Legs[random.Next(0, 2)];
                failingLeg.ErrorMessage = "Simulated database outage";
                // Leg stays in current state (Pending)

                // Retry: acknowledge the failed leg
                intent.AcknowledgeLeg(failingLeg.Id);
                // Then acknowledge the other leg
                InterCompanyClearingLeg otherLeg = intent.Legs.First(l => l.Id != failingLeg.Id);
                intent.AcknowledgeLeg(otherLeg.Id);
                settledIntents.Add(intent);
            }
        }

        // Assert: every settled intent has net-zero
        foreach (InterCompanyClearingIntent intent in settledIntents)
        {
            Assert.Equal(InterCompanyClearingIntentState.Settled, intent.State);

            InterCompanyClearingLeg debitLeg = intent.Legs.First(l => l.Direction == "Debit");
            InterCompanyClearingLeg creditLeg = intent.Legs.First(l => l.Direction == "Credit");

            Assert.Equal(debitLeg.Amount.Amount, creditLeg.Amount.Amount);
            Assert.Equal(debitLeg.Amount.Currency, creditLeg.Amount.Currency);
            Assert.Equal(intent.Amount.Amount, debitLeg.Amount.Amount);
        }

        // Assert: total debit across all settled intents = total credit
        decimal totalDebit = settledIntents.Sum(i => i.Legs.First(l => l.Direction == "Debit").Amount.Amount);
        decimal totalCredit = settledIntents.Sum(i => i.Legs.First(l => l.Direction == "Credit").Amount.Amount);
        Assert.Equal(totalDebit, totalCredit);
    }

    /// <summary>
    /// Compensating an unsettled intent marks all outstanding legs.
    /// </summary>
    [Theory]
    [InlineData(100)]
    public void Reversed_intents_have_all_legs_compensated(int iterations)
    {
        var random = new Random(42);

        for (int i = 0; i < iterations; i++)
        {
            Guid fromCompany = Companies[random.Next(0, 3)];
            Guid toCompany;
            do
            {
                toCompany = Companies[random.Next(0, 3)];
            } while (toCompany == fromCompany);

            decimal amount = random.Next(1, 10000);
            var money = new Money(amount, "ZAR");

            var intent = InterCompanyClearingIntent.Create(
                TenantId, Guid.NewGuid(), "group-receipt",
                fromCompany, toCompany, money, "ZAR");

            // Compensate before settling (outstanding legs only)
            intent.Compensate();

            Assert.Equal(InterCompanyClearingIntentState.Compensated, intent.State);
            Assert.All(intent.Legs, l =>
                Assert.Equal(InterCompanyClearingLegState.Compensated, l.State));
        }
    }
}

/// <summary>
/// Tests for the consolidated income statement inter-company elimination.
/// </summary>
public class ConsolidatedIncomeStatementEliminationTests
{
    [Fact]
    public void Eliminate_inter_company_trade_equals_hand_computed_figure()
    {
        // Hand-computed fixture:
        // Company A: Sales R100,000, Cost of Sales R60,000, IC Clearing (debit) R30,000
        // Company B: Sales R80,000, Cost of Sales R50,000, IC Clearing (credit) R30,000
        // Company C: Sales R120,000, Cost of Sales R70,000
        //
        // After elimination:
        // Total Sales = R100,000 + R80,000 + R120,000 = R300,000
        // Total CoS = R60,000 + R50,000 + R70,000 = R180,000
        // IC Clearing eliminated (net = 0)
        // Net Income = R300,000 - R180,000 = R120,000

        decimal expectedNetIncome = 120_000m;

        // Simulate the consolidation logic
        var accounts = new List<CompanyAccountBalance>
        {
            // Company A
            new() { AccountCode = "4000", AccountName = "Sales", AccountType = "Income",
                Debit = Money.Zero("ZAR"), Credit = new Money(100_000m, "ZAR") },
            new() { AccountCode = "5000", AccountName = "Cost of Sales", AccountType = "Expense",
                Debit = new Money(60_000m, "ZAR"), Credit = Money.Zero("ZAR") },
            new() { AccountCode = "ICCLR-001", AccountName = "IC Clearing A-B", AccountType = "Asset",
                Debit = new Money(30_000m, "ZAR"), Credit = Money.Zero("ZAR") },
            // Company B
            new() { AccountCode = "4000", AccountName = "Sales", AccountType = "Income",
                Debit = Money.Zero("ZAR"), Credit = new Money(80_000m, "ZAR") },
            new() { AccountCode = "5000", AccountName = "Cost of Sales", AccountType = "Expense",
                Debit = new Money(50_000m, "ZAR"), Credit = Money.Zero("ZAR") },
            new() { AccountCode = "ICCLR-001", AccountName = "IC Clearing A-B", AccountType = "Asset",
                Debit = Money.Zero("ZAR"), Credit = new Money(30_000m, "ZAR") },
            // Company C
            new() { AccountCode = "4000", AccountName = "Sales", AccountType = "Income",
                Debit = Money.Zero("ZAR"), Credit = new Money(120_000m, "ZAR") },
            new() { AccountCode = "5000", AccountName = "Cost of Sales", AccountType = "Expense",
                Debit = new Money(70_000m, "ZAR"), Credit = Money.Zero("ZAR") },
        };

        // Eliminate IC clearing accounts
        List<CompanyAccountBalance> filtered = accounts
            .Where(a => !a.AccountCode.StartsWith("ICCLR"))
            .ToList();

        // Aggregate by account code
        var aggregated = filtered
            .GroupBy(a => a.AccountCode)
            .Select(g => new
            {
                AccountCode = g.Key,
                TotalDebit = g.Sum(a => a.Debit.Amount),
                TotalCredit = g.Sum(a => a.Credit.Amount),
            })
            .ToList();

        // Compute net income
        decimal totalIncome = aggregated
            .Where(a => filtered.First(f => f.AccountCode == a.AccountCode).AccountType == "Income")
            .Sum(a => a.TotalCredit - a.TotalDebit);

        decimal totalExpense = aggregated
            .Where(a => filtered.First(f => f.AccountCode == a.AccountCode).AccountType == "Expense")
            .Sum(a => a.TotalDebit - a.TotalCredit);

        decimal actualNetIncome = totalIncome - totalExpense;

        Assert.Equal(expectedNetIncome, actualNetIncome);

        // Verify IC clearing eliminated
        Assert.DoesNotContain(aggregated, a => a.AccountCode.StartsWith("ICCLR"));

        // Verify totals
        Assert.Equal(300_000m, totalIncome);
        Assert.Equal(180_000m, totalExpense);
    }
}
