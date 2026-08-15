using NetArchTest.Rules;

namespace VumaRetail.ArchitectureTests;

/// <summary>
/// Turns a NetArchTest result into a failure message that names the offending types and says which
/// rule they break.
/// </summary>
/// <remarks>
/// "Expected true but found false" tells whoever broke the build nothing. These tests exist to be read
/// by someone who has just been stopped by one, so the message carries the rule and the fix.
/// </remarks>
internal static class TestResultExtensions
{
    /// <summary>Asserts the rule held, listing every failing type if it did not.</summary>
    /// <param name="result">The NetArchTest result.</param>
    /// <param name="because">Why the rule exists and what to do instead.</param>
    public static void ShouldPass(this TestResult result, string because)
    {
        if (result.IsSuccessful)
        {
            return;
        }

        IEnumerable<string> offenders = result.FailingTypeNames ?? [];
        string detail = string.Join(Environment.NewLine, offenders.Select(name => $"  - {name}"));

        Assert.Fail($"""
            Architecture rule violated.

            {because}

            Offending types:
            {detail}
            """);
    }
}
