using System.Globalization;
using System.Text.RegularExpressions;

namespace Iccc.Application.Assistant;

public static partial class NarrativeGuard
{
    public static string Apply(string modelText, IReadOnlyCollection<decimal> allowedNumbers)
    {
        if (string.IsNullOrWhiteSpace(modelText))
        {
            return "";
        }

        var kept = SentencePattern().Split(modelText.Trim())
            .Select(sentence => sentence.Trim())
            .Where(sentence => sentence.Length > 0)
            .Select(sentence => SentenceAllowed(sentence, allowedNumbers)
                ? sentence
                : "A numerical claim was removed because it was not produced by the calculation engine.");
        return string.Join(" ", kept);
    }

    private static bool SentenceAllowed(string sentence, IReadOnlyCollection<decimal> allowedNumbers)
    {
        foreach (Match match in NumberPattern().Matches(sentence))
        {
            if (!decimal.TryParse(match.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
            {
                return false;
            }

            var allowed = allowedNumbers.Any(candidate => Math.Abs(candidate - number) < 0.05m);
            if (!allowed)
            {
                return false;
            }
        }

        return true;
    }

    [GeneratedRegex(@"(?<=[\.!?])\s+")]
    private static partial Regex SentencePattern();

    [GeneratedRegex(@"\d+(?:\.\d+)?")]
    private static partial Regex NumberPattern();
}
