using VumaRetail.Application.Sales.Commands.Analytics;
using VumaRetail.Application.Sales.Commands.Invoices;
using VumaRetail.Application.Sales.Commands.Quotes;
using VumaRetail.Domain.Sales.Invoices;

namespace VumaRetail.UnitTests.Sales;

/// <summary>
/// Every 10c command's validator refuses the malformed shape before any database is opened.
/// </summary>
public sealed class SalesDocumentValidationTests
{
    [Fact]
    public void Quote_lifecycle_commands_need_their_quote()
    {
        new IssueQuoteCommandValidator().Validate(new IssueQuoteCommand(Guid.Empty)).IsValid.Should().BeFalse();
        new AcceptQuoteCommandValidator().Validate(new AcceptQuoteCommand(Guid.Empty)).IsValid.Should().BeFalse();
        new RejectQuoteCommandValidator().Validate(new RejectQuoteCommand(Guid.Empty)).IsValid.Should().BeFalse();
        new ExpireQuoteCommandValidator().Validate(new ExpireQuoteCommand(Guid.Empty)).IsValid.Should().BeFalse();
        new ConvertQuoteToOrderCommandValidator().Validate(new ConvertQuoteToOrderCommand(Guid.Empty)).IsValid.Should().BeFalse();
        new ConvertQuoteToSaleCommandValidator().Validate(new ConvertQuoteToSaleCommand(Guid.Empty)).IsValid.Should().BeFalse();

        new IssueQuoteCommandValidator().Validate(new IssueQuoteCommand(Guid.NewGuid())).IsValid.Should().BeTrue();
    }

    [Fact]
    public void CreateQuoteCommand_needs_a_customer_a_currency_and_a_validity_window()
    {
        var validator = new CreateQuoteCommandValidator();
        DateOnly validUntil = new(2026, 10, 7);

        validator.Validate(new CreateQuoteCommand(Guid.Empty, "ZAR", validUntil)).IsValid.Should().BeFalse();
        validator.Validate(new CreateQuoteCommand(Guid.NewGuid(), "ZARR", validUntil)).IsValid.Should().BeFalse();
        validator.Validate(new CreateQuoteCommand(Guid.NewGuid(), "ZAR", validUntil)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void AddQuoteLineCommand_needs_one_sku_a_positive_quantity_and_a_unit()
    {
        var validator = new AddQuoteLineCommandValidator();
        Guid quoteId = Guid.NewGuid();

        validator.Validate(new AddQuoteLineCommand(quoteId, null, null, 1m, "EA")).IsValid.Should().BeFalse();
        validator.Validate(new AddQuoteLineCommand(quoteId, Guid.NewGuid(), Guid.NewGuid(), 1m, "EA")).IsValid.Should().BeFalse();
        validator.Validate(new AddQuoteLineCommand(quoteId, Guid.NewGuid(), null, 0m, "EA")).IsValid.Should().BeFalse();
        validator.Validate(new AddQuoteLineCommand(quoteId, Guid.NewGuid(), null, 1m, "EA")).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Finalize_and_cancel_need_their_invoice()
    {
        new FinalizeInvoiceCommandValidator().Validate(new FinalizeInvoiceCommand(Guid.Empty)).IsValid.Should().BeFalse();
        new CancelInvoiceCommandValidator().Validate(new CancelInvoiceCommand(Guid.Empty)).IsValid.Should().BeFalse();
        new FinalizeInvoiceCommandValidator().Validate(new FinalizeInvoiceCommand(Guid.NewGuid())).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Generate_needs_a_source_a_customer_and_an_idempotency_key()
    {
        var validator = new GenerateInvoicesFromOrderCommandValidator();
        var segments = new[]
        {
            new Application.Abstractions.Sales.InvoiceCompanySegment(
                Guid.NewGuid(),
                new[]
                {
                    new Application.Abstractions.Sales.InvoiceLineInput(
                        Guid.NewGuid(), null, 1m, "EA", 100m, 0m, 15m, "Each"),
                }),
        };

        validator.Validate(new GenerateInvoicesFromOrderCommand(
            Guid.Empty, "SO-1", InvoiceSourceType.Order, Guid.NewGuid(), Guid.NewGuid(), "ZAR",
            segments, null, "key-1", "user:1")).IsValid.Should().BeFalse();

        validator.Validate(new GenerateInvoicesFromOrderCommand(
            Guid.NewGuid(), "SO-1", InvoiceSourceType.Order, Guid.NewGuid(), Guid.NewGuid(), "ZAR",
            segments, null, string.Empty, "user:1")).IsValid.Should().BeFalse();

        validator.Validate(new GenerateInvoicesFromOrderCommand(
            Guid.NewGuid(), "SO-1", InvoiceSourceType.Order, Guid.NewGuid(), Guid.NewGuid(), "ZAR",
            segments, null, "key-1", "user:1")).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Rebuild_needs_a_forward_window()
    {
        var validator = new RebuildAnalyticsCommandValidator();
        DateTimeOffset from = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

        validator.Validate(new RebuildAnalyticsCommand(null, from, from)).IsValid.Should().BeFalse();
        validator.Validate(new RebuildAnalyticsCommand(null, from, from.AddDays(1))).IsValid.Should().BeTrue();
    }
}
