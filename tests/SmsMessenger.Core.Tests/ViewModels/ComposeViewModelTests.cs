using Moq;
using SmsMessenger.Core.Services;
using SmsMessenger.Core.ViewModels;

namespace SmsMessenger.Core.Tests.ViewModels;

public class ComposeViewModelTests
{
    [Fact]
    public void AddRecipientCommand_rejects_invalid_numbers()
    {
        var sms = new Mock<ISmsService>();
        var viewModel = new ComposeViewModel(sms.Object);

        viewModel.AddRecipientCommand.Execute("abc");

        Assert.Empty(viewModel.Recipients);
    }

    [Fact]
    public void AddRecipientCommand_accepts_valid_numbers_and_dedupes()
    {
        var sms = new Mock<ISmsService>();
        var viewModel = new ComposeViewModel(sms.Object);

        viewModel.AddRecipientCommand.Execute("5550148890");
        viewModel.AddRecipientCommand.Execute("5550148890");

        Assert.Single(viewModel.Recipients);
    }

    [Fact]
    public void IsGroupSend_is_true_only_with_more_than_one_recipient()
    {
        var sms = new Mock<ISmsService>();
        var viewModel = new ComposeViewModel(sms.Object);

        Assert.False(viewModel.IsGroupSend);
        viewModel.AddRecipientCommand.Execute("5550148890");
        Assert.False(viewModel.IsGroupSend);
        viewModel.AddRecipientCommand.Execute("5550142231");
        Assert.True(viewModel.IsGroupSend);
    }

    [Fact]
    public async Task SendCommand_sends_individually_to_every_recipient()
    {
        var sms = new Mock<ISmsService>();
        var viewModel = new ComposeViewModel(sms.Object) { MessageBody = "hello everyone" };
        viewModel.AddRecipientCommand.Execute("5550148890");
        viewModel.AddRecipientCommand.Execute("5550142231");

        await viewModel.SendCommand.ExecuteAsync(null);

        sms.Verify(s => s.SendAsync("5550148890", "hello everyone"), Times.Once);
        sms.Verify(s => s.SendAsync("5550142231", "hello everyone"), Times.Once);
    }
}
