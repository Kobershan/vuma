using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Identity;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Pos;
using VumaRetail.Application.Pos.Commands;
using VumaRetail.Domain.Licensing;
using VumaRetail.Domain.Pos;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.UnitTests.Pos;

/// <summary>
/// §4.10's "cannot originate trade" bound on the reprint exemption. The guard passes
/// <see cref="RecordReceiptPrintCommand"/> through unconditionally while read-only
/// (<c>ReadOnlyExemption.ReceiptReprint</c>), so this handler is the only place left that can still
/// refuse the one case the exemption's own argument does not cover: a first print of a sale that has
/// not completed yet.
/// </summary>
public sealed class RecordReceiptPrintCommandHandlerTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();
    private static readonly Guid StoreId = UuidV7.NewGuid();
    private static readonly Guid TerminalId = UuidV7.NewGuid();
    private static readonly Guid OperatorId = UuidV7.NewGuid();
    private static readonly Guid LocationId = UuidV7.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 9, 0, 0, TimeSpan.Zero);

    private readonly ISaleRepository _sales = Substitute.For<ISaleRepository>();
    private readonly IReceiptPrintRepository _prints = Substitute.For<IReceiptPrintRepository>();
    private readonly IPermissionChecker _permissions = Substitute.For<IPermissionChecker>();
    private readonly IPrincipalAccessor _principal = Substitute.For<IPrincipalAccessor>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly IEnforcementStatusReader _entitlements = Substitute.For<IEnforcementStatusReader>();

    public RecordReceiptPrintCommandHandlerTests()
    {
        _principal.Principal.Returns($"user:{OperatorId}");
        _principal.TerminalId.Returns(TerminalId);
        _clock.UtcNow.Returns(Now);
        _permissions
            .HasPermissionAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
    }

    private RecordReceiptPrintCommandHandler Handler
        => new(_sales, _prints, _permissions, _principal, _clock, _entitlements);

    private static Sale OpenSale()
    {
        TillSession session = TillSession.Open(TenantId, StoreId, TerminalId, OperatorId, new Money(500m, "ZAR"), Now);

        return Sale.Open(
            UuidV7.NewGuid(), TenantId, StoreId, "SALE-000001", session, OperatorId, LocationId,
            customerId: null, "ZAR", Now);
    }

    private void ReadOnly()
        => _entitlements.CurrentLevel(Arg.Any<CancellationToken>()).Returns(new EnforcementDecision(
            EnforcementLevel.ReadOnly, EnforcementReason.SubscriptionLapsed, NoticeStage.FinalNotice, Now));

    [Fact]
    public async Task A_first_print_of_an_open_sale_is_refused_while_read_only()
    {
        // The in-flight case — belongs to the session-scoped mechanism, not to this exemption's
        // "cannot originate trade" argument, which only holds for a sale that already exists in full.
        Sale sale = OpenSale();
        _sales.FindAsync(sale.Id, Arg.Any<CancellationToken>()).Returns(sale);
        _prints.CountForSaleAsync(sale.Id, Arg.Any<CancellationToken>()).Returns(0);
        ReadOnly();

        Func<Task> printing = () => Handler.HandleAsync(new RecordReceiptPrintCommand(sale.Id));

        await printing.Should().ThrowAsync<LicenceReadOnlyException>();
        _prints.DidNotReceive().Add(Arg.Any<ReceiptPrint>());
    }

    [Fact]
    public async Task A_reprint_of_a_completed_sale_succeeds_while_read_only()
    {
        // The exemption's actual case: the sale already exists in full, and appending to the print log
        // cannot create a sale, move money, move stock or consume a document number.
        Sale sale = OpenSale();

        sale.AddLine(SaleLine.Ring(
            TenantId, StoreId, sale.Id, 1, UuidV7.NewGuid(), null, "Full cream milk 2L",
            new Quantity(1m, "EA"), new Money(115m, "ZAR"), Money.Zero("ZAR"),
            "STANDARD", new Money(100m, "ZAR"), new Money(15m, "ZAR"), new Money(115m, "ZAR")));
        sale.AddTender(SaleTender.Capture(TenantId, StoreId, sale.Id, TenderType.Cash, new Money(115m, "ZAR"), null, Now));
        sale.Complete(Now);

        _sales.FindAsync(sale.Id, Arg.Any<CancellationToken>()).Returns(sale);
        _prints.CountForSaleAsync(sale.Id, Arg.Any<CancellationToken>()).Returns(1);
        ReadOnly();

        Guid printId = await Handler.HandleAsync(new RecordReceiptPrintCommand(sale.Id, "Customer lost the original"));

        printId.Should().NotBeEmpty();
        _prints.Received(1).Add(Arg.Is<ReceiptPrint>(print => print.IsReprint));
    }
}
