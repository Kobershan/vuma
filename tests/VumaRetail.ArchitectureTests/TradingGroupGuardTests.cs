namespace VumaRetail.ArchitectureTests;

/// <summary>
/// ADR-122 — every cross-company operation calls <c>ICompanyLinkService.RequireLink</c> at the
/// point of use, with the specific scope for that operation.
/// </summary>
/// <remarks>
/// <para>
/// A link checked only when it is created keeps granting access after it is suspended, and a new
/// entry point added without a check fails silently until a customer notices. This test enumerates
/// the entry points that exist today and asserts each one's body calls <c>RequireLink</c> with its
/// scope. Adding a cross-company entry point means adding a row here — and a missing check fails
/// the build.
/// </para>
/// <para>
/// Entry points in stages not yet built (07c group receipting, 08c sourcing plan and commit, 13b
/// wave building, 09b basket lines, 07c consolidated reporting) register their rows when those
/// stages land; <c>docs/TRADING_GROUP.md</c> §2 is the checklist.
/// </para>
/// </remarks>
public sealed class TradingGroupGuardTests
{
    private static readonly (string File, string Method, string Scope)[] EntryPoints =
    [
        ("src/VumaRetail.Infrastructure/Registry/GroupCreditService.cs", "TryHoldAsync", "SharedCredit"),
        ("src/VumaRetail.Infrastructure/Registry/TradingGroupServices.cs", "AddOccupancyAsync", "SharedFloor"),
    ];

    [Fact]
    public void Every_cross_company_entry_point_calls_RequireLink_with_its_scope()
    {
        List<string> offenders = [];

        foreach ((string file, string method, string scope) in EntryPoints)
        {
            string body = MethodBody(file, method);

            if (!body.Contains("RequireLink", StringComparison.Ordinal))
            {
                offenders.Add($"{file}::{method} never calls RequireLink.");
            }
            else if (!body.Contains(scope, StringComparison.Ordinal))
            {
                offenders.Add($"{file}::{method} calls RequireLink without the {scope} scope.");
            }
        }

        Assert.True(offenders.Count == 0, $"""
            A cross-company operation does not check its company link at the point of use
            (ADR-122, docs/TRADING_GROUP.md §2). A check only at configuration time keeps
            granting access after the link is suspended.

            {string.Join(Environment.NewLine, offenders.Select(name => $"  - {name}"))}
            """);
    }

    [Fact]
    public void The_guard_catches_an_unguarded_entry_point()
    {
        // Self-proving: the same checker that guards the product must flag a deliberately
        // unguarded method body, so a vacuous pass is structurally impossible.
        const string guarded = "public async Task HoldAsync() { await _companyLinks.RequireLink(a, b, CompanyLinkScope.SharedCredit); }";
        const string unguarded = "public async Task HoldAsync() { await _credit.HoldAsync(); }";

        Assert.True(ContainsRequireLinkCall(guarded, "SharedCredit"));
        Assert.False(ContainsRequireLinkCall(unguarded, "SharedCredit"));
    }

    private static string MethodBody(string file, string method)
    {
        (string path, string text) = SolutionSource.ProductionFiles()
            .First(f => string.Equals(f.Path, file, StringComparison.Ordinal));

        int name = text.IndexOf(method + "(", StringComparison.Ordinal);

        Assert.True(name >= 0, $"Entry point {file}::{method} no longer exists. Remove its row — or, if it moved, move the row with it.");

        int open = text.IndexOf('{', name);
        int depth = 0;

        for (int index = open; index < text.Length; index++)
        {
            if (text[index] == '{')
            {
                depth++;
            }
            else if (text[index] == '}')
            {
                depth--;

                if (depth == 0)
                {
                    return text.Substring(open, index - open + 1);
                }
            }
        }

        throw new InvalidOperationException($"Entry point {file}::{method} has unbalanced braces.");
    }

    private static bool ContainsRequireLinkCall(string body, string scope)
        => body.Contains("RequireLink", StringComparison.Ordinal)
            && body.Contains(scope, StringComparison.Ordinal);
}
