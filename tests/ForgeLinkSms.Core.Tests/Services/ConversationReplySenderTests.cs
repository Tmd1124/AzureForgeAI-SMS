using Moq;
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;

namespace ForgeLinkSms.Core.Tests.Services;

public class ConversationReplySenderTests
{
    private readonly Mock<ISmsService> _sms = new();
    private readonly Mock<IThreadService> _threads = new();
    private readonly Mock<IMarkAsReadService> _markAsRead = new();

    private ConversationReplySender MakeSender() => new(_sms.Object, _threads.Object, _markAsRead.Object);

    [Fact]
    public async Task A_reply_in_a_one_to_one_conversation_is_a_plain_text()
    {
        _threads.Setup(t => t.GetParticipantsAsync(4)).ReturnsAsync(new[] { "+14707583374" });

        await MakeSender().SendAsync(4, "+14707583374", " On my way ");

        _sms.Verify(s => s.SendAsync("+14707583374", "On my way"), Times.Once);
    }

    [Fact]
    public async Task A_reply_in_a_group_goes_to_everyone()
    {
        var group = new[] { "+14707583374", "+16782628755" };
        _threads.Setup(t => t.GetParticipantsAsync(4)).ReturnsAsync(group);

        await MakeSender().SendAsync(4, "+14707583374", "On my way");

        _sms.Verify(s => s.SendGroupAsync(4, group, "On my way", null), Times.Once);
        _sms.Verify(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Replying_marks_the_conversation_read()
    {
        _threads.Setup(t => t.GetParticipantsAsync(4)).ReturnsAsync(new[] { "555" });

        await MakeSender().SendAsync(4, "555", "ok");

        _markAsRead.Verify(m => m.MarkThreadAsReadAsync(4), Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_reply_sends_nothing(string text)
    {
        await MakeSender().SendAsync(4, "555", text);

        _sms.Verify(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _sms.Verify(s => s.SendGroupAsync(It.IsAny<long>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>(), It.IsAny<PickedAttachment?>()), Times.Never);
    }
}
