using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Registry;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry;
using NSubstitute;
using Xunit;

namespace VumaRetail.UnitTests.Registry;

public class CaptureGroupReceiptCommandHandlerTests
{
    [Fact]
    public async Task HandleAsync_creates_group_receipt_and_returns_id()
    {
        var service = Substitute.For<IGroupReceiptService>();
        service.CaptureAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
            Arg.Any<Money>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Guid.NewGuid());

        var handler = new CaptureGroupReceiptCommandHandler(service);
        var command = new CaptureGroupReceiptCommand
        {
            TenantId = Guid.NewGuid(),
            CapturingCompanyId = Guid.NewGuid(),
            BankAccountId = Guid.NewGuid(),
            Amount = new Money(5000m, "ZAR"),
            TenderType = "EFT",
            Reference = "TEST-001",
            CapturedAt = DateTimeOffset.UtcNow,
        };

        Guid result = await handler.HandleAsync(command);

        Assert.NotEqual(Guid.Empty, result);
        await service.Received(1).CaptureAsync(
            command.TenantId, command.CapturingCompanyId, command.BankAccountId,
            command.Amount, command.TenderType, command.Reference,
            command.CapturedAt, Arg.Any<CancellationToken>());
    }
}

public class AllocateGroupReceiptCommandHandlerTests
{
    [Fact]
    public async Task HandleAsync_dispatches_allocation()
    {
        var service = Substitute.For<IGroupReceiptService>();

        var handler = new AllocateGroupReceiptCommandHandler(service);
        var command = new AllocateGroupReceiptCommand
        {
            TenantId = Guid.NewGuid(),
            GroupReceiptId = Guid.NewGuid(),
            CompanyId = Guid.NewGuid(),
            Amount = new Money(3000m, "ZAR"),
        };

        await handler.HandleAsync(command);

        await service.Received(1).AllocateAsync(
            command.TenantId, command.GroupReceiptId, command.CompanyId,
            command.CustomerPartnerId, command.Amount, command.TargetInvoiceIds,
            Arg.Any<CancellationToken>());
    }
}

public class ReverseGroupReceiptCommandHandlerTests
{
    [Fact]
    public async Task HandleAsync_dispatches_reversal()
    {
        var service = Substitute.For<IGroupReceiptService>();

        var handler = new ReverseGroupReceiptCommandHandler(service);
        var command = new ReverseGroupReceiptCommand
        {
            TenantId = Guid.NewGuid(),
            GroupReceiptId = Guid.NewGuid(),
        };

        await handler.HandleAsync(command);

        await service.Received(1).ReverseAsync(
            command.TenantId, command.GroupReceiptId,
            Arg.Any<CancellationToken>());
    }
}

public class GetUnallocatedGroupReceiptsQueryHandlerTests
{
    [Fact]
    public async Task HandleAsync_returns_unallocated_receipts()
    {
        var repository = Substitute.For<IGroupReceiptRepository>();
        var receipts = new List<GroupReceipt>
        {
            GroupReceipt.Capture(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                new Money(5000m, "ZAR"), "EFT", "REF-001", DateTimeOffset.UtcNow),
        };
        repository.GetUnallocatedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(receipts);

        var handler = new GetUnallocatedGroupReceiptsQueryHandler(repository);
        var query = new GetUnallocatedGroupReceiptsQuery { TenantId = Guid.NewGuid() };

        var result = await handler.HandleAsync(query);

        Assert.Single(result);
        await repository.Received(1).GetUnallocatedAsync(query.TenantId, Arg.Any<CancellationToken>());
    }
}

public class ConsolidationServiceTests
{
    [Fact]
    public async Task GetTrialBalanceAsync_returns_watermarked_result()
    {
        var fanOut = Substitute.For<ICompanyFanOut>();
        var repository = Substitute.For<IGroupReceiptRepository>();

        fanOut.GetPeriodFiguresAsync(
            Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new List<CompanyPeriodFigure>());

        var service = new ConsolidationService(fanOut, repository);

        var result = await service.GetTrialBalanceAsync(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.Today));

        Assert.Contains("Consolidated", result.Watermark);
        Assert.Contains("management information", result.Watermark);
        Assert.Contains("not a statutory statement", result.Watermark);
    }

    [Fact]
    public async Task GetIncomeStatementAsync_eliminates_clearing_accounts()
    {
        var fanOut = Substitute.For<ICompanyFanOut>();
        var repository = Substitute.For<IGroupReceiptRepository>();

        fanOut.GetPeriodFiguresAsync(
            Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new List<CompanyPeriodFigure>
            {
                new()
                {
                    CompanyId = Guid.NewGuid(),
                    CompanyName = "Company A",
                    AsAt = DateTimeOffset.UtcNow,
                    Accounts = new List<CompanyAccountBalance>
                    {
                        new() { AccountCode = "ICCLR-001", AccountName = "Clearing A-B", AccountType = "Asset",
                            Debit = new Money(3000m, "ZAR"), Credit = Money.Zero("ZAR") },
                        new() { AccountCode = "4000", AccountName = "Sales", AccountType = "Income",
                            Debit = Money.Zero("ZAR"), Credit = new Money(10000m, "ZAR") },
                    },
                },
            });

        var service = new ConsolidationService(fanOut, repository);

        var result = await service.GetIncomeStatementAsync(
            Guid.NewGuid(), DateOnly.FromDateTime(DateTime.Today.AddDays(-30)),
            DateOnly.FromDateTime(DateTime.Today));

        // Clearing account eliminated
        Assert.DoesNotContain(result.IncomeAccounts, a => a.AccountCode.StartsWith("ICCLR"));
        // Sales account present
        Assert.Contains(result.IncomeAccounts, a => a.AccountCode == "4000");
    }

    [Fact]
    public async Task GetTrialBalanceAsync_names_stale_contributors()
    {
        var fanOut = Substitute.For<ICompanyFanOut>();
        var repository = Substitute.For<IGroupReceiptRepository>();

        fanOut.GetPeriodFiguresAsync(
            Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new List<CompanyPeriodFigure>
            {
                new()
                {
                    CompanyId = Guid.NewGuid(),
                    CompanyName = "Company A",
                    AsAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                    IsStale = false,
                    Accounts = [],
                },
                new()
                {
                    CompanyId = Guid.NewGuid(),
                    CompanyName = "Company B",
                    AsAt = DateTimeOffset.UtcNow.AddHours(-2),
                    IsStale = true,
                    StaleReason = "Company database unreachable",
                    Accounts = [],
                },
            });

        var service = new ConsolidationService(fanOut, repository);

        var result = await service.GetTrialBalanceAsync(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.Today));

        Assert.Equal(2, result.Contributors.Count);
        Assert.Single(result.Contributors, c => !c.IsStale);
        Assert.Single(result.Contributors, c => c.IsStale);
        Assert.Contains("unreachable", result.Contributors.First(c => c.IsStale).StaleReason!);
    }
}
