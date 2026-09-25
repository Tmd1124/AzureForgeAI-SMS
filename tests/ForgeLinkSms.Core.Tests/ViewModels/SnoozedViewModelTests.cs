using Moq;
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;
using ForgeLinkSms.Core.ViewModels;

namespace ForgeLinkSms.Core.Tests.ViewModels;

public class SnoozedViewModelTests
{
    private static SmsThread MakeThread(long id, string name) => new()
    {
        Id = id,
        Address = "555",
        DisplayName = name,
        LastMessageBody = "hi",
        LastMessageTimestamp = DateTimeOffset.UtcNow,
        UnreadCount = 0
    };

    [Fact]
    public async Task LoadCommand_lists_snoozed_threads_soonest_first()
    {
        var now = DateTimeOffset.UtcNow;
        var snooze = new Mock<ISnoozeService>();
        snooze.Setup(s => s.GetSnoozedAsync()).ReturnsAsync(new Dictionary<long, DateTimeOffset>
        {
            [1] = now.AddDays(2),
            [3] = now.AddHours(1)
        });
        var threads = new Mock<IThreadService>();
        threads.Setup(t => t.GetThreadsAsync()).ReturnsAsync(new List<SmsThread> { MakeThread(1, "Mom"), MakeThread(2, "Jake"), MakeThread(3, "Sarah") });
        var viewModel = new SnoozedViewModel(snooze.Object, threads.Object);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "Sarah", "Mom" }, viewModel.SnoozedThreads.Select(s => s.Thread.DisplayName));
        Assert.Equal(now.AddHours(1), viewModel.SnoozedThreads[0].UntilUtc);
    }

    [Fact]
    public async Task UnsnoozeCommand_unsnoozes_and_reloads()
    {
        var snooze = new Mock<ISnoozeService>();
        snooze.Setup(s => s.GetSnoozedAsync()).ReturnsAsync(new Dictionary<long, DateTimeOffset>());
        var viewModel = new SnoozedViewModel(snooze.Object, new Mock<IThreadService>().Object);

        await viewModel.UnsnoozeCommand.ExecuteAsync(4L);

        snooze.Verify(s => s.UnsnoozeAsync(4), Times.Once);
        snooze.Verify(s => s.GetSnoozedAsync(), Times.Once);
    }
}
