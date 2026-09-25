using Moq;
using ForgeLinkSms.Core.Data;
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;

namespace ForgeLinkSms.Core.Tests.Services;

public class SnoozeServiceTests
{
    private readonly Mock<ISnoozeRepository> _repository = new();
    private readonly Mock<ISnoozeAlarmScheduler> _alarms = new();
    private readonly Mock<IMarkAsReadService> _markAsRead = new();

    private SnoozeService MakeService() => new(_repository.Object, _alarms.Object, _markAsRead.Object);

    [Fact]
    public async Task SnoozeAsync_stores_the_snooze_and_arms_an_alarm()
    {
        var until = DateTimeOffset.UtcNow.AddHours(3);

        await MakeService().SnoozeAsync(7, until);

        _repository.Verify(r => r.SnoozeThreadAsync(7, until), Times.Once);
        _alarms.Verify(a => a.Arm(7, until), Times.Once);
    }

    [Fact]
    public async Task UnsnoozeAsync_removes_the_snooze_and_disarms_the_alarm()
    {
        await MakeService().UnsnoozeAsync(7);

        _repository.Verify(r => r.UnsnoozeThreadAsync(7), Times.Once);
        _alarms.Verify(a => a.Disarm(7), Times.Once);
    }

    [Fact]
    public async Task WakeAsync_unsnoozes_and_marks_the_thread_unread()
    {
        _repository.Setup(r => r.GetAsync(7)).ReturnsAsync(new SnoozedThread { ThreadId = 7, UntilUtc = DateTimeOffset.UtcNow });

        var woke = await MakeService().WakeAsync(7);

        Assert.True(woke);
        _repository.Verify(r => r.UnsnoozeThreadAsync(7), Times.Once);
        _markAsRead.Verify(m => m.MarkThreadsAsUnreadAsync(It.Is<IReadOnlyList<long>>(ids => ids.SequenceEqual(new long[] { 7 }))), Times.Once);
    }

    [Fact]
    public async Task WakeAsync_does_nothing_when_the_thread_is_no_longer_snoozed()
    {
        var woke = await MakeService().WakeAsync(7);

        Assert.False(woke);
        _markAsRead.Verify(m => m.MarkThreadsAsUnreadAsync(It.IsAny<IReadOnlyList<long>>()), Times.Never);
    }

    [Fact]
    public async Task WakeExpiredAsync_wakes_only_snoozes_that_are_due()
    {
        var now = DateTimeOffset.UtcNow;
        var due = new SnoozedThread { ThreadId = 1, UntilUtc = now.AddMinutes(-1) };
        var future = new SnoozedThread { ThreadId = 2, UntilUtc = now.AddHours(1) };
        _repository.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<SnoozedThread> { due, future });
        _repository.Setup(r => r.GetAsync(1)).ReturnsAsync(due);

        await MakeService().WakeExpiredAsync(now);

        _repository.Verify(r => r.UnsnoozeThreadAsync(1), Times.Once);
        _repository.Verify(r => r.UnsnoozeThreadAsync(2), Times.Never);
    }

    [Fact]
    public async Task RearmAllAsync_arms_every_stored_snooze()
    {
        var until = DateTimeOffset.UtcNow.AddHours(1);
        _repository.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<SnoozedThread> { new() { ThreadId = 3, UntilUtc = until } });

        await MakeService().RearmAllAsync();

        _alarms.Verify(a => a.Arm(3, until), Times.Once);
    }
}
