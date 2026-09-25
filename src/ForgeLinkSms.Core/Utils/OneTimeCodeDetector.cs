using System.Text.RegularExpressions;

namespace ForgeLinkSms.Core.Utils;

public static partial class OneTimeCodeDetector
{
    [GeneratedRegex(@"\b(code|otp|passcode|pin|verification|one[- ]time|2fa|authentication)\b", RegexOptions.IgnoreCase)]
    private static partial Regex KeywordRegex();

    // The lookarounds reject digits that are really part of a phone number, price, time, or
    // longer number (e.g. "555-014-2231", "$2000", "10:30"). The optional letter prefix covers
    // formats like Google's "G-123456".
    [GeneratedRegex(@"(?<![\d$€£#.,:/-])(?:[A-Z]{1,3}-)?(\d{3}[- ]\d{3}|\d{4,8})(?![\d%]|[-.,:/]\d)")]
    private static partial Regex CandidateRegex();

    public static string? Extract(string? body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return null;
        }

        var keyword = KeywordRegex().Match(body);
        if (!keyword.Success)
        {
            return null;
        }

        var candidates = CandidateRegex().Matches(body);
        if (candidates.Count == 0)
        {
            return null;
        }

        var best = candidates.FirstOrDefault(c => c.Index >= keyword.Index) ?? candidates[0];
        return best.Groups[1].Value.Replace("-", string.Empty).Replace(" ", string.Empty);
    }
}
