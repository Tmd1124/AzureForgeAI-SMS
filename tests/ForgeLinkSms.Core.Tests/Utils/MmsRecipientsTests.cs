using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms.Core.Tests.Utils;

public class MmsRecipientsTests
{
    [Fact]
    public void OtherParticipants_for_a_one_to_one_message_is_just_the_sender_even_when_my_number_is_unknown()
    {
        var others = MmsRecipients.OtherParticipants("+14707583374", new[] { "+17708654177" }, Array.Empty<string>(), Array.Empty<string>());

        Assert.Equal(new[] { "+14707583374" }, others);
    }

    [Fact]
    public void OtherParticipants_for_a_group_removes_me_in_any_number_format()
    {
        var others = MmsRecipients.OtherParticipants(
            "+14707583374",
            new[] { "+17708654177", "+16782628755" },
            new[] { "404-555-0123" },
            selfNumbers: new[] { "(770) 865-4177" });

        Assert.Equal(new[] { "+14707583374", "+16782628755", "404-555-0123" }, others);
    }

    [Fact]
    public void OtherParticipants_drops_duplicates_and_blanks()
    {
        var others = MmsRecipients.OtherParticipants(
            "+14707583374",
            new[] { "14707583374", "", "+16782628755", "+1 678-262-8755" },
            Array.Empty<string>(),
            selfNumbers: new[] { "+17708654177" });

        Assert.Equal(new[] { "+14707583374", "+16782628755" }, others);
    }

    [Fact]
    public void OtherParticipants_without_a_sender_uses_the_recipients()
    {
        var others = MmsRecipients.OtherParticipants(null, new[] { "+17708654177", "+16782628755" }, Array.Empty<string>(), new[] { "7708654177" });

        Assert.Equal(new[] { "+16782628755" }, others);
    }

    [Fact]
    public void MostLikelySelfNumber_is_the_recipient_that_appears_on_the_most_incoming_messages()
    {
        var incomingRecipients = new[]
        {
            new[] { "+17708654177" },
            new[] { "+17708654177", "+16782628755" },
            new[] { "7708654177" },
            new[] { "+16782628755" }
        };

        Assert.Equal("+17708654177", MmsRecipients.MostLikelySelfNumber(incomingRecipients));
    }

    [Fact]
    public void MostLikelySelfNumber_is_null_without_history()
    {
        Assert.Null(MmsRecipients.MostLikelySelfNumber(Array.Empty<string[]>()));
    }
}
