using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VumaRetail.Application.Catalog.Commands;
using VumaRetail.Application.Inventory.Commands;
using VumaRetail.Application.Sales.Commands;
using VumaRetail.Application.Warehouse.Commands;
using VumaRetail.Domain.Catalog;
using VumaRetail.Domain.Finance;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Sales;
using VumaRetail.Domain.Warehouse;
using VumaRetail.Finance.Commands;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.IntegrationTests.Api;

namespace VumaRetail.IntegrationTests.Orders;

/// <summary>What a scenario needs before it can raise an order: a location, two items (one with bin
/// stock, one with none), a price for each, and the two posting rules this stage registers.</summary>
/// <param name="LocationId">The Stage 08 location every order in the scenario ships from.</param>
/// <param name="ZoneId">The one zone at that location.</param>
/// <param name="BinId">A bin in that zone, already holding the in-stock item.</param>
/// <param name="SpareBinId">A second, empty bin — where a top-up receipt gets shelved.</param>
/// <param name="InStockItemId">An item with bin stock on hand.</param>
/// <param name="OutOfStockItemId">An item with no stock anywhere — guaranteed to backorder.</param>
internal sealed record OrdersScenario(
    Guid LocationId, Guid ZoneId, Guid BinId, Guid SpareBinId, Guid InStockItemId, Guid OutOfStockItemId);

/// <summary>Builds the reference data every orders integration test needs, through the real dispatcher.</summary>
internal static class OrdersHarnessSetup
{
    public static async Task<OrdersScenario> BuildAsync(ApiHarness harness)
    {
        ArgumentNullException.ThrowIfNull(harness);

        Guid each = await harness.SendAsync(new CreateUnitOfMeasureCommand("EA", "Each", UnitOfMeasureType.Count));

        Guid inStockItem = await harness.SendAsync(
            new CreateItemCommand("MILK-2L", "Full cream milk 2L", ItemType.Stock, each, TaxClassCode: "STANDARD"));
        Guid outOfStockItem = await harness.SendAsync(
            new CreateItemCommand("GADGET", "Red gadget", ItemType.Stock, each, TaxClassCode: "STANDARD"));

        Guid locationId = await harness.SendAsync(new CreateStockLocationCommand("MAIN", "Main warehouse", StockLocationType.Warehouse));

        DateOnly today = DateOnly.FromDateTime(harness.Clock.UtcNow.UtcDateTime);

        await harness.SendAsync(new CreateTaxRuleCommand(
            "STANDARD", "South African VAT, standard rate", 0.15m, TaxTreatment.Inclusive, today.AddYears(-1)));

        Guid priceListId = await harness.SendAsync(new CreatePriceListCommand(
            "RETAIL", "Shelf prices", "ZAR", PriceListKind.Retail, PricesIncludeTax: true, Priority: 0, today.AddYears(-1)));
        await harness.SendAsync(new SetPriceListLineCommand(priceListId, inStockItem, null, new Money(59.99m, "ZAR")));
        await harness.SendAsync(new SetPriceListLineCommand(priceListId, outOfStockItem, null, new Money(249.00m, "ZAR")));

        await BuildFinanceAsync(harness, today);

        Guid zoneId = await harness.SendAsync(new CreateZoneCommand(locationId, "STOR-A", "Storage aisle A", ZoneType.Storage));
        Guid binId = await harness.SendAsync(new CreateBinCommand(zoneId, "A-01", "Aisle A, shelf 1", BinType.Shelf));
        Guid spareBinId = await harness.SendAsync(new CreateBinCommand(zoneId, "A-02", "Aisle A, shelf 2", BinType.Shelf));

        await harness.SendAsync(new ReceiveStockCommand(
            locationId, inStockItem, null, new Quantity(50m, "EA"), new Money(30m, "ZAR"), "Opening stock"));

        Guid putawayId = await harness.SendAsync(new OpenPutawayTaskCommand(
            locationId, inStockItem, null, new Quantity(50m, "EA"), PutawaySourceReferenceType.ManualReceipt));
        await harness.SendAsync(new ConfirmPutawayCommand(putawayId, binId, new Quantity(50m, "EA")));

        return new OrdersScenario(locationId, zoneId, binId, spareBinId, inStockItem, outOfStockItem);
    }

    private static async Task BuildFinanceAsync(ApiHarness harness, DateOnly today)
    {
        Guid debtors = await harness.SendAsync(new CreateAccountCommand(
            "1100", "Trade debtors", AccountType.Asset, "ZAR", ControlAccountType.AccountsReceivable));
        Guid sales = await harness.SendAsync(new CreateAccountCommand("4000", "Sales", AccountType.Revenue, "ZAR", ControlAccountType.None));
        Guid salesReturns = await harness.SendAsync(
            new CreateAccountCommand("4010", "Sales returns", AccountType.Revenue, "ZAR", ControlAccountType.None));
        Guid vatControl = await harness.SendAsync(
            new CreateAccountCommand("2200", "VAT control", AccountType.Liability, "ZAR", ControlAccountType.None));
        Guid inventory = await harness.SendAsync(
            new CreateAccountCommand("1300", "Inventory on hand", AccountType.Asset, "ZAR", ControlAccountType.None));
        Guid costOfSales = await harness.SendAsync(
            new CreateAccountCommand("5000", "Cost of sales", AccountType.Expense, "ZAR", ControlAccountType.None));
        Guid grni = await harness.SendAsync(
            new CreateAccountCommand("2150", "Goods received not invoiced", AccountType.Liability, "ZAR", ControlAccountType.None));

        await harness.SendAsync(new OpenAccountingPeriodCommand(new DateOnly(today.Year, today.Month, 1), today.AddMonths(2)));

        await harness.SendAsync(new DefinePostingRuleCommand(
            "inventory.receipt.posted",
            [
                new PostingRuleLineInput(inventory, NormalBalance.Debit, "Value", InheritDimensions: false, "Inventory on hand"),
                new PostingRuleLineInput(grni, NormalBalance.Credit, "Value", InheritDimensions: false, "Goods received not invoiced"),
            ],
            "Stock received"));

        await harness.SendAsync(new DefinePostingRuleCommand(
            "inventory.sale.issued",
            [
                new PostingRuleLineInput(costOfSales, NormalBalance.Debit, "Value", InheritDimensions: true, "Cost of sales"),
                new PostingRuleLineInput(inventory, NormalBalance.Credit, "Value", InheritDimensions: false, "Inventory on hand"),
            ],
            "Stock issued for a shipment"));

        await harness.SendAsync(new DefinePostingRuleCommand(
            "inventory.sale.returned",
            [
                new PostingRuleLineInput(inventory, NormalBalance.Debit, "Value", InheritDimensions: false, "Inventory on hand"),
                new PostingRuleLineInput(costOfSales, NormalBalance.Credit, "Value", InheritDimensions: true, "Cost of sales"),
            ],
            "Stock back on the shelf from a return"));

        await harness.SendAsync(new DefinePostingRuleCommand(
            "orders.order.fulfilled",
            [
                new PostingRuleLineInput(debtors, NormalBalance.Debit, "Gross", InheritDimensions: false, "Trade debtors"),
                new PostingRuleLineInput(sales, NormalBalance.Credit, "Net", InheritDimensions: true, "Sales"),
                new PostingRuleLineInput(vatControl, NormalBalance.Credit, "Tax", InheritDimensions: false, "Output VAT"),
            ],
            "Order revenue recognised"));

        await harness.SendAsync(new DefinePostingRuleCommand(
            "orders.return.completed",
            [
                new PostingRuleLineInput(salesReturns, NormalBalance.Debit, "Net", InheritDimensions: true, "Sales returns"),
                new PostingRuleLineInput(vatControl, NormalBalance.Debit, "Tax", InheritDimensions: false, "Output VAT reversed"),
                new PostingRuleLineInput(debtors, NormalBalance.Credit, "Gross", InheritDimensions: false, "Trade debtors"),
            ],
            "Order return refund due"));
    }

    /// <summary>Reads every journal raised for one event type, newest first — a direct DbContext read for test assertions.</summary>
    public static Task<List<Journal>> JournalsForEventTypeAsync(ApiHarness harness, string eventType)
        => harness.InScopeAsync(provider =>
        {
            VumaRetailDbContext context = provider.GetRequiredService<VumaRetailDbContext>();

            return context.Journals
                .Include(journal => journal.Lines)
                .Where(journal => journal.SourceEventType == eventType)
                .OrderByDescending(journal => journal.CreatedAt)
                .ToListAsync();
        });
}
