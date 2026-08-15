using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Catalog.Commands;
using VumaRetail.Application.Identity;
using VumaRetail.Application.Identity.Commands;
using VumaRetail.Application.Identity.Permissions;
using VumaRetail.Application.Inventory.Commands;
using VumaRetail.Application.Partners.Commands;
using VumaRetail.Application.Abstractions.Finance;
using VumaRetail.Application.Pos;
using VumaRetail.Application.Pos.Commands;
using VumaRetail.Domain.Pos;
using VumaRetail.Domain.Catalog;
using VumaRetail.Domain.Inventory;
using VumaRetail.Domain.Finance;
using VumaRetail.Domain.Identity;
using VumaRetail.Domain.Licensing;
using VumaRetail.Domain.Partners;
using VumaRetail.Domain.Platform;
using VumaRetail.Domain.Primitives;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Finance.Commands;
using VumaRetail.Licensing.Commands;
using VumaRetail.Licensing.Control;

namespace VumaRetail.StoreServer;

/// <summary>
/// Builds a demonstrable tenant: two stores, three roles, staff with PINs, and an enrolled terminal.
/// </summary>
/// <remarks>
/// <para>
/// <c>docs/TESTING.md</c> §5 — every stage extends the seed so the whole system stays demonstrable,
/// and the seed doubles as the dataset for the Stage 31 DR drill. Stage 02 starts it with the only
/// things that exist: the platform root and identity.
/// </para>
/// <para>
/// Idempotent. Running it twice must not create a second copy of anything, because the first thing
/// anybody does with a seed script is run it again to see what it did.
/// </para>
/// </remarks>
public static class DemoSeed
{
    /// <summary>The demo tenant's fixed id, so a re-run finds what the last run made.</summary>
    public static readonly Guid DemoTenantId = Guid.Parse("01900000-0000-7000-8000-0000000000d0");

    /// <summary>Seeds the demo tenant.</summary>
    /// <param name="services">The host's service provider.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    public static async Task RunAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        using IServiceScope scope = services.CreateScope();
        IServiceProvider provider = scope.ServiceProvider;

        VumaRetailDbContext context = provider.GetRequiredService<VumaRetailDbContext>();
        ITenantContext tenantContext = provider.GetRequiredService<ITenantContext>();
        IUnitOfWork unitOfWork = provider.GetRequiredService<IUnitOfWork>();

        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);

        Tenant tenant = await EnsureTenantAsync(context, tenantContext, unitOfWork, cancellationToken).ConfigureAwait(false);
        tenantContext.SetTenant(tenant.Id);

        Store johannesburg = await EnsureStoreAsync(context, unitOfWork, tenant.Id, "JHB01", "Vuma Sandton", cancellationToken)
            .ConfigureAwait(false);
        await EnsureStoreAsync(context, unitOfWork, tenant.Id, "CPT02", "Vuma Claremont", cancellationToken)
            .ConfigureAwait(false);

        // Before anything else goes through the pipeline. Stage 04b's read-only guard refuses every
        // business write on an unactivated installation, which is correct in production and would
        // otherwise mean the demo seeder produced a tenant with no users in it.
        await EnsureActivationAsync(provider, tenant.Id, johannesburg.Id, cancellationToken)
            .ConfigureAwait(false);

        Guid ownerRole = await EnsureRoleAsync(provider, "Owner", AllPermissions(provider), cancellationToken)
            .ConfigureAwait(false);

        Guid managerRole = await EnsureRoleAsync(
            provider,
            "Store Manager",
            [
                PlatformPermissions.StoreView,
                PlatformPermissions.AuditView,
                IdentityPermissions.UserView,
                IdentityPermissions.UserManage,
                IdentityPermissions.RoleView,
                IdentityPermissions.TerminalView,
                IdentityPermissions.TerminalEnrol,
            ],
            cancellationToken).ConfigureAwait(false);

        Guid cashierRole = await EnsureRoleAsync(
            provider,
            "Cashier",
            [PlatformPermissions.StoreView],
            cancellationToken).ConfigureAwait(false);

        await EnsureUserAsync(provider, "owner", "Thandi Mokoena", "ChangeMe-Owner-2026", ownerRole, null, null, cancellationToken)
            .ConfigureAwait(false);

        await EnsureUserAsync(provider, "manager", "Riaan de Villiers", "ChangeMe-Manager-2026", managerRole, johannesburg.Id, "4821", cancellationToken)
            .ConfigureAwait(false);

        await EnsureUserAsync(provider, "cashier1", "Naledi Dlamini", "ChangeMe-Cashier-2026", cashierRole, johannesburg.Id, "1174", cancellationToken)
            .ConfigureAwait(false);

        await EnsureTerminalAsync(provider, context, johannesburg.Id, "T01", "Front counter 1", cancellationToken)
            .ConfigureAwait(false);

        // Stage 06: master data. Two base units, one derived; an item with no variants and one with
        // two, so the demo shows both of Barcode's attachment rules; and two partners, one of each type.
        Guid each = await EnsureUnitOfMeasureAsync(provider, context, "EA", "Each", UnitOfMeasureType.Count, cancellationToken)
            .ConfigureAwait(false);
        await EnsureUnitOfMeasureAsync(provider, context, "KG", "Kilogram", UnitOfMeasureType.Weight, cancellationToken)
            .ConfigureAwait(false);
        await EnsureDerivedUnitOfMeasureAsync(provider, context, "BOX12", "Box of 12", each, 12m, cancellationToken)
            .ConfigureAwait(false);

        Guid milk = await EnsureItemAsync(
            provider, context, "MILK-2L", "Full cream milk 2L", ItemType.Stock, each, "Fresh full cream milk, 2 litre bottle", "STANDARD", cancellationToken)
            .ConfigureAwait(false);
        await EnsureBarcodeAsync(provider, context, milk, null, "6009880123456", BarcodeSymbology.Ean13, cancellationToken)
            .ConfigureAwait(false);

        Guid shirt = await EnsureItemAsync(
            provider, context, "SHIRT", "Vuma branded T-shirt", ItemType.Stock, each, "Cotton crew-neck T-shirt", "STANDARD", cancellationToken)
            .ConfigureAwait(false);
        Guid shirtMedRed = await EnsureVariantAsync(
            provider, context, shirt, "SHIRT-M-RED", [new VariantAttribute("Size", "M"), new VariantAttribute("Colour", "Red")], cancellationToken)
            .ConfigureAwait(false);
        Guid shirtLgeBlue = await EnsureVariantAsync(
            provider, context, shirt, "SHIRT-L-BLUE", [new VariantAttribute("Size", "L"), new VariantAttribute("Colour", "Blue")], cancellationToken)
            .ConfigureAwait(false);
        await EnsureBarcodeAsync(provider, context, null, shirtMedRed, "6009880234561", BarcodeSymbology.Ean13, cancellationToken)
            .ConfigureAwait(false);
        await EnsureBarcodeAsync(provider, context, null, shirtLgeBlue, "6009880234578", BarcodeSymbology.Ean13, cancellationToken)
            .ConfigureAwait(false);

        await EnsurePartnerAsync(
            provider, context, "FRESHFARM", "Fresh Farm Distributors", PartnerType.Supplier, "orders@freshfarm.example", cancellationToken)
            .ConfigureAwait(false);
        await EnsurePartnerAsync(
            provider, context, "CORPCLIENT", "Corporate Client (Pty) Ltd", PartnerType.Customer, "accounts@corpclient.example", cancellationToken)
            .ConfigureAwait(false);

        await SeedFinanceAsync(provider, context, cancellationToken).ConfigureAwait(false);
        await SeedInventoryAsync(provider, context, milk, cancellationToken).ConfigureAwait(false);
        await SeedPosAsync(provider, context, johannesburg.Id, milk, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Seeds Stage 08: two stock locations, the accounts stock movements post to, the posting rules
    /// that connect them, and an opening receipt so the demo has stock on hand.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two locations rather than one, because a transfer needs somewhere to go — a single-location
    /// demo cannot exercise the one movement type that posts two correlated ledger entries.
    /// </para>
    /// <para>
    /// <b>No rule is seeded for the two transfer event types.</b> That is the demonstration, not an
    /// omission: with one inventory account a transfer's two sides would debit and credit the same
    /// account for the same amount, so the correct posting is no posting at all. A tenant running
    /// per-location inventory accounts defines the rules and the same events start posting, with no
    /// code change. It also exercises the path where a movement is recorded with no posting rule
    /// configured, which logs a warning and leaves the stock ledger correct (ADR-070).
    /// </para>
    /// </remarks>
    private static async Task SeedInventoryAsync(
        IServiceProvider provider,
        VumaRetailDbContext context,
        Guid itemId,
        CancellationToken cancellationToken)
    {
        Guid inventory = await EnsureAccountAsync(
            provider, context, "1300", "Inventory on hand", AccountType.Asset,
            ControlAccountType.None, cancellationToken).ConfigureAwait(false);
        // Goods received but not yet invoiced. A receipt increases stock before the supplier's invoice
        // arrives, and the credit has to land somewhere until Stage 12's three-way match clears it.
        Guid grni = await EnsureAccountAsync(
            provider, context, "2150", "Goods received not invoiced", AccountType.Liability,
            ControlAccountType.None, cancellationToken).ConfigureAwait(false);
        Guid costOfSales = await EnsureAccountAsync(
            provider, context, "5000", "Cost of sales", AccountType.Expense,
            ControlAccountType.None, cancellationToken).ConfigureAwait(false);
        Guid stockAdjustments = await EnsureAccountAsync(
            provider, context, "5100", "Stock adjustments", AccountType.Expense,
            ControlAccountType.None, cancellationToken).ConfigureAwait(false);
        // Separate from adjustments on purpose: a documented write-off and an unexplained count
        // difference are different questions to a business, and merging them hides the second.
        Guid shrinkage = await EnsureAccountAsync(
            provider, context, "5200", "Stock shrinkage", AccountType.Expense,
            ControlAccountType.None, cancellationToken).ConfigureAwait(false);

        await EnsurePostingRuleAsync(
            provider, context, "inventory.receipt.posted", "Stock received",
            [
                new PostingRuleLineInput(inventory, NormalBalance.Debit, "Value", InheritDimensions: false, "Inventory on hand"),
                new PostingRuleLineInput(grni, NormalBalance.Credit, "Value", InheritDimensions: false, "Goods received not invoiced"),
            ],
            cancellationToken).ConfigureAwait(false);

        await EnsurePostingRuleAsync(
            provider, context, "inventory.sale.issued", "Stock issued for a sale",
            [
                new PostingRuleLineInput(costOfSales, NormalBalance.Debit, "Value", InheritDimensions: true, "Cost of sales"),
                new PostingRuleLineInput(inventory, NormalBalance.Credit, "Value", InheritDimensions: false, "Inventory on hand"),
            ],
            cancellationToken).ConfigureAwait(false);

        await EnsurePostingRuleAsync(
            provider, context, "inventory.adjustment.decrease", "Stock written off",
            [
                new PostingRuleLineInput(stockAdjustments, NormalBalance.Debit, "Value", InheritDimensions: true, "Stock adjustments"),
                new PostingRuleLineInput(inventory, NormalBalance.Credit, "Value", InheritDimensions: false, "Inventory on hand"),
            ],
            cancellationToken).ConfigureAwait(false);

        await EnsurePostingRuleAsync(
            provider, context, "inventory.adjustment.increase", "Stock written on",
            [
                new PostingRuleLineInput(inventory, NormalBalance.Debit, "Value", InheritDimensions: false, "Inventory on hand"),
                new PostingRuleLineInput(stockAdjustments, NormalBalance.Credit, "Value", InheritDimensions: true, "Stock adjustments"),
            ],
            cancellationToken).ConfigureAwait(false);

        await EnsurePostingRuleAsync(
            provider, context, "inventory.stocktake.shortage", "Stocktake counted less than the system expected",
            [
                new PostingRuleLineInput(shrinkage, NormalBalance.Debit, "Value", InheritDimensions: true, "Stock shrinkage"),
                new PostingRuleLineInput(inventory, NormalBalance.Credit, "Value", InheritDimensions: false, "Inventory on hand"),
            ],
            cancellationToken).ConfigureAwait(false);

        await EnsurePostingRuleAsync(
            provider, context, "inventory.stocktake.surplus", "Stocktake counted more than the system expected",
            [
                new PostingRuleLineInput(inventory, NormalBalance.Debit, "Value", InheritDimensions: false, "Inventory on hand"),
                new PostingRuleLineInput(shrinkage, NormalBalance.Credit, "Value", InheritDimensions: true, "Stock shrinkage"),
            ],
            cancellationToken).ConfigureAwait(false);

        Guid warehouse = await EnsureStockLocationAsync(
            provider, context, "MAIN", "Sandton back room", StockLocationType.Warehouse, cancellationToken)
            .ConfigureAwait(false);
        await EnsureStockLocationAsync(
            provider, context, "FLOOR", "Sandton sales floor", StockLocationType.SalesFloor, cancellationToken)
            .ConfigureAwait(false);

        // One opening receipt, so a freshly seeded demo has stock on hand to sell, transfer and count
        // rather than an empty ledger that refuses every outbound movement.
        if (!await context.StockLedgerEntries.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            await provider
                .GetRequiredService<IDispatcher>()
                .SendAsync(
                    new ReceiveStockCommand(
                        warehouse,
                        itemId,
                        null,
                        new Quantity(40m, "EA"),
                        new Money(42.50m, "ZAR"),
                        "Opening stock"),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Seeds Stage 09: an open till session on the demo terminal, and one completed sale through it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Built from the domain factories and <see cref="ISaleCompletionService"/> rather than through the
    /// dispatcher, unlike most of this seed. POS attributes every action to the authenticated operator
    /// and the originating terminal (<c>PosActor</c>), and the seed runs as a system principal with no
    /// terminal — so dispatching <c>OpenSaleCommand</c> here would be refused, correctly. Going through
    /// the completion service still exercises the parts that matter: the stock comes off the ledger and
    /// the <c>pos.sale.tendered</c> event posts through the rule seeded in <see cref="SeedFinanceAsync"/>.
    /// </para>
    /// <para>
    /// The sale is rung up against <c>MAIN</c>, which is where <see cref="SeedInventoryAsync"/>'s
    /// opening receipt put the stock. Selling from <c>FLOOR</c> would demonstrate ADR-073's refused-issue
    /// path instead, which is a real thing to look at but a strange default for a demo — the first
    /// question anybody asks of a seeded database is "does a normal sale work".
    /// </para>
    /// </remarks>
    private static async Task SeedPosAsync(
        IServiceProvider provider,
        VumaRetailDbContext context,
        Guid storeId,
        Guid itemId,
        CancellationToken cancellationToken)
    {
        if (await context.Sales.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        Terminal? terminal = await provider.GetRequiredService<ITerminalRepository>()
            .FindByCodeAsync(storeId, "T01", cancellationToken)
            .ConfigureAwait(false);

        User? cashier = await provider.GetRequiredService<IUserRepository>()
            .FindByUserNameAsync("cashier1", cancellationToken)
            .ConfigureAwait(false);

        StockLocation? location = await context.StockLocations
            .FirstOrDefaultAsync(candidate => candidate.Code == "MAIN", cancellationToken)
            .ConfigureAwait(false);

        if (terminal is null || cashier is null || location is null)
        {
            return;
        }

        IClock clock = provider.GetRequiredService<IClock>();

        TillSession session = TillSession.Open(
            DemoTenantId, storeId, terminal.Id, cashier.Id, new Money(500m, "ZAR"), clock.UtcNow);

        provider.GetRequiredService<ITillSessionRepository>().Add(session);

        string saleNumber = await provider.GetRequiredService<IDocumentNumberSequence>()
            .NextAsync(OpenSaleCommandHandler.SaleNumberSeries, cancellationToken)
            .ConfigureAwait(false);

        Sale sale = Sale.Open(
            UuidV7.NewGuid(),
            DemoTenantId,
            storeId,
            saleNumber,
            session,
            cashier.Id,
            location.Id,
            customerId: null,
            "ZAR",
            clock.UtcNow);

        // R59.99 each, two of them, priced through the rules engine exactly as a till would — the
        // STANDARD rule SeedFinanceAsync writes is 15% inclusive, so R119.98 gross is R104.33 net.
        Money unitPrice = new(59.99m, "ZAR");
        Quantity quantity = new(2m, "EA");

        TaxCalculation tax = await provider.GetRequiredService<ITaxCalculator>()
            .CalculateAsync(
                "STANDARD",
                (unitPrice * quantity.Value).RoundToCurrencyScale(),
                DateOnly.FromDateTime(clock.UtcNow.UtcDateTime),
                cancellationToken)
            .ConfigureAwait(false);

        sale.AddLine(SaleLine.Ring(
            DemoTenantId,
            storeId,
            sale.Id,
            sale.NextLineNumber,
            itemId,
            null,
            "Full cream milk 2L",
            quantity,
            unitPrice,
            Money.Zero("ZAR"),
            tax.TaxCode,
            tax.NetAmount,
            tax.TaxAmount,
            tax.GrossAmount));

        sale.AddTender(SaleTender.Capture(
            DemoTenantId, storeId, sale.Id, TenderType.Cash, new Money(150m, "ZAR"), null, clock.UtcNow));

        provider.GetRequiredService<ISaleRepository>().Add(sale);

        await provider.GetRequiredService<ISaleCompletionService>()
            .CompleteAsync(sale, cancellationToken)
            .ConfigureAwait(false);

        provider.GetRequiredService<IReceiptPrintRepository>().Add(ReceiptPrint.Record(
            DemoTenantId, storeId, sale.Id, cashier.Id, terminal.Id, isReprint: false, reason: null, clock.UtcNow));

        await provider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken).ConfigureAwait(false);

        Console.WriteLine($"Sale {sale.SaleNumber} completed: {sale.Gross} tendered, {sale.ChangeGiven} change.");
    }

    private static async Task<Guid> EnsureStockLocationAsync(
        IServiceProvider provider,
        VumaRetailDbContext context,
        string code,
        string name,
        StockLocationType type,
        CancellationToken cancellationToken)
    {
        if (await context.StockLocations
            .FirstOrDefaultAsync(location => location.Code == code, cancellationToken)
            .ConfigureAwait(false) is { } existing)
        {
            return existing.Id;
        }

        return await provider
            .GetRequiredService<IDispatcher>()
            .SendAsync(new CreateStockLocationCommand(code, name, type), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Seeds Stage 07: a minimal chart of accounts, the current period, the `en-ZA` tax rule, and the
    /// posting rules that make an AR invoice and a till sale post to the ledger.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The chart is deliberately small — six accounts, enough to post a sale and a purchase and to
    /// give AR, AP and the bank their control accounts. A demo chart with a realistic hundred accounts
    /// would be a fiction nobody's real business matches, and every one of them would have to be
    /// maintained here.
    /// </para>
    /// <para>
    /// Exactly one tax rule, `STANDARD` at 15% inclusive, per CLAUDE.md §9 and the stage brief:
    /// seeding a second jurisdiction would suggest the list is meant to be exhaustive rather than a
    /// starting point a tenant edits.
    /// </para>
    /// <para>
    /// A `pos.sale.tendered` rule is seeded even though Stage 09 does not exist yet. It costs one row
    /// and it is the demonstration that rule 12 works: the POS module, when it arrives, raises that
    /// event and posts correctly without Finance or the seed changing.
    /// </para>
    /// </remarks>
    private static async Task SeedFinanceAsync(
        IServiceProvider provider, VumaRetailDbContext context, CancellationToken cancellationToken)
    {
        Guid debtors = await EnsureAccountAsync(
            provider, context, "1100", "Trade debtors", AccountType.Asset,
            ControlAccountType.AccountsReceivable, cancellationToken).ConfigureAwait(false);
        Guid bank = await EnsureAccountAsync(
            provider, context, "1200", "Bank — cheque account", AccountType.Asset,
            ControlAccountType.Bank, cancellationToken).ConfigureAwait(false);
        Guid creditors = await EnsureAccountAsync(
            provider, context, "2100", "Trade creditors", AccountType.Liability,
            ControlAccountType.AccountsPayable, cancellationToken).ConfigureAwait(false);
        // One combined VAT control account rather than separate input and output accounts. That is
        // what a small South African retailer's chart usually looks like — the VAT201 return is
        // prepared from the net position — and splitting it would mean seeding two accounts to
        // demonstrate one rule.
        Guid vatControl = await EnsureAccountAsync(
            provider, context, "2200", "VAT control", AccountType.Liability,
            ControlAccountType.None, cancellationToken).ConfigureAwait(false);
        Guid sales = await EnsureAccountAsync(
            provider, context, "4000", "Sales", AccountType.Revenue,
            ControlAccountType.None, cancellationToken).ConfigureAwait(false);
        Guid purchases = await EnsureAccountAsync(
            provider, context, "5000", "Cost of sales", AccountType.Expense,
            ControlAccountType.None, cancellationToken).ConfigureAwait(false);

        await EnsureCurrentPeriodAsync(provider, context, cancellationToken).ConfigureAwait(false);

        await EnsureTaxRuleAsync(
            provider, context, "STANDARD", "South African VAT, standard rate", 0.15m,
            TaxTreatment.Inclusive, cancellationToken).ConfigureAwait(false);

        await EnsurePostingRuleAsync(
            provider, context, "ar.invoice.posted", "Customer invoice",
            [
                new PostingRuleLineInput(debtors, NormalBalance.Debit, "Gross", InheritDimensions: false, "Trade debtors"),
                new PostingRuleLineInput(sales, NormalBalance.Credit, "Net", InheritDimensions: true, "Sales"),
                new PostingRuleLineInput(vatControl, NormalBalance.Credit, "Tax", InheritDimensions: false, "Output VAT"),
            ],
            cancellationToken).ConfigureAwait(false);

        await EnsurePostingRuleAsync(
            provider, context, "pos.sale.tendered", "Cash sale at the till",
            [
                new PostingRuleLineInput(bank, NormalBalance.Debit, "Gross", InheritDimensions: false, "Bank"),
                new PostingRuleLineInput(sales, NormalBalance.Credit, "Net", InheritDimensions: true, "Sales"),
                new PostingRuleLineInput(vatControl, NormalBalance.Credit, "Tax", InheritDimensions: false, "Output VAT"),
            ],
            cancellationToken).ConfigureAwait(false);

        // The mirror of the AR rule: cost of sales and input VAT are debited, trade creditors
        // credited. Debiting the same VAT control account is what makes it a net position.
        await EnsurePostingRuleAsync(
            provider, context, "ap.invoice.posted", "Supplier invoice",
            [
                new PostingRuleLineInput(purchases, NormalBalance.Debit, "Net", InheritDimensions: true, "Cost of sales"),
                new PostingRuleLineInput(vatControl, NormalBalance.Debit, "Tax", InheritDimensions: false, "Input VAT"),
                new PostingRuleLineInput(creditors, NormalBalance.Credit, "Gross", InheritDimensions: false, "Trade creditors"),
            ],
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Guid> EnsureAccountAsync(
        IServiceProvider provider,
        VumaRetailDbContext context,
        string code,
        string name,
        AccountType type,
        ControlAccountType controlAccountType,
        CancellationToken cancellationToken)
    {
        if (await context.Accounts
            .FirstOrDefaultAsync(account => account.Code == code, cancellationToken)
            .ConfigureAwait(false) is { } existing)
        {
            return existing.Id;
        }

        return await provider
            .GetRequiredService<IDispatcher>()
            .SendAsync(new CreateAccountCommand(code, name, type, "ZAR", controlAccountType), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Opens the period covering today, so a demo posting has somewhere to land.</summary>
    private static async Task EnsureCurrentPeriodAsync(
        IServiceProvider provider, VumaRetailDbContext context, CancellationToken cancellationToken)
    {
        DateOnly today = DateOnly.FromDateTime(
            provider.GetRequiredService<IClock>().UtcNow.UtcDateTime);
        DateOnly start = new(today.Year, today.Month, 1);
        DateOnly end = start.AddMonths(1).AddDays(-1);

        if (await context.AccountingPeriods
            .FirstOrDefaultAsync(period => period.PeriodStart == start, cancellationToken)
            .ConfigureAwait(false) is not null)
        {
            return;
        }

        await provider
            .GetRequiredService<IDispatcher>()
            .SendAsync(new OpenAccountingPeriodCommand(start, end), cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task EnsureTaxRuleAsync(
        IServiceProvider provider,
        VumaRetailDbContext context,
        string code,
        string name,
        decimal rate,
        TaxTreatment treatment,
        CancellationToken cancellationToken)
    {
        if (await context.TaxRules
            .FirstOrDefaultAsync(rule => rule.Code == code, cancellationToken)
            .ConfigureAwait(false) is not null)
        {
            return;
        }

        // Effective from the start of the current year rather than today, so a demo can post a
        // back-dated document without the rule having started after the document's own date.
        DateOnly effectiveFrom = new(provider.GetRequiredService<IClock>().UtcNow.Year, 1, 1);

        await provider
            .GetRequiredService<IDispatcher>()
            .SendAsync(new CreateTaxRuleCommand(code, name, rate, treatment, effectiveFrom), cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task EnsurePostingRuleAsync(
        IServiceProvider provider,
        VumaRetailDbContext context,
        string eventType,
        string description,
        IReadOnlyList<PostingRuleLineInput> lines,
        CancellationToken cancellationToken)
    {
        if (await context.PostingRules
            .FirstOrDefaultAsync(rule => rule.EventType == eventType, cancellationToken)
            .ConfigureAwait(false) is not null)
        {
            return;
        }

        await provider
            .GetRequiredService<IDispatcher>()
            .SendAsync(new DefinePostingRuleCommand(eventType, lines, description), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Activates the demo installation against whichever control plane is wired.
    /// </summary>
    /// <remarks>
    /// <para>
    /// In Development that is the in-process one, which signs real documents with the development key
    /// — so a demonstration is a fully licensed store with no vendor service anywhere, which is the
    /// difference between a demo that works on a laptop on a plane and one that does not.
    /// </para>
    /// <para>
    /// Against a real control plane there is nothing to register, and the seeder leaves activation to
    /// whoever holds the licence key. It says so rather than failing: a seeded demo against a
    /// production control plane is not a scenario anybody should be in by accident.
    /// </para>
    /// </remarks>
    private static async Task EnsureActivationAsync(
        IServiceProvider provider,
        Guid tenantId,
        Guid storeId,
        CancellationToken cancellationToken)
    {
        IActivationRepository activations = provider.GetRequiredService<IActivationRepository>();

        if (await activations.FindCurrentAsync(cancellationToken).ConfigureAwait(false) is not null)
        {
            return;
        }

        if (provider.GetService<InProcessControlPlane>() is not { } controlPlane)
        {
            Console.WriteLine(
                "Not activated: no in-process control plane is wired. Activate with a real licence "
                + "key through POST /api/v1/licence/activations.");

            return;
        }

        LicenceKey key = controlPlane.Register(new ControlPlaneTenant
        {
            TenantId = tenantId,
            StoreId = storeId,
            PlanCode = "demo",
            Entitlements = [.. provider.GetServices<IModuleManifest>().Select(manifest => manifest.LicenceFlag)],
            Limits = new LicenceLimits(2, 10, 25, 500_000, 50L * 1024 * 1024 * 1024, 1_000_000),
        });

        await provider
            .GetRequiredService<IDispatcher>()
            .SendAsync(
                new ActivateInstallationCommand(key.Value, "Vuma Sandton", "owner@vuma.example"),
                cancellationToken)
            .ConfigureAwait(false);

        Console.WriteLine($"Activated the demo installation. Licence key: {key}");
    }

    private static IReadOnlyCollection<string> AllPermissions(IServiceProvider provider)
        => [.. provider.GetRequiredService<IPermissionCatalogue>().All.Select(descriptor => descriptor.Key.Value)];

    private static async Task<Tenant> EnsureTenantAsync(
        VumaRetailDbContext context,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        // The tenant row is the one read that legitimately happens before a tenant is resolved: the
        // seeder is asking whether the demo tenant exists at all.
        using IDisposable bypass = tenantContext.BypassTenantFilter("seeding the demo tenant");

        Tenant? existing = await context.Tenants
            .FirstOrDefaultAsync(candidate => candidate.Id == DemoTenantId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return existing;
        }

        Tenant tenant = Tenant.CreateWithSouthAfricanDefaults("Vuma Demo Retail (Pty) Ltd", "Vuma Demo");
        SetId(tenant, DemoTenantId);
        tenant.Activate();

        context.Tenants.Add(tenant);
        await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        return tenant;
    }

    private static async Task<Store> EnsureStoreAsync(
        VumaRetailDbContext context,
        IUnitOfWork unitOfWork,
        Guid tenantId,
        string code,
        string name,
        CancellationToken cancellationToken)
    {
        Store? existing = await context.Stores
            .FirstOrDefaultAsync(store => store.Code == code, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return existing;
        }

        Store store = Store.Create(tenantId, code, name);
        context.Stores.Add(store);
        await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

        return store;
    }

    private static async Task<Guid> EnsureRoleAsync(
        IServiceProvider provider,
        string name,
        IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken)
    {
        IRoleRepository roles = provider.GetRequiredService<IRoleRepository>();

        if (await roles.FindByNameAsync(name, cancellationToken).ConfigureAwait(false) is { } existing)
        {
            return existing.Id;
        }

        return await provider
            .GetRequiredService<IDispatcher>()
            .SendAsync(new CreateRoleCommand(name, permissions), cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task EnsureUserAsync(
        IServiceProvider provider,
        string userName,
        string displayName,
        string password,
        Guid roleId,
        Guid? storeId,
        string? pin,
        CancellationToken cancellationToken)
    {
        IUserRepository users = provider.GetRequiredService<IUserRepository>();

        if (await users.FindByUserNameAsync(userName, cancellationToken).ConfigureAwait(false) is not null)
        {
            return;
        }

        IDispatcher dispatcher = provider.GetRequiredService<IDispatcher>();

        Guid userId = await dispatcher
            .SendAsync(new CreateUserCommand(userName, displayName, password), cancellationToken)
            .ConfigureAwait(false);

        await dispatcher
            .SendAsync(new AssignRoleCommand(userId, roleId, storeId), cancellationToken)
            .ConfigureAwait(false);

        if (pin is not null)
        {
            await dispatcher
                .SendAsync(new SetUserPinCommand(userId, pin), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task EnsureTerminalAsync(
        IServiceProvider provider,
        VumaRetailDbContext context,
        Guid storeId,
        string code,
        string name,
        CancellationToken cancellationToken)
    {
        ITerminalRepository terminals = provider.GetRequiredService<ITerminalRepository>();

        if (await terminals.FindByCodeAsync(storeId, code, cancellationToken).ConfigureAwait(false) is not null)
        {
            return;
        }

        TerminalEnrolment enrolment = await provider
            .GetRequiredService<IDispatcher>()
            .SendAsync(new EnrolTerminalCommand(storeId, code, name), cancellationToken)
            .ConfigureAwait(false);

        // Printed rather than stored: an activation code is shown once and never persisted in
        // plaintext, and a demo is no reason to make an exception to that.
        Console.WriteLine($"Terminal {code} enrolled. Activation code: {enrolment.EnrolmentCode}");
        Console.WriteLine($"  expires {enrolment.ExpiresAt:u}");

        _ = context;
    }

    private static async Task<Guid> EnsureUnitOfMeasureAsync(
        IServiceProvider provider,
        VumaRetailDbContext context,
        string code,
        string name,
        UnitOfMeasureType type,
        CancellationToken cancellationToken)
    {
        if (await context.UnitsOfMeasure
            .FirstOrDefaultAsync(unit => unit.Code == code, cancellationToken)
            .ConfigureAwait(false) is { } existing)
        {
            return existing.Id;
        }

        return await provider
            .GetRequiredService<IDispatcher>()
            .SendAsync(new CreateUnitOfMeasureCommand(code, name, type), cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<Guid> EnsureDerivedUnitOfMeasureAsync(
        IServiceProvider provider,
        VumaRetailDbContext context,
        string code,
        string name,
        Guid baseUnitOfMeasureId,
        decimal conversionFactorToBase,
        CancellationToken cancellationToken)
    {
        if (await context.UnitsOfMeasure
            .FirstOrDefaultAsync(unit => unit.Code == code, cancellationToken)
            .ConfigureAwait(false) is { } existing)
        {
            return existing.Id;
        }

        return await provider
            .GetRequiredService<IDispatcher>()
            .SendAsync(
                new CreateUnitOfMeasureCommand(
                    code,
                    name,
                    UnitOfMeasureType.Count,
                    baseUnitOfMeasureId,
                    conversionFactorToBase),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<Guid> EnsureItemAsync(
        IServiceProvider provider,
        VumaRetailDbContext context,
        string code,
        string name,
        ItemType itemType,
        Guid unitOfMeasureId,
        string description,
        string taxClassCode,
        CancellationToken cancellationToken)
    {
        if (await context.Items
            .FirstOrDefaultAsync(item => item.Code == code, cancellationToken)
            .ConfigureAwait(false) is { } existing)
        {
            return existing.Id;
        }

        return await provider
            .GetRequiredService<IDispatcher>()
            .SendAsync(
                new CreateItemCommand(code, name, itemType, unitOfMeasureId, description, taxClassCode),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<Guid> EnsureVariantAsync(
        IServiceProvider provider,
        VumaRetailDbContext context,
        Guid itemId,
        string sku,
        IReadOnlyList<VariantAttribute> attributes,
        CancellationToken cancellationToken)
    {
        if (await context.ItemVariants
            .FirstOrDefaultAsync(variant => variant.Sku == sku, cancellationToken)
            .ConfigureAwait(false) is { } existing)
        {
            return existing.Id;
        }

        return await provider
            .GetRequiredService<IDispatcher>()
            .SendAsync(new CreateItemVariantCommand(itemId, sku, attributes), cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task EnsureBarcodeAsync(
        IServiceProvider provider,
        VumaRetailDbContext context,
        Guid? itemId,
        Guid? itemVariantId,
        string code,
        BarcodeSymbology symbology,
        CancellationToken cancellationToken)
    {
        if (await context.Barcodes
            .FirstOrDefaultAsync(barcode => barcode.Code == code, cancellationToken)
            .ConfigureAwait(false) is not null)
        {
            return;
        }

        await provider
            .GetRequiredService<IDispatcher>()
            .SendAsync(new AddBarcodeCommand(itemId, itemVariantId, code, symbology), cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task EnsurePartnerAsync(
        IServiceProvider provider,
        VumaRetailDbContext context,
        string code,
        string name,
        PartnerType type,
        string email,
        CancellationToken cancellationToken)
    {
        if (await context.Partners
            .FirstOrDefaultAsync(partner => partner.Code == code, cancellationToken)
            .ConfigureAwait(false) is not null)
        {
            return;
        }

        await provider
            .GetRequiredService<IDispatcher>()
            .SendAsync(new CreatePartnerCommand(code, name, type, Email: email), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Pins the demo tenant's id so a re-run finds the same tenant.
    /// </summary>
    /// <remarks>
    /// <see cref="Tenant"/> generates a UUID v7 on creation, which is right for real tenants and
    /// wrong for a fixture that has to be findable. Reflection rather than a public setter, because a
    /// public "change this entity's identity" method would be usable from a module.
    /// </remarks>
    private static void SetId(Tenant tenant, Guid id)
    {
        typeof(Domain.Entities.Entity)
            .GetProperty(nameof(Domain.Entities.Entity.Id))!
            .SetValue(tenant, id);

        typeof(Domain.Entities.Entity)
            .GetProperty(nameof(Domain.Entities.Entity.TenantId))!
            .SetValue(tenant, id);
    }
}
