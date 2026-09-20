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

    [GeneratedRegex(@"[^\d]")]
    private static partial Regex DigitsOnlyRegex();
}
