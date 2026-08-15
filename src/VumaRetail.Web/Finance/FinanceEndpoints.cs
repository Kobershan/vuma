using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using VumaRetail.Application.Abstractions;
using VumaRetail.Contracts.Finance;
using VumaRetail.Domain.Finance;
using VumaRetail.Finance.Commands;
using VumaRetail.Finance.Permissions;
using VumaRetail.Finance.Periods;
using VumaRetail.Finance.Queries;
using VumaRetail.Finance.Tax;
using VumaRetail.Web.Api;

namespace VumaRetail.Web.Finance;

/// <summary>
/// The <c>finance</c> module's endpoints: chart of accounts, periods, the general ledger, AR, AP,
/// banking and tax.
/// </summary>
/// <remarks>
/// R3: nothing exists in a UI before it exists here. Every command and query
/// <c>docs/stages/STAGE-07-finance.md</c> lists has an endpoint — this is the surface Stage 09's POS
/// and Stage 12's procurement call through, once each raises its own <c>IFinancialEvent</c>.
/// </remarks>
public static class FinanceEndpoints
{
    /// <summary>Maps the finance endpoints under the current API version.</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapVumaFinance(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder api = endpoints.MapVumaApi();

        MapAccounts(api);
        MapPeriods(api);
        MapLedger(api);
        MapPostingRules(api);
        MapAr(api);
        MapAp(api);
        MapBanking(api);
        MapTax(api);

        return endpoints;
    }

    private static void MapAccounts(RouteGroupBuilder api)
    {
        RouteGroupBuilder accounts = api.MapGroup("/finance/accounts").WithTags("Finance");

        accounts.MapGet("/", ListAccountsAsync)
            .RequirePermission(FinancePermissions.LedgerView)
            .Produces<IReadOnlyList<AccountResponse>>()
            .WithSummary("The chart of accounts.")
            .WithDescription("Not paginated — a tenant's chart of accounts is configuration, not a catalogue.");

        accounts.MapPost("/", CreateAccountAsync)
            .RequirePermission(FinancePermissions.LedgerConfigure)
            .Produces<AccountIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Opens a new chart-of-accounts entry.")
            .WithDescription(
                "Refused with 409 if the code is already in use. CLAUDE.md §7 rule 12: only Finance's "
                + "own commands ever name an account — no other module may construct one of these ids.");

        accounts.MapGet("/trial-balance", GetTrialBalanceAsync)
            .RequirePermission(FinancePermissions.LedgerView)
            .Produces<IReadOnlyList<TrialBalanceLineResponse>>()
            .WithSummary("The trial balance as of a date: every account split into its debit or credit column.");
    }

    private static void MapPeriods(RouteGroupBuilder api)
    {
        RouteGroupBuilder periods = api.MapGroup("/finance/periods").WithTags("Finance");

        periods.MapPost("/", OpenAccountingPeriodAsync)
            .RequirePermission(FinancePermissions.PeriodManage)
            .Produces<AccountingPeriodIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Opens a new accounting period.");

        periods.MapPost("/{periodId:guid}/close", ClosePeriodAsync)
            .RequirePermission(FinancePermissions.PeriodManage)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Closes a period.")
            .WithDescription(
                "Refused with 422 (PERIOD_CLOSE_BLOCKED) if any control account disagrees with its "
                + "sub-ledger by a nonzero amount (ADR-016).");

        periods.MapPost("/reconciliation-check", RunDailyReconciliationCheckAsync)
            .RequirePermission(FinancePermissions.PeriodManage)
            .Produces(StatusCodes.Status204NoContent)
            .WithSummary("Runs the control-account variance check on demand.")
            .WithDescription(
                "The same check ADR-016's automated daily job runs — safe to call any time, and how an "
                + "accountant sees today's variance before attempting a close.");
    }

    private static void MapLedger(RouteGroupBuilder api)
    {
        RouteGroupBuilder journals = api.MapGroup("/finance/journals").WithTags("Finance");

        journals.MapGet("/", ListJournalsAsync)
            .RequirePermission(FinancePermissions.LedgerView)
            .Produces<IReadOnlyList<JournalSummaryResponse>>()
            .WithSummary("The most recently posted journals, newest first.");

        journals.MapGet("/{journalId:guid}", GetJournalAsync)
            .RequirePermission(FinancePermissions.LedgerView)
            .Produces<JournalDetailResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Reads one journal in full, with its lines.");

        journals.MapPost("/manual", PostManualJournalAsync)
            .RequirePermission(FinancePermissions.LedgerPost)
            .Produces<JournalIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Posts a journal an accountant entered directly.")
            .WithDescription(
                "Refused with 422 (JOURNAL_NOT_BALANCED) unless debits equal credits, and again "
                + "(NO_OPEN_PERIOD) if no open period covers the posting date.");

        journals.MapPost("/{journalId:guid}/reverse", ReverseJournalAsync)
            .RequirePermission(FinancePermissions.LedgerReverse)
            .Produces<JournalIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Reverses a posted journal with an equal-and-opposite one (CLAUDE.md §7 rule 7).")
            .WithDescription("The original journal is never edited or deleted — only ever linked from the reversal.");
    }

    private static void MapPostingRules(RouteGroupBuilder api)
    {
        RouteGroupBuilder rules = api.MapGroup("/finance/posting-rules").WithTags("Finance");

        rules.MapPost("/", DefinePostingRuleAsync)
            .RequirePermission(FinancePermissions.LedgerConfigure)
            .Produces<PostingRuleIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Maps a financial event type to a balanced set of GL postings (CLAUDE.md §7 rule 12).")
            .WithDescription(
                "Data, not code: a new event type from a later stage's producer module is a new rule "
                + "here, never a change to this module.");
    }

    private static void MapAr(RouteGroupBuilder api)
    {
        RouteGroupBuilder invoices = api.MapGroup("/finance/ar/invoices").WithTags("Finance");

        invoices.MapGet("/open", ListOpenArInvoicesAsync)
            .RequirePermission(FinancePermissions.ArView)
            .Produces<IReadOnlyList<ArInvoiceResponse>>()
            .WithSummary("Every customer invoice with a balance still outstanding.");

        invoices.MapGet("/ageing", GetArAgeingAsync)
            .RequirePermission(FinancePermissions.ArView)
            .Produces<IReadOnlyList<AgeingRowResponse>>()
            .WithSummary("The AR ageing report as of a date: current/30/60/90/90+ buckets per customer.");

        invoices.MapPost("/", CreateArInvoiceAsync)
            .RequirePermission(FinancePermissions.ArInvoice)
            .Produces<ArInvoiceIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Opens a draft customer invoice with its lines, tax calculated per line.");

        invoices.MapPost("/{arInvoiceId:guid}/post", PostArInvoiceAsync)
            .RequirePermission(FinancePermissions.ArInvoice)
            .Produces<JournalIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Posts a draft invoice to the GL through the posting rules engine.")
            .WithDescription("Freezes the invoice's lines; it becomes IImmutableRecord for its lines from here.");

        RouteGroupBuilder receipts = api.MapGroup("/finance/ar/receipts").WithTags("Finance");

        receipts.MapPost("/", RecordArReceiptAsync)
            .RequirePermission(FinancePermissions.ArReceipt)
            .Produces<ArReceiptIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Records a customer receipt and allocates it across one or more invoices.")
            .WithDescription("Immutable once recorded — correct a misallocation by reversing the posted journal.");
    }

    private static void MapAp(RouteGroupBuilder api)
    {
        RouteGroupBuilder invoices = api.MapGroup("/finance/ap/invoices").WithTags("Finance");

        invoices.MapGet("/open", ListOpenApInvoicesAsync)
            .RequirePermission(FinancePermissions.ApView)
            .Produces<IReadOnlyList<ApInvoiceResponse>>()
            .WithSummary("Every supplier invoice with a balance still outstanding.");

        invoices.MapGet("/ageing", GetApAgeingAsync)
            .RequirePermission(FinancePermissions.ApView)
            .Produces<IReadOnlyList<AgeingRowResponse>>()
            .WithSummary("The AP ageing report as of a date: current/30/60/90/90+ buckets per supplier.");

        invoices.MapPost("/", CreateApInvoiceAsync)
            .RequirePermission(FinancePermissions.ApInvoice)
            .Produces<ApInvoiceIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Captures a draft supplier invoice with its lines, tax calculated per line.");

        invoices.MapPost("/{apInvoiceId:guid}/post", PostApInvoiceAsync)
            .RequirePermission(FinancePermissions.ApInvoice)
            .Produces<JournalIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Posts a draft invoice to the GL through the posting rules engine.");

        RouteGroupBuilder payments = api.MapGroup("/finance/ap/payments").WithTags("Finance");

        payments.MapPost("/", RecordApPaymentAsync)
            .RequirePermission(FinancePermissions.ApPayment)
            .Produces<ApPaymentIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Records a supplier payment and allocates it across one or more invoices.");
    }

    private static void MapBanking(RouteGroupBuilder api)
    {
        RouteGroupBuilder accounts = api.MapGroup("/finance/banking/accounts").WithTags("Finance");

        accounts.MapPost("/", CreateBankAccountAsync)
            .RequirePermission(FinancePermissions.BankingReconcile)
            .Produces<BankAccountIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Opens a bank account record, paired one-to-one with a GL bank control account.");

        accounts.MapGet("/{bankAccountId:guid}/reconciliation", GetBankReconciliationSummaryAsync)
            .RequirePermission(FinancePermissions.BankingView)
            .Produces<BankReconciliationSummaryResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Compares the account's GL balance against its reconciled statement lines.");

        RouteGroupBuilder lines = api.MapGroup("/finance/banking/statement-lines").WithTags("Finance");

        lines.MapPost("/import", ImportBankStatementLinesAsync)
            .RequirePermission(FinancePermissions.BankingReconcile)
            .Produces<ImportBankStatementLinesResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Imports a batch of bank statement lines.")
            .WithDescription(
                "A batch command, not a file parser — turning a bank export into these lines is Stage "
                + "11's job. A line already imported for the account is skipped, not rejected.");

        lines.MapPost("/{lineId:guid}/match", MatchBankStatementLineAsync)
            .RequirePermission(FinancePermissions.BankingReconcile)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Matches a statement line against a GL journal line — the reconciliation act.");

        lines.MapPost("/{lineId:guid}/unmatch", UnmatchBankStatementLineAsync)
            .RequirePermission(FinancePermissions.BankingReconcile)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Undoes a match, for example after it is found to be wrong.");
    }

    private static void MapTax(RouteGroupBuilder api)
    {
        RouteGroupBuilder tax = api.MapGroup("/finance/tax").WithTags("Finance");

        tax.MapPost("/rules", CreateTaxRuleAsync)
            .RequirePermission(FinancePermissions.TaxConfigure)
            .Produces<TaxRuleIdResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithSummary("Defines a new tax rule (CLAUDE.md §9 — a rules engine, never a constant).")
            .WithDescription("A rate change or a new jurisdiction is a new row here, never a code change.");

        tax.MapGet("/calculate", CalculateTaxAsync)
            .RequirePermission(FinancePermissions.TaxView)
            .Produces<TaxCalculationResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Previews what a tax code and amount would calculate to, without creating any document.");
    }

    // ---- Accounts ----------------------------------------------------------------------------

    private static async Task<IResult> ListAccountsAsync(IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        IReadOnlyList<AccountView> accounts = await dispatcher
            .QueryAsync(new ListAccountsQuery(), cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<AccountResponse>>(
            [.. accounts.Select(a => new AccountResponse(
                a.Id, a.Code, a.Name, a.Type.ToString(), a.ControlAccountType.ToString(), a.Currency, a.IsActive))]);
    }

    private static async Task<IResult> CreateAccountAsync(
        CreateAccountRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        AccountType type = ParseEnum<AccountType>(request.Type, nameof(request.Type), nameof(CreateAccountCommand));
        ControlAccountType controlAccountType = ParseEnum<ControlAccountType>(
            request.ControlAccountType, nameof(request.ControlAccountType), nameof(CreateAccountCommand));

        Guid id = await dispatcher
            .SendAsync(
                new CreateAccountCommand(
                    request.Code, request.Name, type, request.Currency, controlAccountType, request.ParentAccountId),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/finance/accounts/{id}", new AccountIdResponse(id));
    }

    private static async Task<IResult> GetTrialBalanceAsync(
        IDispatcher dispatcher, IClock clock, CancellationToken cancellationToken, DateOnly? asOf = null)
    {
        IReadOnlyList<TrialBalanceLine> lines = await dispatcher
            .QueryAsync(new GetTrialBalanceQuery(asOf ?? Today(clock)), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<TrialBalanceLineResponse>>(
            [.. lines.Select(l => new TrialBalanceLineResponse(l.AccountId, l.Code, l.Name, l.Type.ToString(), l.Debit, l.Credit))]);
    }

    // ---- Periods -------------------------------------------------------------------------------

    private static async Task<IResult> OpenAccountingPeriodAsync(
        OpenAccountingPeriodRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid id = await dispatcher
            .SendAsync(new OpenAccountingPeriodCommand(request.PeriodStart, request.PeriodEnd), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/finance/periods/{id}", new AccountingPeriodIdResponse(id));
    }

    private static async Task<IResult> ClosePeriodAsync(Guid periodId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new ClosePeriodCommand(periodId), cancellationToken).ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> RunDailyReconciliationCheckAsync(IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new RunDailyReconciliationCheckCommand(), cancellationToken).ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    // ---- Ledger --------------------------------------------------------------------------------

    private static async Task<IResult> ListJournalsAsync(
        IDispatcher dispatcher, CancellationToken cancellationToken, int limit = 50)
    {
        IReadOnlyList<JournalSummary> journals = await dispatcher
            .QueryAsync(new ListJournalsQuery(limit), cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<JournalSummaryResponse>>(
            [.. journals.Select(j => new JournalSummaryResponse(
                j.Id, j.JournalNumber, j.PostedAt, j.SourceModule, j.SourceEventType, j.Narration, j.LineCount))]);
    }

    private static async Task<IResult> GetJournalAsync(Guid journalId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        JournalDetail journal = await dispatcher
            .QueryAsync(new GetJournalQuery(journalId), cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(new JournalDetailResponse(
            journal.Id, journal.JournalNumber, journal.PostedAt, journal.PostedBy, journal.SourceModule,
            journal.SourceEventType, journal.SourceReference, journal.Narration, journal.ReversalOfJournalId,
            [.. journal.Lines.Select(l => new JournalLineResponse(l.LineNumber, l.AccountId, l.Debit, l.Credit, l.Description))]));
    }

    private static async Task<IResult> PostManualJournalAsync(
        PostManualJournalRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid id = await dispatcher
            .SendAsync(
                new PostManualJournalCommand(
                    request.PostingDate,
                    request.Currency,
                    [.. request.Lines.Select(l => new ManualJournalLineInput(
                        l.AccountId, l.Debit, l.Credit, l.Description,
                        l.DepartmentId, l.CostCentreId, l.ProjectId, l.ChannelId, l.EmployeeId))],
                    request.Narration,
                    request.Reference),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/finance/journals/{id}", new JournalIdResponse(id));
    }

    private static async Task<IResult> ReverseJournalAsync(
        Guid journalId, ReverseJournalRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid reversalId = await dispatcher
            .SendAsync(new ReverseJournalCommand(journalId, request.Reason), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/finance/journals/{reversalId}", new JournalIdResponse(reversalId));
    }

    // ---- Posting rules ---------------------------------------------------------------------------

    private static async Task<IResult> DefinePostingRuleAsync(
        DefinePostingRuleRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        IReadOnlyList<PostingRuleLineInput> lines =
        [
            .. request.Lines.Select(l => new PostingRuleLineInput(
                l.AccountId,
                ParseEnum<NormalBalance>(l.Side, nameof(l.Side), nameof(DefinePostingRuleCommand)),
                l.AmountKey,
                l.InheritDimensions,
                l.Description)),
        ];

        Guid id = await dispatcher
            .SendAsync(new DefinePostingRuleCommand(request.EventType, lines, request.Description), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/finance/posting-rules/{id}", new PostingRuleIdResponse(id));
    }

    // ---- AR ------------------------------------------------------------------------------------

    private static async Task<IResult> ListOpenArInvoicesAsync(IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        IReadOnlyList<ArInvoiceView> invoices = await dispatcher
            .QueryAsync(new ListOpenArInvoicesQuery(), cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<ArInvoiceResponse>>([.. invoices.Select(ToResponse)]);
    }

    private static async Task<IResult> GetArAgeingAsync(
        IDispatcher dispatcher, IClock clock, CancellationToken cancellationToken, DateOnly? asOf = null)
    {
        IReadOnlyList<AgeingRow> rows = await dispatcher
            .QueryAsync(new GetArAgeingQuery(asOf ?? Today(clock)), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<AgeingRowResponse>>([.. rows.Select(ToResponse)]);
    }

    private static async Task<IResult> CreateArInvoiceAsync(
        CreateArInvoiceRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid id = await dispatcher
            .SendAsync(
                new CreateArInvoiceCommand(
                    request.PartnerId,
                    request.InvoiceDate,
                    request.DueDate,
                    request.Currency,
                    [.. request.Lines.Select(l => new ArInvoiceLineInput(l.Description, l.Amount, l.TaxCode))]),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/finance/ar/invoices/{id}", new ArInvoiceIdResponse(id));
    }

    private static async Task<IResult> PostArInvoiceAsync(
        Guid arInvoiceId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new PostArInvoiceCommand(arInvoiceId), cancellationToken).ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/finance/ar/invoices/{arInvoiceId}", new ArInvoiceIdResponse(arInvoiceId));
    }

    private static async Task<IResult> RecordArReceiptAsync(
        RecordArReceiptRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid id = await dispatcher
            .SendAsync(
                new RecordArReceiptCommand(
                    request.PartnerId,
                    request.Currency,
                    [.. request.Allocations.Select(a => new ArReceiptAllocationInput(a.ArInvoiceId, a.Amount))],
                    request.BankAccountId),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/finance/ar/receipts/{id}", new ArReceiptIdResponse(id));
    }

    // ---- AP ------------------------------------------------------------------------------------

    private static async Task<IResult> ListOpenApInvoicesAsync(IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        IReadOnlyList<ApInvoiceView> invoices = await dispatcher
            .QueryAsync(new ListOpenApInvoicesQuery(), cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<ApInvoiceResponse>>([.. invoices.Select(ToResponse)]);
    }

    private static async Task<IResult> GetApAgeingAsync(
        IDispatcher dispatcher, IClock clock, CancellationToken cancellationToken, DateOnly? asOf = null)
    {
        IReadOnlyList<AgeingRow> rows = await dispatcher
            .QueryAsync(new GetApAgeingQuery(asOf ?? Today(clock)), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<AgeingRowResponse>>([.. rows.Select(ToResponse)]);
    }

    private static async Task<IResult> CreateApInvoiceAsync(
        CreateApInvoiceRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid id = await dispatcher
            .SendAsync(
                new CreateApInvoiceCommand(
                    request.PartnerId,
                    request.SupplierInvoiceNumber,
                    request.InvoiceDate,
                    request.DueDate,
                    request.Currency,
                    [.. request.Lines.Select(l => new ApInvoiceLineInput(l.Description, l.Amount, l.TaxCode))]),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/finance/ap/invoices/{id}", new ApInvoiceIdResponse(id));
    }

    private static async Task<IResult> PostApInvoiceAsync(
        Guid apInvoiceId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new PostApInvoiceCommand(apInvoiceId), cancellationToken).ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/finance/ap/invoices/{apInvoiceId}", new ApInvoiceIdResponse(apInvoiceId));
    }

    private static async Task<IResult> RecordApPaymentAsync(
        RecordApPaymentRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid id = await dispatcher
            .SendAsync(
                new RecordApPaymentCommand(
                    request.PartnerId,
                    request.Currency,
                    [.. request.Allocations.Select(a => new ApPaymentAllocationInput(a.ApInvoiceId, a.Amount))],
                    request.BankAccountId),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/finance/ap/payments/{id}", new ApPaymentIdResponse(id));
    }

    // ---- Banking -------------------------------------------------------------------------------

    private static async Task<IResult> CreateBankAccountAsync(
        CreateBankAccountRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        Guid id = await dispatcher
            .SendAsync(
                new CreateBankAccountCommand(request.GlAccountId, request.Name, request.AccountNumber, request.Currency),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/finance/banking/accounts/{id}", new BankAccountIdResponse(id));
    }

    private static async Task<IResult> GetBankReconciliationSummaryAsync(
        Guid bankAccountId, IDispatcher dispatcher, IClock clock, CancellationToken cancellationToken, DateOnly? asOf = null)
    {
        BankReconciliationSummary summary = await dispatcher
            .QueryAsync(
                new GetBankReconciliationSummaryQuery(bankAccountId, asOf ?? Today(clock)),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new BankReconciliationSummaryResponse(
            summary.BankAccountId, summary.GlBalance, summary.ReconciledBalance, summary.Variance));
    }

    private static async Task<IResult> ImportBankStatementLinesAsync(
        ImportBankStatementLinesRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        int imported = await dispatcher
            .SendAsync(
                new ImportBankStatementLinesCommand(
                    request.BankAccountId,
                    [.. request.Lines.Select(l => new BankStatementLineInput(
                        l.TransactionDate, l.Description, l.Amount, l.ExternalReference))]),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created(
            $"/api/v1/finance/banking/accounts/{request.BankAccountId}/reconciliation",
            new ImportBankStatementLinesResponse(imported));
    }

    private static async Task<IResult> MatchBankStatementLineAsync(
        Guid lineId, MatchBankStatementLineRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new MatchBankStatementLineCommand(lineId, request.JournalLineId), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> UnmatchBankStatementLineAsync(
        Guid lineId, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new UnmatchBankStatementLineCommand(lineId), cancellationToken).ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    // ---- Tax -----------------------------------------------------------------------------------

    private static async Task<IResult> CreateTaxRuleAsync(
        CreateTaxRuleRequest request, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        TaxTreatment treatment = ParseEnum<TaxTreatment>(
            request.Treatment, nameof(request.Treatment), nameof(CreateTaxRuleCommand));

        Guid id = await dispatcher
            .SendAsync(
                new CreateTaxRuleCommand(
                    request.Code, request.Name, request.Rate, treatment, request.EffectiveFrom, request.EffectiveTo),
                cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Created($"/api/v1/finance/tax/rules/{id}", new TaxRuleIdResponse(id));
    }

    private static async Task<IResult> CalculateTaxAsync(
        string taxCode, decimal amount, string currency, DateOnly asOf, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        TaxCalculation calculation = await dispatcher
            .QueryAsync(new CalculateTaxQuery(taxCode, amount, currency, asOf), cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(new TaxCalculationResponse(
            calculation.TaxCode, calculation.NetAmount.Amount, calculation.TaxAmount.Amount,
            calculation.GrossAmount.Amount, calculation.Rate));
    }

    // ---- Mapping helpers -------------------------------------------------------------------------

    private static ArInvoiceResponse ToResponse(ArInvoiceView invoice) => new(
        invoice.Id, invoice.PartnerId, invoice.InvoiceNumber, invoice.InvoiceDate, invoice.DueDate,
        invoice.Currency, invoice.Total, invoice.OutstandingBalance, invoice.Status.ToString());

    private static ApInvoiceResponse ToResponse(ApInvoiceView invoice) => new(
        invoice.Id, invoice.PartnerId, invoice.SupplierInvoiceNumber, invoice.InvoiceDate, invoice.DueDate,
        invoice.Currency, invoice.Total, invoice.OutstandingBalance, invoice.Status.ToString());

    private static AgeingRowResponse ToResponse(AgeingRow row) => new(
        row.PartnerId, row.Current, row.Days30, row.Days60, row.Days90, row.Days90Plus, row.Total);

    /// <summary>Parses an enum from a request field, refusing with a 400 rather than a 500 on a typo.</summary>
    /// <summary>
    /// Today's date for an "as of" query the caller left unspecified.
    /// </summary>
    /// <remarks>
    /// Through <see cref="IClock"/> rather than <c>DateTime.UtcNow</c> (CONVENTIONS.md §6): a trial
    /// balance, an ageing report and a bank reconciliation are all as-of-a-date answers, so a test
    /// that cannot move the clock cannot assert what any of them return on a date boundary.
    /// </remarks>
    private static DateOnly Today(IClock clock) => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

    private static TEnum ParseEnum<TEnum>(string value, string propertyName, string messageName)
        where TEnum : struct, Enum
    {
        if (Enum.TryParse(value, ignoreCase: true, out TEnum parsed) && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        throw new ValidationFailedException(
            messageName,
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [propertyName] = [$"'{value}' is not one of: {string.Join(", ", Enum.GetNames<TEnum>())}."],
            });
    }
}
