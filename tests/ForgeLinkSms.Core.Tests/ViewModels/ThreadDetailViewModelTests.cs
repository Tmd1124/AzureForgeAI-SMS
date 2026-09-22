using Moq;
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;
using ForgeLinkSms.Core.ViewModels;

namespace ForgeLinkSms.Core.Tests.ViewModels;

public class ThreadDetailViewModelTests
{
    private static SmsMessage MakeMessage(long id, string body, DateTimeOffset timestamp, bool isOutgoing = false) => new()
    {
        Id = id,
        ThreadId = 1,
        Address = "555",
        Body = body,
        Timestamp = timestamp,
        IsOutgoing = isOutgoing,
        Status = isOutgoing ? SmsMessageStatus.Sent : SmsMessageStatus.Delivered
    };

    [Fact]
    public async Task LoadCommand_populates_Messages_in_chronological_order()
    {
        var sms = new Mock<ISmsService>();
        sms.Setup(s => s.GetMessagesAsync(1, null, It.IsAny<int>())).ReturnsAsync(new List<SmsMessage>
        {
            MakeMessage(2, "second", DateTimeOffset.UtcNow, isOutgoing: true),
            MakeMessage(1, "first", DateTimeOffset.UtcNow.AddMinutes(-5))
        });
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "555");

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.Messages.Count);
        Assert.Equal("first", viewModel.Messages[0].Body);
        Assert.Equal("second", viewModel.Messages[1].Body);
    }

    [Fact]
    public async Task LoadCommand_sets_LoadFailed_instead_of_throwing_when_the_sms_service_throws()
    {
        var sms = new Mock<ISmsService>();
        sms.Setup(s => s.GetMessagesAsync(1, null, It.IsAny<int>())).ThrowsAsync(new InvalidOperationException("provider hiccup"));
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "555");

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.True(viewModel.LoadFailed);
        Assert.Empty(viewModel.Messages);
    }

    [Fact]
    public async Task LoadCommand_clears_LoadFailed_on_a_successful_retry()
    {
        var sms = new Mock<ISmsService>();
        sms.SetupSequence(s => s.GetMessagesAsync(1, null, It.IsAny<int>()))
            .ThrowsAsync(new InvalidOperationException("provider hiccup"))
            .ReturnsAsync(new List<SmsMessage> { MakeMessage(1, "hi", DateTimeOffset.UtcNow) });
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "555");
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.False(viewModel.LoadFailed);
        Assert.Single(viewModel.Messages);
    }

    [Fact]
    public async Task LoadCommand_sets_HasMoreMessages_false_when_the_first_page_is_short()
    {
        var sms = new Mock<ISmsService>();
        sms.Setup(s => s.GetMessagesAsync(1, null, It.IsAny<int>())).ReturnsAsync(new List<SmsMessage>
        {
            MakeMessage(1, "hi", DateTimeOffset.UtcNow)
        });
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "555");

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.False(viewModel.HasMoreMessages);
    }

    [Fact]
    public async Task LoadOlderMessagesCommand_prepends_an_older_page_using_the_oldest_loaded_timestamp_as_the_cursor()
    {
        // The ViewModel treats a full page (its internal page size, currently 50) as a signal
        // there might be more — return exactly that many so HasMoreMessages stays true and
        // LoadOlderMessagesCommand actually fetches, rather than short-circuiting.
        var newest = DateTimeOffset.UtcNow;
        var oldest = newest.AddMinutes(-50);
        var firstPage = Enumerable.Range(0, 50)
            .Select(i => MakeMessage(i + 100, $"msg-{i}", newest.AddMinutes(-i)))
            .ToList();
        firstPage[^1] = MakeMessage(1, "oldest-in-first-page", oldest);

        var sms = new Mock<ISmsService>();
        sms.Setup(s => s.GetMessagesAsync(1, null, It.IsAny<int>())).ReturnsAsync(firstPage);
        var olderMessage = MakeMessage(0, "even older", oldest.AddMinutes(-1));
        sms.Setup(s => s.GetMessagesAsync(1, oldest, It.IsAny<int>())).ReturnsAsync(new List<SmsMessage> { olderMessage });
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "555");
        await viewModel.LoadCommand.ExecuteAsync(null);
        Assert.True(viewModel.HasMoreMessages);

        await viewModel.LoadOlderMessagesCommand.ExecuteAsync(null);

        sms.Verify(s => s.GetMessagesAsync(1, oldest, It.IsAny<int>()), Times.Once);
        Assert.Equal(51, viewModel.Messages.Count);
        Assert.Equal("even older", viewModel.Messages[0].Body);
        Assert.Equal("oldest-in-first-page", viewModel.Messages[1].Body);
    }

    [Fact]
    public async Task LoadOlderMessagesCommand_trims_the_newest_end_once_total_messages_exceed_the_cap()
    {
        // Regression test: unbounded growth here is what produced a single render-batch payload
        // over 300MB in a media-heavy real thread (every loaded MMS attachment stays in memory
        // as a base64 image), which the JSON serializer shipping it to the WebView refused to
        // write, crashing the whole page. The cap (currently 150) must actually get enforced as
        // older pages come in, not just documented.
        List<SmsMessage> MakePage(int pageIndex, DateTimeOffset pageNewest) => Enumerable.Range(0, 50)
            .Select(i => MakeMessage(pageIndex * 1000 + i, $"p{pageIndex}-{i}", pageNewest.AddMinutes(-i)))
            .ToList();

        var now = DateTimeOffset.UtcNow;
        var page1 = MakePage(1, now);
        var page1Oldest = page1[^1].Timestamp;
        var page2 = MakePage(2, page1Oldest.AddMinutes(-1));
        var page2Oldest = page2[^1].Timestamp;
        var page3 = MakePage(3, page2Oldest.AddMinutes(-1));
        var page3Oldest = page3[^1].Timestamp;
        var page4 = MakePage(4, page3Oldest.AddMinutes(-1));

        var sms = new Mock<ISmsService>();
        sms.Setup(s => s.GetMessagesAsync(1, null, It.IsAny<int>())).ReturnsAsync(page1);
        sms.Setup(s => s.GetMessagesAsync(1, page1Oldest, It.IsAny<int>())).ReturnsAsync(page2);
        sms.Setup(s => s.GetMessagesAsync(1, page2Oldest, It.IsAny<int>())).ReturnsAsync(page3);
        sms.Setup(s => s.GetMessagesAsync(1, page3Oldest, It.IsAny<int>())).ReturnsAsync(page4);
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "555");
        await viewModel.LoadCommand.ExecuteAsync(null);
        await viewModel.LoadOlderMessagesCommand.ExecuteAsync(null);
        await viewModel.LoadOlderMessagesCommand.ExecuteAsync(null);
        Assert.Equal(150, viewModel.Messages.Count);

        await viewModel.LoadOlderMessagesCommand.ExecuteAsync(null);

        Assert.Equal(150, viewModel.Messages.Count);
        Assert.DoesNotContain(viewModel.Messages, m => m.Body == "p1-0");
        Assert.Contains(viewModel.Messages, m => m.Body == "p4-49");
    }

    [Fact]
    public async Task LoadNewerMessagesCommand_does_nothing_when_IsAtLatest_is_true()
    {
        var sms = new Mock<ISmsService>();
        sms.Setup(s => s.GetMessagesAsync(1, null, It.IsAny<int>())).ReturnsAsync(new List<SmsMessage>
        {
            MakeMessage(1, "hi", DateTimeOffset.UtcNow)
        });
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "555");
        await viewModel.LoadCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsAtLatest);

        await viewModel.LoadNewerMessagesCommand.ExecuteAsync(null);

        sms.Verify(s => s.GetNewerMessagesAsync(It.IsAny<long>(), It.IsAny<DateTimeOffset>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task LoadNewerMessagesCommand_catches_a_trimmed_window_back_up_to_latest()
    {
        // Regression test: scrolling up far enough that LoadOlderMessages trims the newest end
        // (see that test) discards the thread's actual latest messages from the loaded window.
        // Reported symptom: scrolling back down toward "now" stopped at whatever trimming had
        // left behind and never reached the real latest message, only fixable by leaving and
        // reopening the thread. LoadNewerMessages is the fetch-forward that must restore it
        // instead, trimming the *oldest* end this time so the window stays bounded either way.
        List<SmsMessage> MakePage(int pageIndex, DateTimeOffset pageNewest) => Enumerable.Range(0, 50)
            .Select(i => MakeMessage(pageIndex * 1000 + i, $"p{pageIndex}-{i}", pageNewest.AddMinutes(-i)))
            .ToList();

        var now = DateTimeOffset.UtcNow;
        var page1 = MakePage(1, now);
        var page1Oldest = page1[^1].Timestamp;
        var page2 = MakePage(2, page1Oldest.AddMinutes(-1));
        var page2Newest = page2[0].Timestamp;
        var page2Oldest = page2[^1].Timestamp;
        var page3 = MakePage(3, page2Oldest.AddMinutes(-1));
        var page3Oldest = page3[^1].Timestamp;
        var page4 = MakePage(4, page3Oldest.AddMinutes(-1));
        var catchUpPage = Enumerable.Range(0, 10)
            .Select(i => MakeMessage(9000 + i, $"newer-{i}", now.AddMinutes(i + 1)))
            .ToList();

        var sms = new Mock<ISmsService>();
        sms.Setup(s => s.GetMessagesAsync(1, null, It.IsAny<int>())).ReturnsAsync(page1);
        sms.Setup(s => s.GetMessagesAsync(1, page1Oldest, It.IsAny<int>())).ReturnsAsync(page2);
        sms.Setup(s => s.GetMessagesAsync(1, page2Oldest, It.IsAny<int>())).ReturnsAsync(page3);
        sms.Setup(s => s.GetMessagesAsync(1, page3Oldest, It.IsAny<int>())).ReturnsAsync(page4);
        // The window's newest displayed message after the trim below is page2's newest (page1
        // got trimmed away entirely) — LoadNewerMessages must resume from there, not from the
        // thread's true original latest ("now"), which trimming has already left behind.
        sms.Setup(s => s.GetNewerMessagesAsync(1, page2Newest, It.IsAny<int>())).ReturnsAsync(catchUpPage);
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "555");
        await viewModel.LoadCommand.ExecuteAsync(null);
        await viewModel.LoadOlderMessagesCommand.ExecuteAsync(null);
        await viewModel.LoadOlderMessagesCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsAtLatest);

        await viewModel.LoadOlderMessagesCommand.ExecuteAsync(null);
        Assert.False(viewModel.IsAtLatest);
        Assert.DoesNotContain(viewModel.Messages, m => m.Body == "p1-0");

        await viewModel.LoadNewerMessagesCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsAtLatest);
        Assert.True(viewModel.HasMoreMessages);
        Assert.Equal(150, viewModel.Messages.Count);
        Assert.Contains(viewModel.Messages, m => m.Body == "newer-9");
        Assert.DoesNotContain(viewModel.Messages, m => m.Body == "p4-49");
        Assert.Contains(viewModel.Messages, m => m.Body == "p4-0");
    }

    [Fact]
    public async Task LoadOlderMessagesCommand_does_nothing_when_HasMoreMessages_is_false()
    {
        var sms = new Mock<ISmsService>();
        sms.Setup(s => s.GetMessagesAsync(1, null, It.IsAny<int>())).ReturnsAsync(new List<SmsMessage>
        {
            MakeMessage(1, "only message", DateTimeOffset.UtcNow)
        });
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "555");
        await viewModel.LoadCommand.ExecuteAsync(null);
        Assert.False(viewModel.HasMoreMessages);

        await viewModel.LoadOlderMessagesCommand.ExecuteAsync(null);

        sms.Verify(s => s.GetMessagesAsync(1, It.IsAny<DateTimeOffset?>(), It.IsAny<int>()), Times.Once);
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
    public async Task SendCommand_sends_mms_with_caption_when_an_attachment_is_passed()
    {
        var sms = new Mock<ISmsService>();
        sms.Setup(s => s.GetMessagesAsync(1, null, It.IsAny<int>())).ReturnsAsync(new List<SmsMessage>());
        var attachment = new PickedAttachment { FileName = "photo.jpg", LocalPath = "/tmp/photo.jpg", Kind = AttachmentKind.Image };
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "5550148890")
        {
            ComposeText = "Check this out"
        };

        await viewModel.SendCommand.ExecuteAsync(attachment);

        sms.Verify(s => s.SendMmsAsync(1, "5550148890", "Check this out", attachment), Times.Once);
        sms.Verify(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        Assert.Equal(string.Empty, viewModel.ComposeText);
    }

    [Fact]
    public async Task SendCommand_sends_mms_with_no_body_when_ComposeText_is_blank()
    {
        var sms = new Mock<ISmsService>();
        sms.Setup(s => s.GetMessagesAsync(1, null, It.IsAny<int>())).ReturnsAsync(new List<SmsMessage>());
        var attachment = new PickedAttachment { FileName = "photo.jpg", LocalPath = "/tmp/photo.jpg", Kind = AttachmentKind.Image };
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "5550148890");

        await viewModel.SendCommand.ExecuteAsync(attachment);

        sms.Verify(s => s.SendMmsAsync(1, "5550148890", null, attachment), Times.Once);
    }

    [Fact]
    public async Task SendReactionCommand_sends_the_emoji_quoting_the_target_message()
    {
        var sms = new Mock<ISmsService>();
        sms.Setup(s => s.GetMessagesAsync(1, null, It.IsAny<int>())).ReturnsAsync(new List<SmsMessage>());
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "5550148890");

        await viewModel.SendReactionCommand.ExecuteAsync(("👍", "You still coming over"));

        sms.Verify(s => s.SendAsync("5550148890", "👍 to \"You still coming over\""), Times.Once);
    }

    [Fact]
    public async Task SendReactionCommand_truncates_a_long_target_message()
    {
        var sms = new Mock<ISmsService>();
        sms.Setup(s => s.GetMessagesAsync(1, null, It.IsAny<int>())).ReturnsAsync(new List<SmsMessage>());
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
        sms.Setup(s => s.GetMessagesAsync(1, null, It.IsAny<int>())).ReturnsAsync(new List<SmsMessage>
        {
            MakeMessage(1, "👍 to \"hi\"", DateTimeOffset.UtcNow, isOutgoing: true)
        });
        var viewModel = new ThreadDetailViewModel(sms.Object, new Mock<IMessageSchedulerService>().Object, threadId: 1, address: "5550148890");

        await viewModel.SendReactionCommand.ExecuteAsync(("👍", "hi"));

        Assert.Single(viewModel.Messages);
    }
}
