using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Tests.Models;

public class SmsMessageTests
{
    [Theory]
    [InlineData(SmsMessageStatus.Sending, "Sending…")]
    [InlineData(SmsMessageStatus.Sent, "✓ Sent")]
    [InlineData(SmsMessageStatus.Delivered, "✓✓ Delivered")]
    [InlineData(SmsMessageStatus.Failed, "⚠ Not sent")]
    public void StatusDisplay_reflects_status(SmsMessageStatus status, string expected)
    {
        var message = new SmsMessage
        {
            Id = 1,
            ThreadId = 1,
            Address = "5550142231",
            Body = "hi",
            Timestamp = DateTimeOffset.UtcNow,
            IsOutgoing = true,
            Status = status
        };

        Assert.Equal(expected, message.StatusDisplay);
    }

    [Fact]
    public void StatusDisplay_is_blank_for_incoming_messages()
    {
        var message = new SmsMessage
        {
            Id = 1,
            ThreadId = 1,
            Address = "5550142231",
            Body = "hi",
            Timestamp = DateTimeOffset.UtcNow,
            IsOutgoing = false,
            Status = SmsMessageStatus.Delivered
        };

        Assert.Equal(string.Empty, message.StatusDisplay);
    }
}
