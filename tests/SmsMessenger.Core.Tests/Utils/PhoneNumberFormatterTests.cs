using SmsMessenger.Core.Utils;

namespace SmsMessenger.Core.Tests.Utils;

public class PhoneNumberFormatterTests
{
    [Theory]
    [InlineData("5550142231", "(555) 014-2231")]
    [InlineData("15550142231", "+1 (555) 014-2231")]
    [InlineData("555-014-2231", "(555) 014-2231")]
    public void ToDisplayFormat_formats_valid_us_numbers(string raw, string expected)
    {
        Assert.Equal(expected, PhoneNumberFormatter.ToDisplayFormat(raw));
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("SHORTCODE12")]
    public void ToDisplayFormat_returns_input_unchanged_when_not_a_standard_length(string raw)
    {
        Assert.Equal(raw, PhoneNumberFormatter.ToDisplayFormat(raw));
    }

    [Theory]
    [InlineData("5550142231", "5550142231")]
    [InlineData("+15550142231", "5550142231")]
    [InlineData("15550142231", "5550142231")]
    [InlineData("(555) 014-2231", "5550142231")]
    public void ToComparableDigits_normalizes_equivalent_numbers_to_the_same_key(string raw, string expected)
    {
        Assert.Equal(expected, PhoneNumberFormatter.ToComparableDigits(raw));
    }
}
