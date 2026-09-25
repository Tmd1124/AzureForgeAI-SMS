using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms.Core.Tests.Utils;

public class OneTimeCodeDetectorTests
{
    [Theory]
    [InlineData("Your Chase verification code is 482913. Don't share it.", "482913")]
    [InlineData("482913 is your Amazon OTP. Do not share it with anyone.", "482913")]
    [InlineData("G-123456 is your Google verification code.", "123456")]
    [InlineData("Your security code: 123-456", "123456")]
    [InlineData("Your login code is 123 456", "123456")]
    [InlineData("Use passcode 8841 to sign in", "8841")]
    [InlineData("Your one-time code is 55012345", "55012345")]
    [InlineData("Your PIN is 0042", "0042")]
    [InlineData("Wells Fargo: code 771204. Questions? Call 800-869-3557", "771204")]
    public void Extract_finds_the_code(string body, string expected)
    {
        Assert.Equal(expected, OneTimeCodeDetector.Extract(body));
    }

    [Theory]
    [InlineData("See you at 5, bring the 2 chairs")]
    [InlineData("Your order 48291374 has shipped")]
    [InlineData("Your code is ready, call 555-014-2231")]
    [InlineData("Promo code SAVE20 gets you $2000 off")]
    [InlineData("Your verification is complete")]
    [InlineData("Enter code at 10:30 tomorrow")]
    [InlineData("")]
    public void Extract_returns_null_when_there_is_no_code(string body)
    {
        Assert.Null(OneTimeCodeDetector.Extract(body));
    }
}
