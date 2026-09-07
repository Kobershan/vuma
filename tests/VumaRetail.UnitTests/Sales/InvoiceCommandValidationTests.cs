using VumaRetail.Application.Abstractions.Sales;
using VumaRetail.Application.Sales.Commands.Invoices;
using VumaRetail.Domain.Sales.Invoices;

namespace VumaRetail.UnitTests.Sales;

/// <summary>
/// Malformed invoice commands are refused before they reach a handler — and therefore before any
/// database is opened.
/// </summary>
public sealed class InvoiceCommandValidationTests
{
    [Fact]
    public void Creating_an_invoice_without_lines_is_refused()
    {
        var validator = new CreateInvoiceCommandValidator();

        var result = validator.Validate(new CreateInvoiceCommand(
            Guid.NewGuid(), "ZAR", Guid.NewGuid(), InvoiceSourceType.Order,
            Array.Empty<InvoiceLineInput>()));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Generating_invoices_without_segments_is_refused()
    {
        var validator = new GenerateInvoicesFromOrderCommandValidator();

        var result = validator.Validate(new GenerateInvoicesFromOrderCommand(
            Guid.NewGuid(), "SO-2026-000412", InvoiceSourceType.Order, Guid.NewGuid(),
            Guid.NewGuid(), "ZAR", Array.Empty<InvoiceCompanySegment>(),
            "SO-2026-000412", "key-1", "user:1"));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void A_line_input_with_no_pack_size_fails_validation_before_any_snapshot_is_checked()
    {
        var validator = new CreateInvoiceCommandValidator();

        var result = validator.Validate(new CreateInvoiceCommand(
            Guid.NewGuid(), "ZAR", Guid.NewGuid(), InvoiceSourceType.Order,
            new[]
            {
                new InvoiceLineInput(
                    Guid.NewGuid(), null, 1m, "EA", 100m, 0m, 15m, string.Empty),
            }));

        result.IsValid.Should().BeFalse();
    }
}
