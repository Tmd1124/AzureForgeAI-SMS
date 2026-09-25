using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms.Core.Tests.Utils;

public class SenderScreeningTests
{
    private static readonly IReadOnlySet<string> NoneAllowed = new HashSet<string>();

    private static SmsThread MakeThread(string? name = null, bool hasOutgoing = false, bool isFavorite = false, string address = "(855) 201-4477") => new()
    {
        Id = 1,
        Address = address,
        DisplayName = name,
        LastMessageBody = "hi",
        LastMessageTimestamp = DateTimeOffset.UtcNow,
        UnreadCount = 0,
        HasOutgoing = hasOutgoing,
        IsFavorite = isFavorite
    };

    [Fact]
    public void IsScreened_is_true_for_an_unknown_sender_you_never_replied_to()
    {
        Assert.True(SenderScreening.IsScreened(MakeThread(), NoneAllowed));
    }

    [Fact]
    public void IsScreened_is_false_for_a_contact()
    {
        Assert.False(SenderScreening.IsScreened(MakeThread(name: "Mom"), NoneAllowed));
    }

    [Fact]
    public void IsScreened_is_false_once_you_have_texted_them()
    {
        Assert.False(SenderScreening.IsScreened(MakeThread(hasOutgoing: true), NoneAllowed));
    }

    [Fact]
    public void IsScreened_is_false_for_a_favorite()
    {
        Assert.False(SenderScreening.IsScreened(MakeThread(isFavorite: true), NoneAllowed));
    }

    [Fact]
    public void IsScreened_is_false_for_an_allowed_number_in_any_format()
    {
        Assert.False(SenderScreening.IsScreened(MakeThread(address: "+1 855-201-4477"), new HashSet<string> { "8552014477" }));
    }

    [Theory]
    [InlineData(true, false, false, "hi")]
    [InlineData(false, true, false, "hi")]
    [InlineData(false, false, true, "hi")]
    [InlineData(false, false, false, "Your verification code is 482913")]
    public void ShouldNotify_for_known_senders_and_codes(bool isContact, bool isAllowed, bool hasOutgoing, string body)
    {
        Assert.True(SenderScreening.ShouldNotify(isContact, isAllowed, hasOutgoing, body));
    }

    [Fact]
    public void ShouldNotify_is_false_for_an_unknown_sender_without_a_code()
    {
        Assert.False(SenderScreening.ShouldNotify(false, false, false, "USPS: package on hold, confirm address"));
    }
}
