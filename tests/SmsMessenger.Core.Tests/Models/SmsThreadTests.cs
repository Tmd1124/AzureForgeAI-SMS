using SmsMessenger.Core.Models;

namespace SmsMessenger.Core.Tests.Models;

public class SmsThreadTests
{
    [Fact]
    public void PreviewText_truncates_long_bodies_to_60_chars_with_ellipsis()
    {
        var longBody = new string('a', 100);
        var thread = new SmsThread
        {
            Id = 1,
            Address = "5550142231",
            DisplayName = null,
            LastMessageBody = longBody,
            LastMessageTimestamp = DateTimeOffset.UtcNow,
            UnreadCount = 0
        };

        Assert.Equal(new string('a', 60) + "…", thread.PreviewText);
    }

    [Fact]
    public void PreviewText_leaves_short_bodies_unchanged()
    {
        var thread = new SmsThread
        {
            Id = 1,
            Address = "5550142231",
            DisplayName = null,
            LastMessageBody = "short message",
            LastMessageTimestamp = DateTimeOffset.UtcNow,
            UnreadCount = 0
        };

        Assert.Equal("short message", thread.PreviewText);
    }

    [Fact]
    public void DisplayNameOrAddress_falls_back_to_address_when_no_contact_match()
    {
        var thread = new SmsThread
        {
            Id = 1,
            Address = "5550142231",
            DisplayName = null,
            LastMessageBody = "hi",
            LastMessageTimestamp = DateTimeOffset.UtcNow,
            UnreadCount = 0
        };

        Assert.Equal("5550142231", thread.DisplayNameOrAddress);
    }

    [Fact]
    public void DisplayNameOrAddress_prefers_contact_name()
    {
        var thread = new SmsThread
        {
            Id = 1,
            Address = "5550142231",
            DisplayName = "Alice Smith",
            LastMessageBody = "hi",
            LastMessageTimestamp = DateTimeOffset.UtcNow,
            UnreadCount = 0
        };

        Assert.Equal("Alice Smith", thread.DisplayNameOrAddress);
    }
}
