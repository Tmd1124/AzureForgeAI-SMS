using Moq;
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;
using ForgeLinkSms.Core.ViewModels;

namespace ForgeLinkSms.Core.Tests.ViewModels;

public class ThreadDetailViewModelTests
{
    [Fact]
    public async Task LoadCommand_populates_Messages_in_chronological_order()
    {
        var sms = new Mock<ISmsService>();
        sms.Setup(s => s.GetMessagesAsync(1)).ReturnsAsync(new List<SmsMessage>
        {
            new() { Id = 2, ThreadId = 1, Address = "555", Body = "second", Timestamp = DateTimeOffset.UtcNow, IsOutgoing = true, Status = SmsMessageStatus.Sent },
            new() { Id = 1, ThreadId = 1, Address = "555", Body = "first", Timestamp = DateTimeOffset.UtcNow.AddMinutes(-5), IsOutgoing = false, Status = SmsMessageStatus.Delivered }
        });
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "555");

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.Messages.Count);
        Assert.Equal("first", viewModel.Messages[0].Body);
        Assert.Equal("second", viewModel.Messages[1].Body);
    }

    [Fact]
    public async Task SendCommand_does_nothing_when_ComposeText_is_blank()
    {
        var sms = new Mock<ISmsService>();
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "555")
        {
            ComposeText = "   "
        };

        await viewModel.SendCommand.ExecuteAsync(null);

        sms.Verify(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SendCommand_sends_and_clears_ComposeText()
    {
        var sms = new Mock<ISmsService>();
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "5550148890")
        {
            ComposeText = "See you then"
        };

        await viewModel.SendCommand.ExecuteAsync(null);

        sms.Verify(s => s.SendAsync("5550148890", "See you then"), Times.Once);
        Assert.Equal(string.Empty, viewModel.ComposeText);
    }

    [Fact]
    public async Task SendReactionCommand_sends_the_emoji_quoting_the_target_message()
    {
        var sms = new Mock<ISmsService>();
        sms.Setup(s => s.GetMessagesAsync(1)).ReturnsAsync(new List<SmsMessage>());
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "5550148890");

        await viewModel.SendReactionCommand.ExecuteAsync(("👍", "You still coming over"));

        sms.Verify(s => s.SendAsync("5550148890", "👍 to \"You still coming over\""), Times.Once);
    }

    [Fact]
    public async Task SendReactionCommand_truncates_a_long_target_message()
    {
        var sms = new Mock<ISmsService>();
        sms.Setup(s => s.GetMessagesAsync(1)).ReturnsAsync(new List<SmsMessage>());
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "5550148890");
        var longBody = new string('a', 60);

        await viewModel.SendReactionCommand.ExecuteAsync(("❤️", longBody));

        var expectedQuote = new string('a', 40) + "…";
        sms.Verify(s => s.SendAsync("5550148890", $"❤️ to \"{expectedQuote}\""), Times.Once);
    }

    [Fact]
    public async Task SendReactionCommand_reloads_messages_after_sending()
    {
        var sms = new Mock<ISmsService>();
        sms.Setup(s => s.GetMessagesAsync(1)).ReturnsAsync(new List<SmsMessage>
        {
            new() { Id = 1, ThreadId = 1, Address = "5550148890", Body = "👍 to \"hi\"", Timestamp = DateTimeOffset.UtcNow, IsOutgoing = true, Status = SmsMessageStatus.Sent }
        });
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "5550148890");

        await viewModel.SendReactionCommand.ExecuteAsync(("👍", "hi"));

        Assert.Single(viewModel.Messages);
    }
}
