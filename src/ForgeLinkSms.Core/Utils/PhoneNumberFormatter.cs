using System.Text.RegularExpressions;

namespace ForgeLinkSms.Core.Utils;

public static partial class PhoneNumberFormatter
{
    public static string ToDisplayFormat(string raw)
    {
        var digits = DigitsOnlyRegex().Replace(raw, "");

        return digits.Length switch
        {
            10 => $"({digits[..3]}) {digits[3..6]}-{digits[6..]}",
            11 when digits[0] == '1' => $"+1 ({digits[1..4]}) {digits[4..7]}-{digits[7..]}",
            _ => raw
        };
    }

    public static string ToComparableDigits(string raw)
    {
        var digits = DigitsOnlyRegex().Replace(raw, "");
        return digits.Length == 11 && digits[0] == '1' ? digits[1..] : digits;
    }

    /// True for addresses that are actually dialable full phone numbers, with the usual dialing
    /// punctuation allowed. False for alphanumeric sender IDs (e.g. "CarelonRx Pharmacy") and RCS
    /// business-messaging addresses, which aren't phone numbers at all, and false for short codes
    /// (e.g. "50757") — real but not dialable, so they don't get a call button either.
    public static bool IsValidPhoneNumber(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || !DialablePunctuationRegex().IsMatch(raw))
        {
            return false;
        }

        var digits = DigitsOnlyRegex().Replace(raw, "");
        return digits.Length is >= 10 and <= 15;
    }

    [GeneratedRegex(@"[^\d]")]
    private static partial Regex DigitsOnlyRegex();

    [GeneratedRegex(@"^[\d+\-.() ]+$")]
    private static partial Regex DialablePunctuationRegex();
}
