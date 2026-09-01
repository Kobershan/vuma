using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Identity.Commands;
using VumaRetail.Application.Pos.Commands;
using VumaRetail.Contracts.Licensing;
using VumaRetail.Domain.Licensing;
using VumaRetail.Domain.Pos;
using VumaRetail.Domain.Primitives;
using VumaRetail.IntegrationTests.Api;
using VumaRetail.IntegrationTests.Harness;
using VumaRetail.Licensing;
using VumaRetail.Licensing.Commands;
using VumaRetail.Licensing.Control;
using VumaRetail.Licensing.Enforcement;
using VumaRetail.Licensing.Queries;
using VumaRetail.TestSupport;

namespace VumaRetail.IntegrationTests.Licensing;

/// <summary>
/// Read-only, in full (<c>docs/TESTING.md</c> §7, ADR-028).
/// </summary>
/// <remarks>
/// The other mandatory suite. Its job is to prove that read-only is <b>complete on the read side</b>
/// and <b>total on the write side</b> — every report works, every write is refused with the right
/// code, and the three carve-outs are exactly three.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class ReadOnlyCorrectnessTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Every_write_command_in_the_system_is_refused_and_every_query_passes()
    {
        // Generated from the assemblies rather than written by hand, which is what docs/TESTING.md §7
        // means by "one test per module, generated from the module manifests so a new module cannot be
        // forgotten". A module stage that adds a write command inherits this assertion without
        // touching the file.
        ReadOnlyGuardBehaviour guard = new(
            new TestEntitlementService
            {
                Decision = new EnforcementDecision(
                    EnforcementLevel.ReadOnly,
                    EnforcementReason.SubscriptionLapsed,
                    NoticeStage.FinalNotice,
                    DateTimeOffset.UtcNow),
            },
            new OpenSessionRegistry(),
            new LicensingOptions());

        List<string> permittedWrites = [];
        List<string> refusedReads = [];

        foreach (Type command in Commands())
        {
            CommandSideEffectAttribute classification =
                command.GetCustomAttribute<CommandSideEffectAttribute>()!;

            MessageEnvelope envelope = MessageEnvelope.ForCommand(
                RuntimeHelpers.GetUninitializedObject(command));

            bool reached = false;

            try
            {
                await guard.HandleAsync<object?>(
                    envelope,
                    _ =>
                    {
                        reached = true;
                        return Task.FromResult<object?>(null);
                    },
                    CancellationToken.None);
            }
            catch (LicenceReadOnlyException)
            {
                reached = false;
            }

            bool shouldBeRefused = classification is
            {
                Effect: SideEffect.Write,
                Exemption: ReadOnlyExemption.None,
            };

            if (shouldBeRefused && reached)
            {
                permittedWrites.Add(command.FullName ?? command.Name);
            }

            if (!shouldBeRefused && !reached)
            {
                refusedReads.Add(command.FullName ?? command.Name);
            }
        }

        permittedWrites.Should().BeEmpty("every unexempted write must be refused while read-only");
        refusedReads.Should().BeEmpty("a read or an ADR-028 carve-out must never be refused");

        foreach (Type query in Queries())
        {
            bool reached = false;

            await guard.HandleAsync<object?>(
                MessageEnvelope.ForQuery(RuntimeHelpers.GetUninitializedObject(query)),
                _ =>
                {
                    reached = true;
                    return Task.FromResult<object?>(null);
                },
                CancellationToken.None);

            reached.Should().BeTrue(
                "{0} is a read, and ADR-028 promises reporting keeps working",
                query.Name);
        }
    }

    [Fact]
    public async Task The_read_only_exemptions_are_exactly_the_four_ADR_135_carve_outs()
    {
        // The set is capped at four *kinds*, and membership is a closed list rather than a count —
        // the same shape as the BypassTenantFilter call-site list Stage 04 introduced. A fifth kind
        // needs a superseding ADR; a new member of an existing kind needs somebody to edit this list
        // and say why.
        Dictionary<string, ReadOnlyExemption> exempt = Commands()
            .Select(command => (
                Name: command.Name,
                Attribute: command.GetCustomAttribute<CommandSideEffectAttribute>()!))
            .Where(entry => entry.Attribute.Exemption is not ReadOnlyExemption.None)
            .ToDictionary(entry => entry.Name, entry => entry.Attribute.Exemption, StringComparer.Ordinal);

        exempt.Values.Distinct().Should().HaveCountLessThanOrEqualTo(4);

        exempt.Should().BeEquivalentTo(new Dictionary<string, ReadOnlyExemption>(StringComparer.Ordinal)
        {
            // Backup: a tenant's data protection must never depend on their subscription status (R4).
            ["CreateBackupSnapshotCommand"] = ReadOnlyExemption.Backup,
            ["VerifyBackupSnapshotCommand"] = ReadOnlyExemption.Backup,

            // OfflineFlush: an inbound batch is data that has already happened. Refusing it would
            // destroy a tenant's books to make a billing point.
            ["ReceiveSyncBatchCommand"] = ReadOnlyExemption.OfflineFlush,

            // Payment: the licence screen and everything on it, plus withdrawal of consent, which must
            // not be held hostage by billing (ADR-052).
            ["ActivateInstallationCommand"] = ReadOnlyExemption.Payment,
            ["RebindActivationCommand"] = ReadOnlyExemption.Payment,
            ["RefreshLeaseCommand"] = ReadOnlyExemption.Payment,
            ["SendHeartbeatCommand"] = ReadOnlyExemption.Payment,
            ["RedeemEmergencyCodeCommand"] = ReadOnlyExemption.Payment,
            ["RevokeSupportAccessCommand"] = ReadOnlyExemption.Payment,

            // ReceiptReprint (ADR-135, §4.10): cannot originate trade — one row appended to an
            // append-only log for a sale that already exists. The single member this kind is scoped to.
            ["RecordReceiptPrintCommand"] = ReadOnlyExemption.ReceiptReprint,
        });

        await Task.CompletedTask;
    }

    [Fact]
    public async Task A_refused_write_says_what_is_owed_and_where_to_pay()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);

        await LapseAsync(harness);

        Func<Task> write = () => harness.SendAsync(new CreateRoleCommand("blocked", []));

        LicenceReadOnlyException refusal = (await write.Should().ThrowAsync<LicenceReadOnlyException>()).Which;

        refusal.Code.Should().Be("LICENCE_READ_ONLY");
        refusal.ProblemExtensions.Should().ContainKey("amountDue");
        refusal.ProblemExtensions.Should().ContainKey("payUrl");
        refusal.ProblemExtensions.Should().ContainKey("updatePaymentMethodUrl");
    }

    [Fact]
    public async Task Over_http_a_refused_write_is_a_403_problem_document_with_the_payment_link()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);

        await harness.CreateUserAsync(
            "readonly-admin",
            permissions: [VumaRetail.Licensing.Permissions.LicensingPermissions.SupportDecide]);

        HttpClient client = await harness.SignInAsync("readonly-admin");

        await LapseAsync(harness);

        // Any unexempted write endpoint would do. This one is chosen because the grant does not
        // exist, which makes the point sharply: the guard sits in slot 50, outside validation and
        // outside the handler, so the answer is 403 rather than the 404 the handler would have
        // reached — and approving a *new* support grant is deliberately not one of the ADR-028
        // carve-outs, while revoking one is.
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/licence/support-access/{Guid.NewGuid()}/approve",
            new ApproveSupportAccessRequest(1));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        System.Text.Json.JsonDocument problem =
            (await response.Content.ReadFromJsonAsync<System.Text.Json.JsonDocument>())!;

        problem.RootElement.GetProperty("code").GetString().Should().Be("LICENCE_READ_ONLY");
        problem.RootElement.GetProperty("payUrl").GetString().Should().NotBeNullOrWhiteSpace();
        problem.RootElement.GetProperty("amountDue").GetDecimal().Should().Be(1499m);
    }

    [Fact]
    public async Task The_licence_screen_still_works_and_so_does_every_read()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);

        Guid userId = await harness.CreateUserAsync(
            "reader",
            permissions:
            [
                VumaRetail.Application.Identity.Permissions.PlatformPermissions.StoreView,
                VumaRetail.Licensing.Permissions.LicensingPermissions.LicenceView,
            ]);

        await LapseAsync(harness);

        // Sign-in works, which is a property of the design rather than a carve-out somebody
        // remembered: AuthenticationService never enters the command pipeline (ADR-040).
        HttpClient client = await harness.SignInAsync("reader");

        HttpResponseMessage permissions = await client.GetAsync("/api/v1/me/permissions");
        permissions.StatusCode.Should().Be(HttpStatusCode.OK);

        LicenceStatusResponse status = await harness.QueryAsync(new GetLicenceStatusQuery());

        status.EnforcementLevel.Should().Be(nameof(EnforcementLevel.ReadOnly));
        status.AmountDue.Should().Be(1499m);
        status.PayUrl.Should().NotBeNullOrWhiteSpace();
        status.UpdatePaymentMethodUrl.Should().NotBeNullOrWhiteSpace();

        userId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Paying_from_inside_read_only_restores_full_access()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);

        await LapseAsync(harness);

        Func<Task> blocked = () => harness.SendAsync(new CreateRoleCommand("before", []));
        await blocked.Should().ThrowAsync<LicenceReadOnlyException>();

        // The customer uses the link the refusal handed them. "Retry now" is on the licence screen and
        // is itself a write — which is why it carries the payment carve-out.
        ControlPlaneTenant vendor = harness.ControlPlane[harness.LicenceKey];
        vendor.SubscriptionCurrent = true;
        vendor.DunningCompletedAt = null;
        vendor.AmountDue = null;

        await harness.SendAsync(new RefreshLeaseCommand());

        Guid roleId = await harness.SendAsync(new CreateRoleCommand("after", []));
        roleId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Six_in_flight_pos_commands_finish_and_no_new_sale_or_session_may_start()
    {
        // The open-session carve-out (LICENSING.md §4, ADR-135). Stage 09's till opens and closes real
        // sessions and sales against this registry; the mechanism and the guard that honours it are
        // asserted here against the real POS command types rather than a test double, per §4.10's
        // adjudication — the shape a real command has is exactly what decides whether it is exempt.
        OpenSessionRegistry sessions = new();
        DateTimeOffset restrictedSince = DateTimeOffset.UtcNow;
        TimeSpan window = TimeSpan.FromMinutes(30);

        Guid inProgressSale = Guid.NewGuid();
        sessions.Open(inProgressSale, restrictedSince.AddMinutes(-2), window);

        Guid startedAfterSale = Guid.NewGuid();
        sessions.Open(startedAfterSale, restrictedSince.AddMinutes(2), window);

        ReadOnlyGuardBehaviour guard = Guard(sessions, restrictedSince, new LicensingOptions());

        // The member set, in full, on the sale opened before the restriction began.
        (await Runs(guard, new AddSaleLineCommand(
            inProgressSale, Guid.NewGuid(), null, new Quantity(1m, "EA"), new Money(10m, "ZAR")))).Should().BeTrue();
        (await Runs(guard, new VoidSaleLineCommand(inProgressSale, Guid.NewGuid()))).Should().BeTrue();
        (await Runs(guard, new TenderSaleCommand(inProgressSale, TenderType.Cash, new Money(10m, "ZAR")))).Should().BeTrue();
        (await Runs(guard, new CompleteSaleCommand(inProgressSale))).Should().BeTrue();
        (await Runs(guard, new VoidSaleCommand(inProgressSale, "wrong item"))).Should().BeTrue();
        (await Runs(guard, new CloseTillSessionCommand(inProgressSale, new Money(500m, "ZAR")))).Should().BeTrue();

        // A session opened *after* the restriction began is a new sale, and no new sale may start —
        // without this the carve-out would be a permanent bypass for anything calling itself a session.
        (await Runs(guard, new AddSaleLineCommand(
            startedAfterSale, Guid.NewGuid(), null, new Quantity(1m, "EA"), new Money(10m, "ZAR")))).Should().BeFalse();
        (await Runs(guard, new CompleteSaleCommand(startedAfterSale))).Should().BeFalse();

        // These are never in the member set at all, whichever sale or session they name — "no new sale
        // can start" would mean nothing if opening one, or waking a parked one, were exempt.
        (await Runs(guard, new OpenSaleCommand(null, Guid.NewGuid()))).Should().BeFalse();
        (await Runs(guard, new OpenTillSessionCommand(Money.Zero("ZAR")))).Should().BeFalse();
        (await Runs(guard, new ParkSaleCommand(inProgressSale))).Should().BeFalse();
        (await Runs(guard, new ResumeSaleCommand(inProgressSale))).Should().BeFalse();
    }

    [Fact]
    public async Task The_in_flight_carve_out_expires_at_its_own_deadline()
    {
        // ADR-135's second bound, and the single most important assertion in this file: opened before
        // the restriction is not enough on its own, or a tenant who simply never finishes a sale keeps
        // every one of these six commands carved out forever. At exactly one second past the window,
        // every one of them is refused.
        OpenSessionRegistry sessions = new();
        DateTimeOffset restrictedSince = DateTimeOffset.UtcNow;
        TimeSpan window = TimeSpan.FromMinutes(30);

        // Opened a minute before the restriction, so it passes bound 1 — the deadline (bound 2) is what
        // this test is about, and it is measured from when the sale opened, not from restrictedSince.
        DateTimeOffset openedAt = restrictedSince.AddMinutes(-1);
        Guid saleId = Guid.NewGuid();
        sessions.Open(saleId, openedAt, window);

        ReadOnlyGuardBehaviour stillWithinWindow = Guard(
            sessions, restrictedSince, new LicensingOptions(), evaluatedAt: openedAt.Add(window).AddSeconds(-1));

        (await Runs(stillWithinWindow, new CompleteSaleCommand(saleId))).Should().BeTrue();

        ReadOnlyGuardBehaviour pastTheDeadline = Guard(
            sessions, restrictedSince, new LicensingOptions(), evaluatedAt: openedAt.Add(window).AddSeconds(1));

        (await Runs(pastTheDeadline, new AddSaleLineCommand(
            saleId, Guid.NewGuid(), null, new Quantity(1m, "EA"), new Money(10m, "ZAR")))).Should().BeFalse();
        (await Runs(pastTheDeadline, new VoidSaleLineCommand(saleId, Guid.NewGuid()))).Should().BeFalse();
        (await Runs(pastTheDeadline, new TenderSaleCommand(saleId, TenderType.Cash, new Money(10m, "ZAR")))).Should().BeFalse();
        (await Runs(pastTheDeadline, new CompleteSaleCommand(saleId))).Should().BeFalse();
        (await Runs(pastTheDeadline, new VoidSaleCommand(saleId, "wrong item"))).Should().BeFalse();
        (await Runs(pastTheDeadline, new CloseTillSessionCommand(saleId, new Money(500m, "ZAR")))).Should().BeFalse();
    }

    [Fact]
    public async Task Winding_the_clock_back_does_not_revive_an_expired_carve_out()
    {
        // LICENSING.md §7: a clock wound back must buy nothing, including here. MayCarryOn is evaluated
        // against decision.EvaluatedAt — the same watermarked instant the enforcement decision itself
        // was computed from — so this test asserts the plumbing carries that instant through rather
        // than re-deriving "now" from IClock.UtcNow at the guard.
        OpenSessionRegistry sessions = new();
        DateTimeOffset restrictedSince = DateTimeOffset.UtcNow;
        TimeSpan window = TimeSpan.FromMinutes(30);

        Guid saleId = Guid.NewGuid();
        sessions.Open(saleId, restrictedSince.AddMinutes(-1), window);

        // The decision's own EvaluatedAt is already past the deadline; winding a *local* clock back
        // cannot matter because the guard never consults one directly for this check.
        ReadOnlyGuardBehaviour guard = Guard(
            sessions,
            restrictedSince,
            new LicensingOptions(),
            evaluatedAt: restrictedSince.Add(window).AddDays(30));

        (await Runs(guard, new CompleteSaleCommand(saleId))).Should().BeFalse();
    }

    [Fact]
    public async Task The_carve_out_can_be_switched_off_by_the_vendor()
    {
        OpenSessionRegistry sessions = new();
        DateTimeOffset restrictedSince = DateTimeOffset.UtcNow;

        Guid inProgressSale = Guid.NewGuid();
        sessions.Open(inProgressSale, restrictedSince.AddMinutes(-2), TimeSpan.FromMinutes(30));

        ReadOnlyGuardBehaviour guard = Guard(
            sessions, restrictedSince, new LicensingOptions { OpenSessionCarveOut = false });

        (await Runs(guard, new CompleteSaleCommand(inProgressSale))).Should().BeFalse();
    }

    private static ReadOnlyGuardBehaviour Guard(
        OpenSessionRegistry sessions, DateTimeOffset restrictedSince, LicensingOptions options, DateTimeOffset? evaluatedAt = null)
        => new(
            new TestEntitlementService
            {
                Decision = new EnforcementDecision(
                    EnforcementLevel.ReadOnly,
                    EnforcementReason.SubscriptionLapsed,
                    NoticeStage.FinalNotice,
                    evaluatedAt ?? restrictedSince,
                    RestrictedSince: restrictedSince),
            },
            sessions,
            options);

    [Fact]
    public async Task An_unlicensed_module_is_refused_with_an_upgrade_reference_and_not_a_dead_end()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture, activate: false);

        // A plan with nothing on it. The core modules stay on regardless — a tenant who could not sign
        // in to fix their licence would be a tenant nobody could help.
        await harness.ActivateAsync(entitlements: []);

        bool identity = await harness.InScopeAsync(provider =>
            provider.GetRequiredService<IEntitlementService>().IsModuleEnabledAsync("identity"));

        // Deliberately a name no module declares. Until Stage 08 this read "inventory", which worked
        // only because inventory did not exist yet — when it arrived as a core module the assertion
        // inverted, which is the assertion doing its job rather than a regression.
        //
        // Note what this actually exercises: EntitlementService refuses an *undeclared* module before
        // it ever consults the plan, so this is the unknown-module path, not the entitled-or-not one.
        // Every module in the solution is core today, so the "declared, not core, not on the plan"
        // branch has no coverage — see docs/PROGRESS.md. The first licensable module should come with
        // a test that names it here.
        const string unlicensed = "no-such-module";

        bool unknown = await harness.InScopeAsync(provider =>
            provider.GetRequiredService<IEntitlementService>().IsModuleEnabledAsync(unlicensed));

        identity.Should().BeTrue();
        unknown.Should().BeFalse();

        ModuleNotEnabledException refusal = new(unlicensed);
        refusal.Kind.Should().Be(VumaRetail.Domain.Primitives.DomainProblemKind.Forbidden);
        refusal.ProblemExtensions["upgrade"].Should().Be($"module:{unlicensed}");
    }

    [Fact]
    public async Task A_declared_module_is_permitted_when_there_is_no_lease_to_ask_at_all()
    {
        // §4.12's trap, closed. Before this fix, EntitlementService fell back to an *empty*
        // entitlement set whenever there was no lease — indistinguishable from a real lease that
        // genuinely grants nothing — so a fresh DR restore before rebind, a migration, or a corrupted
        // state directory would hard-403 every declared module, RequireModule included, which sits on
        // GETs with no other gate behind it (rule 15's read-only carve-out is a command-pipeline
        // concern and never runs for a query). The fix mirrors CheckLimitAsync's own
        // LicenceLimits.Unlimited fallback: "the lease says no" and "there is no lease to ask" are
        // different facts, and only the first is a real denial.
        //
        // Deliberately never activates at all — not even with an empty entitlement lease, which is
        // the *other* test in this file and a genuinely different fact from this one.
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture, activate: false);

        bool identity = await harness.InScopeAsync(provider =>
            provider.GetRequiredService<IEntitlementService>().IsModuleEnabledAsync("identity"));

        // "pos" is declared and IsCore => false (PosModuleManifest) — the exact shape §4.12 named:
        // the first non-core module, where the empty-set fallback used to bite.
        bool pos = await harness.InScopeAsync(provider =>
            provider.GetRequiredService<IEntitlementService>().IsModuleEnabledAsync("pos"));

        identity.Should().BeTrue("core modules are never gated on a lease at all");
        pos.Should().BeTrue("no lease to consult must mean permit, not deny — the ReadOnlyGuard, not this gate, is what still correctly restricts writes");
    }

    [Fact]
    public async Task RequireModule_is_actually_wired_and_refuses_a_real_request_for_an_unlicensed_module()
    {
        // §4.12's other half: IsModuleEnabledAsync working in isolation (the test above) proves
        // nothing about whether any endpoint actually calls it. Confirmed zero call sites before this
        // session; now wired onto every route group in Pos/Sales/Imports/Procurement/Warehouse. This
        // drives a real HTTP request through the real pipeline against a plan that genuinely excludes
        // warehouse, with a user permissioned for the endpoint itself, so a 403 here can only be
        // RequireModule and nothing else.
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture, activate: false);

        await harness.ActivateAsync(entitlements: ["identity", "catalog", "inventory", "finance"]);

        await harness.CreateUserAsync(
            "picker",
            permissions: [VumaRetail.Application.Warehouse.Permissions.WarehousePermissions.View]);

        HttpClient client = await harness.SignInAsync("picker");

        // A real (if nonexistent) locationId, so parameter binding succeeds and the request reaches the
        // filter pipeline — RequireModule runs before the handler, but binding runs before any filter.
        HttpResponseMessage response = await client.GetAsync($"/api/v1/warehouse/zones?locationId={Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        System.Text.Json.JsonDocument problem =
            (await response.Content.ReadFromJsonAsync<System.Text.Json.JsonDocument>())!;

        problem.RootElement.GetProperty("upgrade").GetString().Should().Be("module:warehouse");
    }

    [Fact]
    public async Task A_hard_limit_refuses_the_eleventh_terminal_and_lets_the_tenth_through()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture, activate: false);

        await harness.ActivateAsync(limits: new LicenceLimits(2, 10, 25, 1000, 1000, 1000));

        for (int index = 1; index <= 10; index++)
        {
            await harness.SendAsync(new EnrolTerminalCommand(harness.StoreId, $"T{index:00}", $"Till {index}"));
        }

        Func<Task> eleventh = () => harness.SendAsync(
            new EnrolTerminalCommand(harness.StoreId, "T11", "Till 11"));

        LicenceLimitExceededException refusal =
            (await eleventh.Should().ThrowAsync<LicenceLimitExceededException>()).Which;

        refusal.Message.Should().Contain("10").And.Contain("terminals");
        refusal.ProblemExtensions["upgrade"].Should().Be("limit:Terminals");
    }

    [Fact]
    public async Task A_soft_limit_warns_and_never_blocks()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture, activate: false);

        await harness.ActivateAsync(limits: new LicenceLimits(2, 10, 25, 100, 1000, 1000));

        LimitCheck check = await harness.InScopeAsync(provider =>
            provider.GetRequiredService<IEntitlementService>()
                .CheckLimitAsync(LimitKind.TransactionsPerMonth, 250));

        check.Exceeded.Should().BeTrue();
        check.IsHard.Should().BeFalse();
        check.MustRefuse.Should().BeFalse();
        check.ShouldWarn.Should().BeTrue();
    }

    private static async Task<bool> Runs<TResult>(ReadOnlyGuardBehaviour guard, ICommand<TResult> command)
    {
        try
        {
            await guard.HandleAsync(
                MessageEnvelope.ForCommand(command),
                _ => Task.FromResult<TResult>(default!),
                CancellationToken.None);

            return true;
        }
        catch (LicenceReadOnlyException)
        {
            return false;
        }
    }

    private static async Task LapseAsync(ApiHarness harness)
    {
        ControlPlaneTenant vendor = harness.ControlPlane[harness.LicenceKey];

        vendor.SubscriptionCurrent = false;
        vendor.AmountDue = new VumaRetail.Domain.Primitives.Money(1499m, "ZAR");
        vendor.DunningCompletedAt = harness.Clock.UtcNow.AddDays(-1);

        await harness.SendAsync(new RefreshLeaseCommand());
    }

    private static IEnumerable<Type> Commands() => Assemblies()
        .SelectMany(assembly => assembly.GetTypes())
        .Where(type => type is { IsAbstract: false, IsInterface: false })
        .Where(type => type.GetInterfaces().Any(contract => contract.IsGenericType
            && contract.GetGenericTypeDefinition() == typeof(ICommand<>)))
        .Where(type => type.GetCustomAttribute<CommandSideEffectAttribute>() is not null);

    private static IEnumerable<Type> Queries() => Assemblies()
        .SelectMany(assembly => assembly.GetTypes())
        .Where(type => type is { IsAbstract: false, IsInterface: false })
        .Where(type => type.GetInterfaces().Any(contract => contract.IsGenericType
            && contract.GetGenericTypeDefinition() == typeof(IQuery<>)));

    /// <summary>
    /// Every product assembly whose commands and queries this sweep covers.
    /// </summary>
    /// <remarks>
    /// Derived rather than listed, since Stage 11 (ADR-078). The hand-maintained version of this array
    /// omitted <c>VumaRetail.Finance</c> for a whole stage, which meant the trial balance, both
    /// ageings and every ledger query were never asserted to survive read-only —
    /// <c>TESTING.md</c> §7 requires the full catalogue, not a sample (<c>PROGRESS.md</c> §4.16).
    /// <see cref="ModuleAssemblies"/> is shared source with the architecture tests so the two sweeps
    /// cannot drift apart again.
    /// </remarks>
    private static Assembly[] Assemblies() => ModuleAssemblies.All;
}
