using System.Reflection;
using NetArchTest.Rules;
using VumaRetail.Application.Abstractions.Finance;

namespace VumaRetail.ArchitectureTests;

/// <summary>
/// CLAUDE.md §7 rule 12 — no module names a GL account.
/// </summary>
/// <remarks>
/// <para>
/// Modules raise financial events; Stage 07's posting rules engine decides the accounts. The rule
/// exists because the alternative — every module knowing which account its transactions hit — is how
/// a chart of accounts becomes unchangeable: a tenant who wants to split revenue by channel then
/// needs a code change in POS, in ecommerce and in procurement rather than a new rule row.
/// </para>
/// <para>
/// <b>Proved by a deliberate violation before being committed</b>, per every prior stage's
/// architecture rule. A field <c>private readonly Account? _account;</c> was added to
/// <c>VumaRetail.Sync.Outbox.OutboxCapture</c> and the first test below failed with
/// <c>VumaRetail.Sync.Outbox.OutboxCapture</c> named in the message; a
/// <c>Guid AccountId</c> property added to <c>SyncBatch</c> did <em>not</em> fail it, which is the
/// honest limit of what a type-dependency check can see and is why the second and third tests exist.
/// Both experiments were reverted before commit.
/// </para>
/// <para>
/// The exemptions are the Finance module's own surface, not a loophole: <c>VumaRetail.Domain</c>
/// declares <c>Account</c>, <c>VumaRetail.Finance</c> is the engine, <c>VumaRetail.Infrastructure</c>
/// maps and stores it, and <c>VumaRetail.Web</c> exposes the chart-of-accounts endpoints a person
/// administers it through. <c>VumaRetail.Application.Abstractions.Finance</c> is exempt for the same
/// reason — it is where Finance's own repository ports live, and a port that returns an account has
/// to name the type it returns.
/// </para>
/// </remarks>
public sealed class FinanceRulesTests
{
    private const string AccountTypeName = "VumaRetail.Domain.Finance.Account";

    /// <summary>The namespace holding Finance's own ports, which legitimately name an account.</summary>
    private const string FinanceAbstractions = "VumaRetail.Application.Abstractions.Finance";

    /// <summary>Every assembly that must not know a GL account exists.</summary>
    private static readonly Assembly[] ProducerAssemblies =
    [
        typeof(VumaRetail.Sync.AssemblyMarker).Assembly,
        typeof(VumaRetail.Licensing.AssemblyMarker).Assembly,
        typeof(VumaRetail.PublicApi.AssemblyMarker).Assembly,
        typeof(VumaRetail.Contracts.AssemblyMarker).Assembly,
    ];

    [Fact]
    public void No_module_outside_finance_references_a_GL_account()
    {
        foreach (Assembly assembly in ProducerAssemblies)
        {
            Types.InAssembly(assembly)
                .Should()
                .NotHaveDependencyOn(AccountTypeName)
                .GetResult()
                .ShouldPass(
                    $"{assembly.GetName().Name} holds a type dependency on {AccountTypeName}. "
                    + "Modules raise an IFinancialEvent naming amounts; the posting rules engine "
                    + "decides which accounts those amounts land on (CLAUDE.md §7 rule 12).");
        }
    }

    [Fact]
    public void No_application_code_outside_finances_own_ports_references_a_GL_account()
    {
        // VumaRetail.Application is where a future module's handlers will live, so it is the most
        // likely place for the rule to be broken first — but its Abstractions.Finance namespace is
        // Finance's own port surface and must be allowed to name the type.
        Types.InAssembly(typeof(VumaRetail.Application.AssemblyMarker).Assembly)
            .That()
            .DoNotResideInNamespaceStartingWith(FinanceAbstractions)
            .Should()
            .NotHaveDependencyOn(AccountTypeName)
            .GetResult()
            .ShouldPass(
                "Application code outside Abstractions.Finance depends on the GL Account type. "
                + "Raise an IFinancialEvent instead (CLAUDE.md §7 rule 12).");
    }

    [Fact]
    public void The_financial_event_contract_gives_a_producer_nowhere_to_put_an_account_id()
    {
        // The half a type-dependency check cannot see: a module that never references Account but
        // passes a raw Guid it treats as one. The defence is that the contract has no field to put
        // it in, so this asserts the contract's shape — if a property is ever added here that could
        // carry an account, rule 12 stops being true by construction and this test says so.
        string[] properties = [.. typeof(IFinancialEvent).GetProperties().Select(property => property.Name)];

        Assert.True(
            properties.All(name => !name.Contains("Account", StringComparison.OrdinalIgnoreCase)),
            $"""
            IFinancialEvent has grown a property that can name a GL account: {string.Join(", ", properties)}.

            Rule 12 is true by construction because a producer has nowhere to put an account id, not
            because reviewers keep noticing. Remove the property and let the posting rule decide the
            account (docs/stages/STAGE-07-finance.md).
            """);
    }

    [Fact]
    public void The_posting_engine_is_the_only_way_into_the_ledger_from_outside_finance()
    {
        // IFinancialEventPoster is the single door. If a second implementation ever appears outside
        // the Finance module, a producer can be handed one that writes journals by its own rules and
        // rule 12 is unenforceable regardless of what the account-dependency tests say.
        List<Type> implementations =
        [
            .. new[]
                {
                    typeof(VumaRetail.Application.AssemblyMarker).Assembly,
                    typeof(VumaRetail.Infrastructure.AssemblyMarker).Assembly,
                    typeof(VumaRetail.Sync.AssemblyMarker).Assembly,
                    typeof(VumaRetail.Licensing.AssemblyMarker).Assembly,
                    typeof(VumaRetail.Finance.AssemblyMarker).Assembly,
                }
                .SelectMany(assembly => assembly.GetTypes())
                .Where(type => type is { IsAbstract: false, IsInterface: false })
                .Where(type => typeof(IFinancialEventPoster).IsAssignableFrom(type)),
        ];

        Type implementation = Assert.Single(implementations);
        Assert.Equal("VumaRetail.Finance", implementation.Assembly.GetName().Name);
    }
}
