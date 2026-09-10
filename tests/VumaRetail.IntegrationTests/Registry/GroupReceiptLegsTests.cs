using Microsoft.EntityFrameworkCore;
using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.CustomerAccounts;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Domain.Finance;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry;
using VumaRetail.Finance.Commands;
using VumaRetail.Finance.Periods;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Registry;
using VumaRetail.IntegrationTests.Harness;

namespace VumaRetail.IntegrationTests.Registry;

/// <summary>
/// Stage 07c against real PostgreSQL: legs actually execute in their companies' databases.
/// The operator's example — R9 000 captured once, allocated R1 000 / R3 000 / R5 000 across
/// three companies — becomes three AR receipts, balanced trial balances, and clearing that
/// nets to zero, with group exposure dropping by exactly R9 000.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class GroupReceiptLegsTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Operators_example_posts_three_receipts_and_clearing_nets_to_zero()
    {
        await using GroupReceiptHarness harness = await GroupReceiptHarness.CreateAsync(fixture);

        Guid receiptId = await harness.Service.CaptureAsync(
            harness.TenantId, harness.CompanyAId, harness.BankAccountId,
            new Money(9000m, "ZAR"), "EFT", "MKHIZE 22/08", harness.Clock.UtcNow);

        await harness.Service.AllocateAsync(
            harness.TenantId, receiptId, harness.CompanyAId, harness.CustomerId,
            new Money(1000m, "ZAR"), [harness.InvoiceAId]);
        await harness.Service.AllocateAsync(
            harness.TenantId, receiptId, harness.CompanyBId, harness.CustomerId,
            new Money(3000m, "ZAR"), [harness.InvoiceBId]);
        await harness.Service.AllocateAsync(
            harness.TenantId, receiptId, harness.CompanyCId, harness.CustomerId,
            new Money(5000m, "ZAR"), [harness.InvoiceCId]);

        // One receipt per company, each in its own books.
        await using VumaRetailDbContext companyDb = harness.OpenCompanyDb();
        List<ArReceipt> receiptsA = await ReceiptsInAsync(companyDb, harness.CompanyAId);
        List<ArReceipt> receiptsB = await ReceiptsInAsync(companyDb, harness.CompanyBId);
        List<ArReceipt> receiptsC = await ReceiptsInAsync(companyDb, harness.CompanyCId);
        receiptsA.Should().ContainSingle();
        receiptsB.Should().ContainSingle();
        receiptsC.Should().ContainSingle();
        receiptsA.Single().Amount.Should().Be(new Money(1000m, "ZAR"));
        receiptsB.Single().Amount.Should().Be(new Money(3000m, "ZAR"));
        receiptsC.Single().Amount.Should().Be(new Money(5000m, "ZAR"));

        // Every receipt is traceable to the group document; sister legs carry the clearing
        // intent, the bank owner's own leg carries the saga intent.
        receiptsA.Single().GroupDocumentId.Should().Be(receiptId);
        receiptsA.Single().IntentId.Should().NotBeNull();
        receiptsB.Single().GroupDocumentId.Should().Be(receiptId);
        receiptsC.Single().GroupDocumentId.Should().Be(receiptId);

        // The targeted invoices are settled: group exposure drops by exactly R9 000.
        (await OutstandingAsync(companyDb, harness.InvoiceAId)).Should().Be(new Money(0m, "ZAR"));
        (await OutstandingAsync(companyDb, harness.InvoiceBId)).Should().Be(new Money(0m, "ZAR"));
        (await OutstandingAsync(companyDb, harness.InvoiceCId)).Should().Be(new Money(0m, "ZAR"));

        // Every company's trial balance still balances.
        (await TrialBalanceAsync(companyDb, harness.CompanyAId)).Should().Be(0m);
        (await TrialBalanceAsync(companyDb, harness.CompanyBId)).Should().Be(0m);
        (await TrialBalanceAsync(companyDb, harness.CompanyCId)).Should().Be(0m);

        // Two clearing intents (one per sister share), both settled, netting to zero.
        await using VumaRegistryDbContext registry = TestDbContextFactory.ForRegistry(
            harness.ConnectionString, TestTenantContext.For(harness.TenantId));
        List<InterCompanyClearingIntent> intents = await registry.InterCompanyClearingIntents
            .Include(i => i.Legs)
            .Where(i => i.GroupDocumentId == receiptId)
            .ToListAsync();
        intents.Should().HaveCount(2);
        intents.Should().OnlyContain(i => i.State == InterCompanyClearingIntentState.Settled);
        intents.SelectMany(i => i.Legs).Where(l => l.Direction == "Debit").Sum(l => l.Amount.Amount)
            .Should().Be(intents.SelectMany(i => i.Legs).Where(l => l.Direction == "Credit").Sum(l => l.Amount.Amount));

        // Sister receipts point at their clearing intents.
        receiptsB.Single().IntentId.Should().Be(intents.Single(i => i.ToCompanyId == harness.CompanyBId).Id);
        receiptsC.Single().IntentId.Should().Be(intents.Single(i => i.ToCompanyId == harness.CompanyCId).Id);

        // Nothing outstanding anywhere: the receipt is fully allocated, no leg is pending.
        GroupReceipt? stored = await harness.Repository.GetByIdAsync(receiptId);
        stored!.Status.Should().Be(GroupReceiptStatus.Allocated);
        (await harness.Repository.GetOutstandingIntentsAsync(harness.TenantId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Mid_allocation_outage_stays_pending_and_retry_applies_once()
    {
        await using GroupReceiptHarness harness = await GroupReceiptHarness.CreateAsync(fixture);

        Guid receiptId = await harness.Service.CaptureAsync(
            harness.TenantId, harness.CompanyAId, harness.BankAccountId,
            new Money(9000m, "ZAR"), "EFT", "OUTAGE 01", harness.Clock.UtcNow);

        await harness.Service.AllocateAsync(
            harness.TenantId, receiptId, harness.CompanyAId, harness.CustomerId,
            new Money(1000m, "ZAR"), [harness.InvoiceAId]);

        // Company B's database goes down mid-allocation: its leg stays pending.
        harness.SetCompanyDown(harness.CompanyBId, true);
        Func<Task> allocating = () => harness.Service.AllocateAsync(
            harness.TenantId, receiptId, harness.CompanyBId, harness.CustomerId,
            new Money(3000m, "ZAR"), [harness.InvoiceBId]);
        (await allocating.Should().ThrowAsync<InvalidOperationException>()).Which.Message
            .Should().Contain(harness.CompanyBId.ToString());

        GroupReceipt? stored = await harness.Repository.GetByIdAsync(receiptId);
        GroupReceiptAllocation pending = stored!.Allocations.Single(a => a.CompanyId == harness.CompanyBId);
        pending.LegState.Should().Be(GroupReceiptAllocationLegState.Pending);
        pending.ErrorMessage.Should().NotBeNullOrWhiteSpace();

        // The clearing intent is outstanding and visible with its company and amount.
        IReadOnlyList<InterCompanyClearingIntent> outstanding =
            await harness.Repository.GetOutstandingIntentsAsync(harness.TenantId);
        InterCompanyClearingIntent blocked = outstanding.Should().ContainSingle().Subject;
        blocked.ToCompanyId.Should().Be(harness.CompanyBId);
        blocked.Amount.Should().Be(new Money(3000m, "ZAR"));
        blocked.Legs.Should().OnlyContain(l =>
            l.State == InterCompanyClearingLegState.Pending || l.State == InterCompanyClearingLegState.Failed);

        // The other companies are unaffected: company C allocates while B is down.
        await harness.Service.AllocateAsync(
            harness.TenantId, receiptId, harness.CompanyCId, harness.CustomerId,
            new Money(5000m, "ZAR"), [harness.InvoiceCId]);
        await using VumaRetailDbContext companyDb = harness.OpenCompanyDb();
        (await ReceiptsInAsync(companyDb, harness.CompanyCId)).Should().ContainSingle();

        // The database comes back: one retry applies exactly once, and a second retry is a no-op.
        harness.SetCompanyDown(harness.CompanyBId, false);
        GroupReceiptAllocation pendingId = (await harness.Repository.GetByIdAsync(receiptId))!
            .Allocations.Single(a => a.CompanyId == harness.CompanyBId);
        await harness.Service.RetryAllocationAsync(harness.TenantId, receiptId, pendingId.Id);
        await harness.Service.RetryAllocationAsync(harness.TenantId, receiptId, pendingId.Id);

        await using VumaRetailDbContext afterDb = harness.OpenCompanyDb();
        (await ReceiptsInAsync(afterDb, harness.CompanyBId)).Should().ContainSingle();
        (await OutstandingAsync(afterDb, harness.InvoiceBId)).Should().Be(new Money(0m, "ZAR"));
        (await harness.Repository.GetOutstandingIntentsAsync(harness.TenantId)).Should().BeEmpty();
        (await harness.Repository.GetByIdAsync(receiptId))!.Status
            .Should().Be(GroupReceiptStatus.Allocated);
    }

    [Fact]
    public async Task Partial_allocation_leaves_nothing_in_any_other_ledger()
    {
        await using GroupReceiptHarness harness = await GroupReceiptHarness.CreateAsync(fixture);

        Guid receiptId = await harness.Service.CaptureAsync(
            harness.TenantId, harness.CompanyAId, harness.BankAccountId,
            new Money(9000m, "ZAR"), "EFT", "PARTIAL 01", harness.Clock.UtcNow);
        await harness.Service.AllocateAsync(
            harness.TenantId, receiptId, harness.CompanyAId, harness.CustomerId,
            new Money(1000m, "ZAR"), [harness.InvoiceAId]);

        // The R8 000 that stays home appears in no company's ledger.
        await using VumaRetailDbContext companyDb = harness.OpenCompanyDb();
        (await ReceiptsInAsync(companyDb, harness.CompanyBId)).Should().BeEmpty();
        (await ReceiptsInAsync(companyDb, harness.CompanyCId)).Should().BeEmpty();
        (await companyDb.Journals.CountAsync(j => j.CompanyId == harness.CompanyBId)).Should().Be(0);
        (await companyDb.Journals.CountAsync(j => j.CompanyId == harness.CompanyCId)).Should().Be(0);

        // …and ages on the unallocated report instead.
        IReadOnlyList<GroupReceipt> unallocated =
            await harness.Repository.GetUnallocatedAsync(harness.TenantId);
        GroupReceipt report = unallocated.Should().ContainSingle(r => r.Id == receiptId).Subject;
        report.Status.Should().Be(GroupReceiptStatus.PartiallyAllocated);
        (report.Amount.Amount - report.Allocations
            .Where(a => a.LegState is not GroupReceiptAllocationLegState.Compensated)
            .Sum(a => a.Amount.Amount)).Should().Be(8000m);
    }

    [Fact]
    public async Task Full_reversal_restores_everything_and_edits_no_journal()
    {
        await using GroupReceiptHarness harness = await GroupReceiptHarness.CreateAsync(fixture);

        Guid receiptId = await harness.Service.CaptureAsync(
            harness.TenantId, harness.CompanyAId, harness.BankAccountId,
            new Money(9000m, "ZAR"), "EFT", "REVERSE 01", harness.Clock.UtcNow);
        await harness.Service.AllocateAsync(
            harness.TenantId, receiptId, harness.CompanyAId, harness.CustomerId,
            new Money(1000m, "ZAR"), [harness.InvoiceAId]);
        await harness.Service.AllocateAsync(
            harness.TenantId, receiptId, harness.CompanyBId, harness.CustomerId,
            new Money(3000m, "ZAR"), [harness.InvoiceBId]);
        await harness.Service.AllocateAsync(
            harness.TenantId, receiptId, harness.CompanyCId, harness.CustomerId,
            new Money(5000m, "ZAR"), [harness.InvoiceCId]);

        await using VumaRetailDbContext beforeDb = harness.OpenCompanyDb();
        List<Guid> journalIds = await beforeDb.Journals.Select(j => j.Id).ToListAsync();

        await harness.Service.ReverseAsync(harness.TenantId, receiptId);

        await using VumaRetailDbContext companyDb = harness.OpenCompanyDb();

        // Every posted journal stands untouched; the reversal added mirrors beside them.
        List<Guid> afterIds = await companyDb.Journals.Select(j => j.Id).ToListAsync();
        afterIds.Should().Contain(journalIds);
        afterIds.Should().HaveCount(journalIds.Count + 5);

        // One negative reversal receipt per company, against the same invoices.
        foreach ((Guid companyId, decimal share) in new[]
                 {
                     (harness.CompanyAId, 1000m), (harness.CompanyBId, 3000m), (harness.CompanyCId, 5000m),
                 })
        {
            List<ArReceipt> receipts = await ReceiptsInAsync(companyDb, companyId);
            receipts.Should().HaveCount(2);
            receipts.Single(r => r.Amount.Amount < 0).Amount.Should().Be(new Money(-share, "ZAR"));
            (await TrialBalanceAsync(companyDb, companyId)).Should().Be(0m);
        }

        // The invoices are whole again: group exposure returns.
        (await OutstandingAsync(companyDb, harness.InvoiceAId)).Should().Be(new Money(1000m, "ZAR"));
        (await OutstandingAsync(companyDb, harness.InvoiceBId)).Should().Be(new Money(3000m, "ZAR"));
        (await OutstandingAsync(companyDb, harness.InvoiceCId)).Should().Be(new Money(5000m, "ZAR"));

        await using VumaRegistryDbContext registry = TestDbContextFactory.ForRegistry(
            harness.ConnectionString, TestTenantContext.For(harness.TenantId));
        (await harness.Repository.GetByIdAsync(receiptId))!.Status.Should().Be(GroupReceiptStatus.Reversed);
        List<InterCompanyClearingIntent> intents = await registry.InterCompanyClearingIntents
            .Where(i => i.GroupDocumentId == receiptId)
            .ToListAsync();
        intents.Should().HaveCount(2);
        intents.Should().OnlyContain(i => i.State == InterCompanyClearingIntentState.Reversed);
        (await harness.Repository.GetOutstandingIntentsAsync(harness.TenantId)).Should().BeEmpty();

        // Reversing twice changes nothing.
        await harness.Service.ReverseAsync(harness.TenantId, receiptId);
        await using VumaRetailDbContext replayDb = harness.OpenCompanyDb();
        (await replayDb.ArReceipts.CountAsync()).Should().Be(6);
    }

    [Fact]
    public async Task Over_allocated_invoice_is_refused_without_posting()
    {
        await using GroupReceiptHarness harness = await GroupReceiptHarness.CreateAsync(fixture);

        Guid receiptId = await harness.Service.CaptureAsync(
            harness.TenantId, harness.CompanyAId, harness.BankAccountId,
            new Money(9000m, "ZAR"), "EFT", "OVER 01", harness.Clock.UtcNow);
        await harness.Service.AllocateAsync(
            harness.TenantId, receiptId, harness.CompanyBId, harness.CustomerId,
            new Money(3000m, "ZAR"), [harness.InvoiceBId]);

        // The invoice is settled; allocating its full value again must refuse (business rule 5).
        Guid secondId = await harness.Service.CaptureAsync(
            harness.TenantId, harness.CompanyAId, harness.BankAccountId,
            new Money(9000m, "ZAR"), "EFT", "OVER 02", harness.Clock.UtcNow);
        Func<Task> over = () => harness.Service.AllocateAsync(
            harness.TenantId, secondId, harness.CompanyBId, harness.CustomerId,
            new Money(3000m, "ZAR"), [harness.InvoiceBId]);
        (await over.Should().ThrowAsync<InvalidOperationException>()).Which.Message
            .Should().Contain(harness.InvoiceBId.ToString());

        // The failed leg posted nothing: still exactly one receipt in company B.
        await using VumaRetailDbContext companyDb = harness.OpenCompanyDb();
        (await ReceiptsInAsync(companyDb, harness.CompanyBId)).Should().ContainSingle();
        GroupReceipt? stored = await harness.Repository.GetByIdAsync(secondId);
        stored!.Allocations.Should().ContainSingle()
            .Which.LegState.Should().Be(GroupReceiptAllocationLegState.Pending);
    }

    [Fact]
    public async Task Unlinked_company_is_refused_before_any_write()
    {
        var links = Substitute.For<ICompanyLinkGuard>();
        links.When(x => x.RequireLinkAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CompanyLinkScope>(),
                Arg.Any<CancellationToken>()))
            .Do(call => throw new InvalidOperationException(
                $"TRADING_LINK_REQUIRED: no active {call.ArgAt<CompanyLinkScope>(3)} link "
                + $"between {call.ArgAt<Guid>(1)} and {call.ArgAt<Guid>(2)}."));
        await using GroupReceiptHarness harness = await GroupReceiptHarness.CreateAsync(fixture, links);

        Guid receiptId = await harness.Service.CaptureAsync(
            harness.TenantId, harness.CompanyAId, harness.BankAccountId,
            new Money(9000m, "ZAR"), "EFT", "NOLINK 01", harness.Clock.UtcNow);

        Func<Task> allocating = () => harness.Service.AllocateAsync(
            harness.TenantId, receiptId, harness.CompanyBId, harness.CustomerId,
            new Money(3000m, "ZAR"), [harness.InvoiceBId]);
        var refusal = (await allocating.Should().ThrowAsync<InvalidOperationException>()).Which;
        refusal.Message.Should().Contain("SharedReceipting");
        refusal.Message.Should().Contain(harness.CompanyAId.ToString());
        refusal.Message.Should().Contain(harness.CompanyBId.ToString());

        // The refusal happened before any write: no allocation, no intent, no receipt.
        GroupReceipt? stored = await harness.Repository.GetByIdAsync(receiptId);
        stored!.Allocations.Should().BeEmpty();
        (await harness.Repository.GetOutstandingIntentsAsync(harness.TenantId)).Should().BeEmpty();
        await using VumaRetailDbContext companyDb = harness.OpenCompanyDb();
        (await ReceiptsInAsync(companyDb, harness.CompanyBId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Period_close_is_refused_while_a_leg_is_outstanding_then_closes_after_retry()
    {
        await using GroupReceiptHarness harness = await GroupReceiptHarness.CreateAsync(fixture);

        Guid receiptId = await harness.Service.CaptureAsync(
            harness.TenantId, harness.CompanyAId, harness.BankAccountId,
            new Money(9000m, "ZAR"), "EFT", "CLOSE 01", harness.Clock.UtcNow);

        harness.SetCompanyDown(harness.CompanyBId, true);
        try
        {
            await harness.Service.AllocateAsync(
                harness.TenantId, receiptId, harness.CompanyBId, harness.CustomerId,
                new Money(3000m, "ZAR"), [harness.InvoiceBId]);
        }
        catch (InvalidOperationException)
        {
            // Expected: the leg stays pending.
        }

        var periods = Substitute.For<IAccountingPeriodRepository>();
        AccountingPeriod period = AccountingPeriod.Open(
            harness.TenantId, DateOnly.FromDateTime(harness.Clock.UtcNow.UtcDateTime), DateOnly.FromDateTime(harness.Clock.UtcNow.UtcDateTime).AddMonths(1));
        periods.FindByIdAsync(period.Id, Arg.Any<CancellationToken>()).Returns(period);
        var company = Substitute.For<ICompanyContext>();
        company.CompanyId.Returns(harness.CompanyBId);
        var handler = new ClosePeriodCommandHandler(
            periods, CleanChecker(), new TestPrincipal("close-test"), harness.Clock,
            new PeriodCloseGuard(harness.Repository), company);

        Func<Task> closing = () => handler.HandleAsync(new ClosePeriodCommand(period.Id));
        var refusal = (await closing.Should().ThrowAsync<PeriodCloseBlockedByIntentsException>()).Which;
        refusal.IntentIds.Should().NotBeEmpty();
        period.Status.Should().Be(PeriodStatus.Open);

        // The database comes back, the leg applies on retry, and the close proceeds.
        harness.SetCompanyDown(harness.CompanyBId, false);
        GroupReceiptAllocation pending = (await harness.Repository.GetByIdAsync(receiptId))!
            .Allocations.Single(a => a.CompanyId == harness.CompanyBId);
        await harness.Service.RetryAllocationAsync(harness.TenantId, receiptId, pending.Id);

        await handler.HandleAsync(new ClosePeriodCommand(period.Id));
        period.Status.Should().Be(PeriodStatus.Closed);
    }

    [Fact]
    public async Task Randomised_run_keeps_clearing_at_zero()
    {
        await using GroupReceiptHarness harness = await GroupReceiptHarness.CreateAsync(fixture);
        var random = new Random(42);

        Guid[] companies = [harness.CompanyAId, harness.CompanyBId, harness.CompanyCId];
        List<Guid> receipts = [];
        HashSet<Guid> reversalTargets = [];

        for (int op = 0; op < 200; op++)
        {
            int choice = random.Next(100);
            if (choice < 30 || receipts.Count == 0)
            {
                Guid id = await harness.Service.CaptureAsync(
                    harness.TenantId, harness.CompanyAId, harness.BankAccountId,
                    new Money(random.Next(10, 90) * 100m, "ZAR"), "EFT",
                    $"RND-{op:000}", harness.Clock.UtcNow);
                receipts.Add(id);
            }
            else if (choice < 75)
            {
                Guid id = receipts[random.Next(receipts.Count)];
                GroupReceipt? stored = await harness.Repository.GetByIdAsync(id);
                if (stored is null || stored.Status is GroupReceiptStatus.Allocated or GroupReceiptStatus.Reversed)
                {
                    continue;
                }

                decimal remaining = stored.Amount.Amount - stored.Allocations
                    .Where(a => a.LegState is not GroupReceiptAllocationLegState.Compensated)
                    .Sum(a => a.Amount.Amount);
                if (remaining <= 0)
                {
                    continue;
                }

                Guid company = companies[random.Next(companies.Length)];
                decimal slice = Math.Min(remaining, random.Next(1, 10) * 100m);
                try
                {
                    await harness.Service.AllocateAsync(
                        harness.TenantId, id, company, harness.CustomerId,
                        new Money(slice, "ZAR"), null);
                }
                catch (InvalidOperationException)
                {
                    // A company taken down below, or a state race with a reversal: the
                    // allocation stays pending and the next retry pass picks it up.
                }
            }
            else if (choice < 85)
            {
                Guid id = receipts[random.Next(receipts.Count)];
                reversalTargets.Add(id);
                try
                {
                    await harness.Service.ReverseAsync(harness.TenantId, id);
                }
                catch (InvalidOperationException)
                {
                    // Partially reversed under an outage below; retried at the end.
                }
            }
            else if (choice < 90)
            {
                Guid down = companies[random.Next(companies.Length)];
                harness.SetCompanyDown(down, true);
            }
            else if (choice < 95)
            {
                foreach (Guid down in companies)
                {
                    harness.SetCompanyDown(down, false);
                }

                foreach (Guid id in receipts)
                {
                    GroupReceipt? stored = await harness.Repository.GetByIdAsync(id);
                    if (stored is null)
                    {
                        continue;
                    }

                    foreach (GroupReceiptAllocation allocation in stored.Allocations
                        .Where(a => a.LegState == GroupReceiptAllocationLegState.Pending))
                    {
                        try
                        {
                            await harness.Service.RetryAllocationAsync(harness.TenantId, id, allocation.Id);
                        }
                        catch (InvalidOperationException)
                        {
                            // Still failing; the final pass retries again.
                        }
                    }
                }
            }
            else
            {
                foreach (Guid down in companies)
                {
                    harness.SetCompanyDown(down, false);
                }
            }
        }

        // Final retry pass with every database up: pending allocations apply, and every
        // reversal target finishes reversing.
        foreach (Guid down in companies)
        {
            harness.SetCompanyDown(down, false);
        }

        foreach (Guid id in receipts)
        {
            GroupReceipt? stored = await harness.Repository.GetByIdAsync(id);
            if (stored is null)
            {
                continue;
            }

            foreach (GroupReceiptAllocation allocation in stored.Allocations
                .Where(a => a.LegState == GroupReceiptAllocationLegState.Pending))
            {
                await harness.Service.RetryAllocationAsync(harness.TenantId, id, allocation.Id);
            }
        }

        foreach (Guid id in reversalTargets)
        {
            GroupReceipt? stored = await harness.Repository.GetByIdAsync(id);
            if (stored is { Status: not GroupReceiptStatus.Reversed })
            {
                await harness.Service.ReverseAsync(harness.TenantId, id);
            }
        }

        // Every allocation applied or was compensated by a reversal; nothing dangles.
        await using VumaRegistryDbContext registry = TestDbContextFactory.ForRegistry(
            harness.ConnectionString, TestTenantContext.For(harness.TenantId));
        List<GroupReceipt> all = await registry.GroupReceipts
            .Include(r => r.Allocations)
            .ToListAsync();
        all.Should().NotBeEmpty();
        all.SelectMany(r => r.Allocations)
            .Should().OnlyContain(a => a.LegState == GroupReceiptAllocationLegState.Applied || a.LegState == GroupReceiptAllocationLegState.Compensated);

        List<InterCompanyClearingIntent> intents = await registry.InterCompanyClearingIntents
            .Include(i => i.Legs)
            .ToListAsync();
        intents.Should().OnlyContain(i => i.State == InterCompanyClearingIntentState.Settled || i.State == InterCompanyClearingIntentState.Reversed || i.State == InterCompanyClearingIntentState.Compensated);
        foreach (InterCompanyClearingIntent intent in intents.Where(i => i.State == InterCompanyClearingIntentState.Settled))
        {
            intent.Legs.Where(l => l.Direction == "Debit").Sum(l => l.Amount.Amount)
                .Should().Be(intent.Legs.Where(l => l.Direction == "Credit").Sum(l => l.Amount.Amount));
        }

        (await harness.Repository.GetOutstandingIntentsAsync(harness.TenantId)).Should().BeEmpty();

        // Every company's books still balance, and each company's net receipts equal its net
        // applied allocations from the registry (reversals included on both sides).
        await using VumaRetailDbContext companyDb = harness.OpenCompanyDb();
        foreach (Guid companyId in companies)
        {
            (await TrialBalanceAsync(companyDb, companyId)).Should().Be(0m);
        }

        Dictionary<Guid, decimal> expected = companies.ToDictionary(c => c, _ => 0m);
        foreach (GroupReceipt receipt in all)
        {
            foreach (GroupReceiptAllocation allocation in receipt.Allocations
                .Where(a => a.LegState == GroupReceiptAllocationLegState.Applied))
            {
                expected[allocation.CompanyId] += allocation.Amount.Amount;
            }
        }

        foreach (Guid companyId in companies)
        {
            decimal posted = await companyDb.ArReceipts
                .Where(r => r.CompanyId == companyId)
                .SumAsync(r => r.Amount.Amount);
            posted.Should().Be(expected[companyId]);
        }
    }

    private static async Task<List<ArReceipt>> ReceiptsInAsync(VumaRetailDbContext db, Guid companyId)
        => await db.ArReceipts
            .Include(r => r.Allocations)
            .Where(r => r.CompanyId == companyId)
            .OrderBy(r => r.ReceivedAt)
            .ToListAsync();

    private static async Task<Money> OutstandingAsync(VumaRetailDbContext db, Guid invoiceId)
        => (await db.ArInvoices.FirstAsync(i => i.Id == invoiceId)).OutstandingBalance;

    /// <summary>Σ debits − Σ credits over the company's journal lines: zero when balanced.</summary>
    private static async Task<decimal> TrialBalanceAsync(VumaRetailDbContext db, Guid companyId)
    {
        List<JournalLine> lines = await db.JournalLines
            .Where(l => l.CompanyId == companyId)
            .ToListAsync();
        return lines.Sum(l => l.Debit?.Amount ?? 0m) - lines.Sum(l => l.Credit?.Amount ?? 0m);
    }

    private static PeriodVarianceChecker CleanChecker()
    {
        var accounts = Substitute.For<IAccountRepository>();
        accounts.ListControlAccountsAsync(Arg.Any<CancellationToken>()).Returns([]);
        return new PeriodVarianceChecker(
            accounts,
            Substitute.For<IJournalRepository>(),
            Substitute.For<IArInvoiceRepository>(),
            Substitute.For<IApInvoiceRepository>(),
            Substitute.For<IBankAccountRepository>(),
            Substitute.For<IBankStatementLineRepository>(),
            Substitute.For<ILayByAgreementRepository>());
    }

    private sealed class TestPrincipal(string name) : IPrincipalAccessor
    {
        public string Principal => name;
        public Guid? TerminalId => null;
        public bool IsSystem => false;
    }
}
