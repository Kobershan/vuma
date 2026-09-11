using System.Text.RegularExpressions;

namespace VumaRetail.Application.Conversations;

/// <summary>Checks that a composed reply does not introduce numeric or date facts.</summary>
public static partial class ReplySafety
{
    /// <summary>Returns false when a number or ISO date is absent from the API facts.</summary>
    public static bool ContainsOnlyApiFacts(string reply, ReplyFacts facts)
    {
        ArgumentNullException.ThrowIfNull(reply);
        ArgumentNullException.ThrowIfNull(facts);
        string source = string.Join(" ", facts.Facts);

        foreach (Match match in NumericFactRegex().Matches(reply))
        {
            if (!source.Contains(match.Value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        foreach (Match match in IsoDateRegex().Matches(reply))
        {
            if (!source.Contains(match.Value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    [GeneratedRegex(@"(?<![A-Za-z])(?:\d+(?:[.,]\d+)?)(?![A-Za-z])", RegexOptions.CultureInvariant)]
    private static partial Regex NumericFactRegex();

    [GeneratedRegex(@"\b\d{4}-\d{2}-\d{2}\b", RegexOptions.CultureInvariant)]
    private static partial Regex IsoDateRegex();
}
