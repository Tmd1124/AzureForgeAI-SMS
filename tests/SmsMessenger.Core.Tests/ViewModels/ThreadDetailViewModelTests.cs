using Moq;
using SmsMessenger.Core.Models;
using SmsMessenger.Core.Services;
using SmsMessenger.Core.ViewModels;

namespace SmsMessenger.Core.Tests.ViewModels;

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
        var viewModel = new ThreadDetailViewModel(sms.Object, threadId: 1, address: "555");

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.Messages.Count);
        Assert.Equal("first", viewModel.Messages[0].Body);
        Assert.Equal("second", viewModel.Messages[1].Body);
    }

    [Fact]
    public async Task SendCommand_does_nothing_when_ComposeText_is_blank()
    {
        var sms = new Mock<ISmsService>();
        var viewModel = new ThreadDetailViewModel(sms.Object, threadId: 1, address: "555")
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
        var viewModel = new ThreadDetailViewModel(sms.Object, threadId: 1, address: "5550148890")
        {
            ComposeText = "See you then"
        };

        await viewModel.SendCommand.ExecuteAsync(null);

        sms.Verify(s => s.SendAsync("5550148890", "See you then"), Times.Once);
        Assert.Equal(string.Empty, viewModel.ComposeText);
    }
}
