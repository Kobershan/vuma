using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Application.Identity.Commands;
using VumaRetail.Contracts.Licensing;
using VumaRetail.Domain.Licensing;
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
    public async Task The_read_only_exemptions_are_exactly_the_three_ADR_028_carve_outs()
    {
        // The set is capped at three *kinds*, and membership is a closed list rather than a count —
        // the same shape as the BypassTenantFilter call-site list Stage 04 introduced. A fourth kind
        // needs a superseding ADR; a new member of an existing kind needs somebody to edit this list
        // and say why.
        Dictionary<string, ReadOnlyExemption> exempt = Commands()
            .Select(command => (
                Name: command.Name,
                Attribute: command.GetCustomAttribute<CommandSideEffectAttribute>()!))
            .Where(entry => entry.Attribute.Exemption is not ReadOnlyExemption.None)
            .ToDictionary(entry => entry.Name, entry => entry.Attribute.Exemption, StringComparer.Ordinal);

        exempt.Values.Distinct().Should().HaveCountLessThanOrEqualTo(3);

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
    public async Task An_in_progress_session_finishes_and_a_new_one_may_not_start()
    {
        // The open-session carve-out (LICENSING.md §4). Stage 09's till opens and closes real sessions
        // against this registry; the mechanism and the guard that honours it are built and asserted
        // here, because a carve-out retrofitted into an enforcement path ends up half-applied.
        OpenSessionRegistry sessions = new();
        DateTimeOffset restrictedSince = DateTimeOffset.UtcNow;

        Guid inProgress = Guid.NewGuid();
        sessions.Open(inProgress, restrictedSince.AddMinutes(-2));

        Guid started = Guid.NewGuid();
        sessions.Open(started, restrictedSince.AddMinutes(2));

        ReadOnlyGuardBehaviour guard = new(
            new TestEntitlementService
            {
                Decision = new EnforcementDecision(
                    EnforcementLevel.ReadOnly,
                    EnforcementReason.SubscriptionLapsed,
                    NoticeStage.FinalNotice,
                    restrictedSince,
                    RestrictedSince: restrictedSince),
            },
            sessions,
            new LicensingOptions());

        (await Runs(guard, new FinishSaleCommand(inProgress))).Should().BeTrue();
        (await Runs(guard, new FinishSaleCommand(started))).Should().BeFalse();
    }

    [Fact]
    public async Task The_carve_out_can_be_switched_off_by_the_vendor()
    {
        OpenSessionRegistry sessions = new();
        DateTimeOffset restrictedSince = DateTimeOffset.UtcNow;

        Guid inProgress = Guid.NewGuid();
        sessions.Open(inProgress, restrictedSince.AddMinutes(-2));

        ReadOnlyGuardBehaviour guard = new(
            new TestEntitlementService
            {
                Decision = new EnforcementDecision(
                    EnforcementLevel.ReadOnly,
                    EnforcementReason.SubscriptionLapsed,
                    NoticeStage.FinalNotice,
                    restrictedSince,
                    RestrictedSince: restrictedSince),
            },
            sessions,
            new LicensingOptions { OpenSessionCarveOut = false });

        (await Runs(guard, new FinishSaleCommand(inProgress))).Should().BeFalse();
    }

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

    private static async Task<bool> Runs(ReadOnlyGuardBehaviour guard, ICommand<Unit> command)
    {
        try
        {
            return await guard.HandleAsync(
                MessageEnvelope.ForCommand(command),
                _ => Task.FromResult(true),
                CancellationToken.None);
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

/// <summary>
/// A till finishing a sale it had already started, for the open-session carve-out.
/// </summary>
/// <remarks>
/// Declared in the test assembly rather than in a module, because the POS does not exist until Stage
/// 09 and the carve-out has to be proven now. It is the shape a real sale command will have: an
/// ordinary write that also says which session it belongs to.
/// </remarks>
/// <param name="SessionId">The till session.</param>
[CommandSideEffect(SideEffect.Write)]
public sealed record FinishSaleCommand(Guid SessionId) : ICommand<Unit>, ISessionScopedCommand;
