using Moq;
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;
using ForgeLinkSms.Core.ViewModels;

namespace ForgeLinkSms.Core.Tests.ViewModels;

public class ConversationMediaViewModelTests
{
    private static SmsMessage Message(long id, string body, int daysAgo, bool outgoing = false) => new()
    {
        Id = id,
        ThreadId = 3,
        Address = "555",
        Body = body,
        Timestamp = DateTimeOffset.UtcNow.AddDays(-daysAgo),
        IsOutgoing = outgoing,
        Status = SmsMessageStatus.Delivered
    };

    [Fact]
    public async Task LoadCommand_lists_photos_and_videos_newest_first()
    {
        var sms = new Mock<ISmsService>();
        var older = new SharedMedia(10, AttachmentKind.Image, "a.jpg", DateTimeOffset.UtcNow.AddDays(-5), false);
        var newer = new SharedMedia(11, AttachmentKind.Video, "b.mp4", DateTimeOffset.UtcNow.AddDays(-1), true);
        sms.Setup(s => s.GetSharedMediaAsync(3)).ReturnsAsync(new List<SharedMedia> { older, newer });
        sms.Setup(s => s.SearchMessagesAsync(3, It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync(new List<SmsMessage>());
        var viewModel = new ConversationMediaViewModel(sms.Object, threadId: 3);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(new long[] { 11, 10 }, viewModel.Media.Select(m => m.PartId));
    }

    [Fact]
    public async Task LoadCommand_collects_every_link_once_newest_first()
    {
        var sms = new Mock<ISmsService>();
        sms.Setup(s => s.GetSharedMediaAsync(3)).ReturnsAsync(new List<SharedMedia>());
        sms.Setup(s => s.SearchMessagesAsync(3, "http", It.IsAny<int>())).ReturnsAsync(new List<SmsMessage>
        {
            Message(1, "look https://example.com/recipe and https://youtu.be/abc", 1),
            Message(2, "old one https://example.com/recipe", 4, outgoing: true)
        });
        sms.Setup(s => s.SearchMessagesAsync(3, "www.", It.IsAny<int>())).ReturnsAsync(new List<SmsMessage>
        {
            Message(3, "try www.weather.gov", 2)
        });
        var viewModel = new ConversationMediaViewModel(sms.Object, threadId: 3);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "https://example.com/recipe", "https://youtu.be/abc", "www.weather.gov" }, viewModel.Links.Select(l => l.Text));
        Assert.Equal("example.com", viewModel.Links[0].Domain);
    }
}
